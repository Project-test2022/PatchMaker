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

            var oldMap = Utility.Utility.FilesUnder(config.OldDir, config.ExcludeGlobs)
                .ToDictionary(path => Utility.Utility.GetRelativePath(config.OldDir, path).Replace('\\', '/'));

            var newMap = Utility.Utility.FilesUnder(config.NewDir, config.ExcludeGlobs)
                .ToDictionary(path => Utility.Utility.GetRelativePath(config.NewDir, path).Replace('\\', '/'));

            var fileEntries = new List<PatchFileEntry>();
            var addedFiles = new List<string>();
            var removedFiles = new List<string>();

            // --- 差分ファイル作成 ---
            foreach (var pair in newMap)
            {
                var relativePath = pair.Key;
                var newPath = pair.Value;

                if (!oldMap.TryGetValue(relativePath, out var oldPath))
                {
                    // 新規追加ファイル
                    addedFiles.Add(relativePath);
                    continue;
                }

                var baseHash = Hash.Sha256(oldPath);
                var newHash = Hash.Sha256(newPath);

                if (baseHash == newHash)
                {
                    continue; // 同一ファイルはスキップ
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

                fileEntries.Add(new PatchFileEntry
                {
                    Path = relativePath,
                    BaseSha256 = baseHash,
                    NewSha256 = newHash,
                    Delta = deltaFileName,
                    IsAdded = false,
                    IsRemoved = false
                });
            }

            // --- 追加ファイル登録 ---
            foreach (var addedPath in addedFiles)
            {
                var newFullPath = newMap[addedPath];
                var safeFileName = addedPath.Replace("/", "__");
                var fullFileName = $"{safeFileName}.v{config.BaseVersion}_to_v{config.Version}.full";
                var fullTempPath = Path.Combine(config.OutDir, fullFileName);

                File.Copy(newFullPath, fullTempPath, true);
                var newHash = Hash.Sha256(newFullPath);

                fileEntries.Add(new PatchFileEntry
                {
                    Path = addedPath,
                    BaseSha256 = null,
                    NewSha256 = newHash,
                    Delta = fullFileName,
                    IsAdded = true,
                    IsRemoved = false
                });
            }

            // --- 削除ファイル登録 ---
            foreach (var oldPath in oldMap.Keys)
            {
                if (!newMap.ContainsKey(oldPath))
                {
                    removedFiles.Add(oldPath);
                    var oldFullPath = oldMap[oldPath];
                    var oldHash = Hash.Sha256(oldFullPath);

                    fileEntries.Add(new PatchFileEntry
                    {
                        Path = oldPath,
                        BaseSha256 = oldHash,
                        NewSha256 = null,
                        Delta = null,
                        IsAdded = false,
                        IsRemoved = true
                    });
                }
            }

            // --- ZIP作成 ---
            var zipName = $"patch_v{config.BaseVersion}_to_v{config.Version}.zip";
            var zipPath = Path.Combine(config.OutDir, zipName);

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var entry in fileEntries.Where(x => !x.IsRemoved))
                {
                    var srcPath = Path.Combine(config.OutDir, entry.Delta ?? string.Empty);
                    if (File.Exists(srcPath))
                    {
                        zip.CreateEntryFromFile(srcPath, entry.Delta, CompressionLevel.Fastest);
                        File.Delete(srcPath);
                    }
                }
            }

            var zipSize = new FileInfo(zipPath).Length;
            var zipSha256 = Hash.Sha256(zipPath);

            // --- マニフェスト生成 ---
            var manifest = new ManifestZip
            {
                Version = config.Version,
                BaseFrom = config.BaseVersion,
                PatchArchives = new List<PatchArchiveEntry>
                {
                    new PatchArchiveEntry
                    {
                        ArchiveName = zipName,
                        Url = "./" + zipName,
                        Size = zipSize,
                        Sha256 = zipSha256,
                        Files = fileEntries
                    }
                },
                Mandatory = false
            };

            var manifestPath = Path.Combine(config.OutDir, "latest.json");
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, manifestJson);

            // --- コンソール出力 ---
            Console.WriteLine("=== 差分作成完了 ===");
            Console.WriteLine($"変更ファイル数: {fileEntries.Count}");

            if (addedFiles.Count > 0)
            {
                Console.WriteLine($"追加ファイル数: {addedFiles.Count}");
            }

            if (removedFiles.Count > 0)
            {
                Console.WriteLine($"削除ファイル数: {removedFiles.Count}");
            }

            Console.WriteLine("出力先: " + Path.GetFullPath(config.OutDir));
            Console.WriteLine("マニフェスト: " + Path.GetFullPath(manifestPath));

            return true;
        }
    }
}
