using System;
using System.Collections.Generic;
using System.IO;

namespace PatchMaker.Utility
{
    public static class Utility
    {
        public static IEnumerable<string> FilesUnder(string root, List<string> excludes)
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

        public static string GetRelativePath(string basePath, string fullPath)
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(basePath));
            var fullUri = new Uri(fullPath);
            var relativeUri = baseUri.MakeRelativeUri(fullUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());
            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }

        public static string AppendDirectorySeparatorChar(string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
        }

        public static bool IsExcluded(string path, List<string> excludes)
        {
            var unixPath = path.Replace('\\', '/');

            foreach (var pattern in excludes)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                var keyword = pattern.Replace("**/", "/").Replace("**", "");
                if (unixPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
