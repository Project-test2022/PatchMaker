using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PatchMaker.Model
{
    public sealed class Manifest
    {
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("base_from")] public string BaseFrom { get; set; }
        [JsonPropertyName("patches")] public List<PatchEntry> Patches { get; set; }
        [JsonPropertyName("mandatory")] public bool Mandatory { get; set; }

        public Manifest()
        {
            Version = "";
            BaseFrom = "";
            Patches = new List<PatchEntry>();
            Mandatory = false;
        }
    }
}
