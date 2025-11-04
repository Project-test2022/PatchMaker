using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PatchMaker.Model
{
    public sealed class AddArchiveEntry
    {
        public string ArchiveName { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }

        // ZIPに含まれるファイルと、配置先を対応付ける
        public List<AddFileEntry> Entries { get; set; }
    }
}
