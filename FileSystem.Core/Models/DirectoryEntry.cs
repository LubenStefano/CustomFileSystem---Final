namespace FileSystem.Core.Models
{
    public class DirectoryEntry
    {
        public string Name { get; set; } = "";
        public int InodeIndex { get; set; }
        public bool IsDirectory { get; set; }
        public int ParentInode { get; set; } = -1;
        public Utils.Collections.SimpleList<int> ChildInodes { get; set; } = new Utils.Collections.SimpleList<int>();
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}
