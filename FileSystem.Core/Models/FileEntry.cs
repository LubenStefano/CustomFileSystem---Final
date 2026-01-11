namespace FileSystem.Core.Models
{
    public class FileEntry
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public bool IsDirectory { get; set; }
        public Utils.Collections.SimpleList<int> BlockIndices { get; set; } = new Utils.Collections.SimpleList<int>();
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public int ParentDirectory { get; set; } = -1;
        public uint Checksum { get; set; }
    }
}
