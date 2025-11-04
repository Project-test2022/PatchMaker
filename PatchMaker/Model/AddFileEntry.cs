using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PatchMaker.Model
{
    public sealed class AddFileEntry
    {
        /// <summary>ZIP内でのパス</summary>
        public string ZipPath { get; set; }
        /// <summary>展開後に配置する先</summary>
        public string TargetPath { get; set; }
    }
}
