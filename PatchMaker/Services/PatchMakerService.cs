using System;
using System.Collections.Generic;
using System.IO;
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

            var oldMap = FilesUnder(config.OldDir, config.ExcludeGlobs).ToDictionary(path => GetRelativePath(config.OldDir, path).Replace('\\', '/'));
            var newMap = FilesUnder(config.NewDir, config.ExcludeGlobs).ToDictionary(path => GetRelativePath(config.NewDir, path).Replace('\\', '/'));

            var patches = new List<PatchEntry>();
            var added = new List<string>();
            var removed = new List<string>();

            foreach(var pair in newMap)
            {
                var relativePath = pair.Key;
                var newPath = pair.Value;

                string oldPath;
                if(!oldMap.TryGetValue(relativePath, out oldPath))
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
                var signaturePath = Path.Combine(config.OutDir, safeFileName + ".v" + config.BaseVersion + ".signature");
                var deltaPath = Path.Combine(config.OutDir, safeFileName + ".v" + config.BaseVersion + "_to_v" + config.Version + ".octodelta");

                Console.WriteLine("差分生成中: " + relativePath);

                // 署名作成
                using (var baseFileStream = File.OpenRead(oldPath))
                using (var signatureStream = File.Create(signaturePath))
                {
                    var signatureBuilder = new SignatureBuilder();
                    signatureBuilder.Build(baseFileStream, new SignatureWriter(signatureStream));
                }

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

                var entry = new PatchEntry();
                entry.Path = relativePath;
                entry.BaseSha256 = baseHash;
                entry.NewSha256 = newHash;
                entry.Url = "./" + Path.GetFileName(deltaPath);
                entry.Size = new FileInfo(deltaPath).Length;
                patches.Add(entry);
            }

            foreach (var path in oldMap.Keys)
            {
                if (!newMap.ContainsKey(path))
                {
                    removed.Add(path);
                }
            }

            var manifest = new Manifest();
            manifest.Version = config.Version;
            manifest.BaseFrom = config.BaseVersion;
            manifest.Patches = patches;
            manifest.Mandatory = false;

            var manifestPath = Path.Combine(config.OutDir, "latest.json");
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, manifestJson);

            if (added.Count > 0)
            {
                File.WriteAllLines(Path.Combine(config.OutDir, "ADDED_FILES.txt"), added.OrderBy(x => x));
            }

            if (removed.Count > 0)
            {
                File.WriteAllLines(Path.Combine(config.OutDir, "REMOVED_FILES.txt"), removed.OrderBy(x => x));
            }

            Console.WriteLine("=== 差分作成完了 ===");
            Console.WriteLine("変更ファイル数: " + patches.Count);
            if (added.Count > 0)
            {
                Console.WriteLine("追加ファイル数: " + added.Count + " （ADDED_FILES.txt 参照）");
            }
            if (removed.Count > 0)
            {
                Console.WriteLine("削除ファイル数: " + removed.Count + " （REMOVED_FILES.txt 参照）");
            }
            Console.WriteLine("出力先: " + Path.GetFullPath(config.OutDir));
            Console.WriteLine("マニフェスト: " + Path.GetFullPath(manifestPath));

            return true;
        }

        private static IEnumerable<string> FilesUnder(string root, List<string> excludes)
        {
            var all = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            var list = new List<string>(all.Length);
            
            foreach(var path in all)
            {
                if(!IsExcluded(GetRelativePath(root, path), excludes))
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
}
