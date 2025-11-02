using System.Text.Json.Serialization;

namespace PatchMaker.Model
{
    public sealed class PatchEntry
    {
        [JsonPropertyName("path")] public string Path { get; set; }
        [JsonPropertyName("base_sha256")] public string BaseSha256 { get; set; }
        [JsonPropertyName("new_sha256")] public string NewSha256 { get; set; }
        [JsonPropertyName("url")] public string Url { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }

        public PatchEntry()
        {
            Path = "";
            BaseSha256 = "";
            NewSha256 = "";
            Url = "";
            Size = 0;
        }
    }
}
