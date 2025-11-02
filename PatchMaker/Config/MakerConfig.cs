using System.Collections.Generic;

namespace PatchMaker.Config
{
    public sealed class MakerConfig
    {
        public string OldDir { get; set; }
        public string NewDir { get; set; }
        public string OutDir { get; set; }
        public string Version { get; set; }
        public string BaseVersion { get; set; }
        public List<string> ExcludeGlobs { get; set; }

        public MakerConfig()
        {
            OldDir = "";
            NewDir = "";
            OutDir = "dist";
            Version = "";
            BaseVersion = "";
            ExcludeGlobs = new List<string>();
        }
    }
}
