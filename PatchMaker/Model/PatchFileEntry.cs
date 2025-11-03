namespace PatchMaker.Model
{
    public sealed class PatchFileEntry
    {
        public string Path { get; set; }
        public string BaseSha256 { get; set; }
        public string NewSha256 { get; set; }
        public string Delta { get; set; }
        public bool IsAdded { get; set; }
    }
}
