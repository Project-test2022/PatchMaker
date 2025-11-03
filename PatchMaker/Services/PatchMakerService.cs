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
                Error("旧バージョンのディレクトリが存在しません: " + config.OldDir);
                return false;
            }
            if (!Directory.Exists(config.NewDir))
            {
                Error("新バージョンのディレクトリが存在しません: " + config.NewDir);
                return false;
            }
            Directory.CreateDirectory(config.OutDir);

            var oldMap = FilesUnder(config.OldDir, config.ExcludeGlobs)
                .ToDictionary(path => GetRelativePath(config.OldDir, path).Replace('\\', '/'));
            var newMap = FilesUnder(config.NewDir, config.ExcludeGlobs)
                .ToDictionary(path => GetRelativePath(config.NewDir, path).Replace('\\', '/'));

            var fileEntries = new List<PatchFileEntry>();
            var added = new List<string>();
            var removed = new List<string>();

            foreach (var pair in newMap)
            {
                var relativePath = pair.Key;
                var newPath = pair.Value;

                string oldPath;
                if (!oldMap.TryGetValue(relativePath, out oldPath))
                {
                    added.Add(relativePath);
                    continue;
                }

                var baseHash = Hash.Sha256(oldPath);
                var newHash = Hash.Sha256(newPath);
                if (baseHash == newHash)
                {
                    continue;
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
                    Delta = deltaFileName
                });
            }

            // 削除ファイル一覧
            foreach (var path in oldMap.Keys)
            {
                if (!newMap.ContainsKey(path))
                {
                    removed.Add(path);
                }
            }

            // ZIP作成
            var zipName = $"patch_v{config.BaseVersion}_to_v{config.Version}.zip";
            var zipPath = Path.Combine(config.OutDir, zipName);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var entry in fileEntries)
                {
                    var deltaPath = Path.Combine(config.OutDir, entry.Delta);
                    if (File.Exists(deltaPath))
                    {
                        zip.CreateEntryFromFile(deltaPath, entry.Delta, CompressionLevel.Fastest);
                        File.Delete(deltaPath); // ZIP化後削除してクリーンに
                    }
                }
            }

            var zipSize = new FileInfo(zipPath).Length;
            var zipSha256 = Hash.Sha256(zipPath);

            // マニフェスト生成（新形式）
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

            if (added.Count > 0)
                File.WriteAllLines(Path.Combine(config.OutDir, "ADDED_FILES.txt"), added.OrderBy(x => x));

            if (removed.Count > 0)
                File.WriteAllLines(Path.Combine(config.OutDir, "REMOVED_FILES.txt"), removed.OrderBy(x => x));

            Console.WriteLine("=== 差分作成完了 ===");
            Console.WriteLine($"変更ファイル数: {fileEntries.Count}");
            if (added.Count > 0)
                Console.WriteLine($"追加ファイル数: {added.Count} （ADDED_FILES.txt 参照）");
            if (removed.Count > 0)
                Console.WriteLine($"削除ファイル数: {removed.Count} （REMOVED_FILES.txt 参照）");
            Console.WriteLine("出力先: " + Path.GetFullPath(config.OutDir));
            Console.WriteLine("マニフェスト: " + Path.GetFullPath(manifestPath));

            return true;
        }

        // --- ユーティリティ類 ---
        private static IEnumerable<string> FilesUnder(string root, List<string> excludes)
        {
            var all = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            var list = new List<string>(all.Length);

            foreach (var path in all)
            {
                if (!IsExcluded(GetRelativePath(root, path), excludes))
                {
                    list.Add(path);
                }
            }
            return list;
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(basePath));
            var fullUri = new Uri(fullPath);
            var relativeUri = baseUri.MakeRelativeUri(fullUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());
            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                return path + Path.DirectorySeparatorChar;
            }
            return path;
        }

        private static bool IsExcluded(string path, List<string> excludes)
        {
            var unixPath = path.Replace('\\', '/');
            foreach (var pattern in excludes)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;

                var keyword = pattern.Replace("**/", "/").Replace("**", "");
                if (unixPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Error(string message)
        {
            Console.Error.WriteLine("Error: " + message);
        }
    }

    // --- 新マニフェスト用モデル定義 ---
    public sealed class ManifestZip
    {
        public string Version { get; set; }
        public string BaseFrom { get; set; }
        public List<PatchArchiveEntry> PatchArchives { get; set; }
        public bool Mandatory { get; set; }
    }

    public sealed class PatchArchiveEntry
    {
        public string ArchiveName { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        public List<PatchFileEntry> Files { get; set; }
    }

    public sealed class PatchFileEntry
    {
        public string Path { get; set; }
        public string BaseSha256 { get; set; }
        public string NewSha256 { get; set; }
        public string Delta { get; set; }
    }
}
