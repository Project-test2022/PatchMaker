using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PatchMaker.Utility
{
    public static class Hash
    {
        public static string Sha256(string file)
        {
            using (var fs = File.OpenRead(file))
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
