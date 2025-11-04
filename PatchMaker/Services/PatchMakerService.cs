using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Octodiff.Core;
using Octodiff.Diagnostics;
using PatchMaker.Config;
using PatchMaker.Model;
using PatchMaker.Utility;

namespace PatchMaker.Services
{
    public sealed class PatchMakerService
    {
        public bool Run(MakerConfig config)
        {
            // --- 検証 ---
            if (!Directory.Exists(config.OldDir))
            {
                Log.Error("旧バージョンのディレクトリが存在しません: " + config.OldDir);
                return false;
            }

            if (!Directory.Exists(config.NewDir))
            {
                Log.Error("新バージョンのディレクトリが存在しません: " + config.NewDir);
                return false;
            }

            Directory.CreateDirectory(config.OutDir);

            // --- ファイルマッピング ---
            var oldMap = Utility.Utility.FilesUnder(config.OldDir, config.ExcludeGlobs)
                .ToDictionary(path => Utility.Utility.GetRelativePath(config.OldDir, path).Replace('\\', '/'));

            var newMap = Utility.Utility.FilesUnder(config.NewDir, config.ExcludeGlobs)
                .ToDictionary(path => Utility.Utility.GetRelativePath(config.NewDir, path).Replace('\\', '/'));

            var diffFiles = new List<PatchFileEntry>();
            var addedFiles = new List<string>();
            var removedFiles = new List<string>();

            // --- 差分生成 ---
            foreach (var pair in newMap)
            {
                var relativePath = pair.Key;
                var newPath = pair.Value;

                if (!oldMap.TryGetValue(relativePath, out var oldPath))
                {
                    continue; // 追加ファイル
                }

                var baseHash = Hash.Sha256(oldPath);
                var newHash = Hash.Sha256(newPath);
                if (baseHash == newHash)
                {
                    continue; // 同一
                }

                var safeFileName = relativePath.Replace("/", "__");
                var deltaFileName = $"{safeFileName}.v{config.BaseVersion}_to_v{config.Version}.octodelta";
                var deltaPath = Path.Combine(config.OutDir, deltaFileName);

                Console.WriteLine("差分生成中: " + relativePath);

                // 署名作成
                var signaturePath = Path.Combine(config.OutDir, safeFileName + ".v" + config.BaseVersion + ".signature");
                using (var baseFileStream = File.OpenRead(oldPath))
                using (var signatureStream = File.Create(signaturePath))
                {
                    var signatureBuilder = new SignatureBuilder();
                    signatureBuilder.Build(baseFileStream, new SignatureWriter(signatureStream));
                }

                // 差分生成
                using (var newFileStream = File.OpenRead(newPath))
                using (var deltaStream = File.Create(deltaPath))
                using (var signatureStream = File.OpenRead(signaturePath))
                {
                    var progress = new ConsoleProgressReporter();
                    var signatureReader = new SignatureReader(signatureStream, progress);
                    var deltaBuilder = new DeltaBuilder();
                    deltaBuilder.BuildDelta(newFileStream, signatureReader, new BinaryDeltaWriter(deltaStream));
                }

                File.Delete(signaturePath);

                diffFiles.Add(new PatchFileEntry
                {
                    Path = relativePath,
                    BaseSha256 = baseHash,
                    NewSha256 = newHash,
                    Delta = deltaFileName,
                    IsAdded = false,
                    IsRemoved = false
                });
            }

            // --- 追加ファイル ---
            foreach (var pair in newMap)
            {
                if (!oldMap.ContainsKey(pair.Key))
                {
                    addedFiles.Add(pair.Key);
                }
            }

            // --- 削除ファイル ---
            foreach (var oldPath in oldMap.Keys)
            {
                if (!newMap.ContainsKey(oldPath))
                {
                    removedFiles.Add(oldPath);
                }
            }

            // --- 差分ZIP ---
            string patchZipName = null;
            string patchZipPath = null;
            long patchZipSize = 0;
            string patchZipSha256 = null;

            if (diffFiles.Count > 0)
            {
                patchZipName = $"patch_v{config.BaseVersion}_to_v{config.Version}.zip";
                patchZipPath = Path.Combine(config.OutDir, patchZipName);
                if (File.Exists(patchZipPath))
                {
                    File.Delete(patchZipPath);
                }

                using (var zip = ZipFile.Open(patchZipPath, ZipArchiveMode.Create))
                {
                    foreach (var entry in diffFiles)
                    {
                        var srcPath = Path.Combine(config.OutDir, entry.Delta ?? string.Empty);
                        if (File.Exists(srcPath))
                        {
                            zip.CreateEntryFromFile(srcPath, entry.Delta, CompressionLevel.Fastest);
                            File.Delete(srcPath);
                        }
                    }
                }

                patchZipSize = new FileInfo(patchZipPath).Length;
                patchZipSha256 = Hash.Sha256(patchZipPath);
            }
            else
            {
                Console.WriteLine("差分ファイルが存在しないため、パッチZIPは作成されません。");
            }

            // --- 追加ZIP ---
            string addZipName = null;
            string addZipPath = null;
            long addZipSize = 0;
            string addZipSha256 = null;
            var addEntries = new List<AddFileEntry>();

            if (addedFiles.Count > 0)
            {
                addZipName = $"add_v{config.BaseVersion}_to_v{config.Version}.zip";
                addZipPath = Path.Combine(config.OutDir, addZipName);

                if (File.Exists(addZipPath))
                {
                    File.Delete(addZipPath);
                }

                using (var zip = ZipFile.Open(addZipPath, ZipArchiveMode.Create))
                {
                    foreach (var addedPath in addedFiles)
                    {
                        var fullPath = newMap[addedPath];
                        zip.CreateEntryFromFile(fullPath, addedPath.Replace('\\', '/'), CompressionLevel.Fastest);

                        addEntries.Add(new AddFileEntry
                        {
                            ZipPath = addedPath.Replace('\\', '/'),
                            TargetPath = addedPath.Replace('\\', '/')
                        });
                    }
                }

                addZipSize = new FileInfo(addZipPath).Length;
                addZipSha256 = Hash.Sha256(addZipPath);
            }

            // --- マニフェスト生成 ---
            var manifest = new ManifestZip
            {
                Version = config.Version,
                BaseFrom = config.BaseVersion,
                PatchArchives = new List<PatchArchiveEntry>
                {
                    new PatchArchiveEntry
                    {
                        ArchiveName = patchZipName,
                        Url = "./" + patchZipName,
                        Size = patchZipSize,
                        Sha256 = patchZipSha256,
                        Files = diffFiles
                    }
                },
                AddFiles = (addZipName != null)
                    ? new List<AddArchiveEntry>
                    {
                        new AddArchiveEntry
                        {
                            ArchiveName = addZipName,
                            Url = "./" + addZipName,
                            Size = addZipSize,
                            Sha256 = addZipSha256 ?? "",
                            Entries = addEntries
                        }
                    }
                    : new List<AddArchiveEntry>(),
                RemoveFiles = removedFiles,
                Mandatory = false
            };

            var manifestPath = Path.Combine(config.OutDir, "latest.json");
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, manifestJson);

            // --- コンソール出力 ---
            Console.WriteLine("=== 差分作成完了 ===");
            Console.WriteLine($"変更ファイル数: {diffFiles.Count}");
            Console.WriteLine($"追加ファイル数: {addedFiles.Count}");
            Console.WriteLine($"削除ファイル数: {removedFiles.Count}");
            Console.WriteLine("出力先: " + Path.GetFullPath(config.OutDir));
            Console.WriteLine("マニフェスト: " + Path.GetFullPath(manifestPath));

            return true;
        }
    }
}
