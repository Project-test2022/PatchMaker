using System.Collections.Generic;

namespace PatchMaker.Model
{
    public sealed class PatchArchiveEntry
    {
        public string ArchiveName { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        public List<PatchFileEntry> Files { get; set; }
    }
}
