using System.Collections.Generic;

namespace PatchMaker.Model
{
    public sealed class ManifestZip
    {
        public string Version { get; set; }
        public string BaseFrom { get; set; }
        public List<PatchArchiveEntry> PatchArchives { get; set; }
        public List<AddArchiveEntry> AddFiles { get; set; }
        public List<string> RemoveFiles { get; set; }
        public bool Mandatory { get; set; }
    }
}
