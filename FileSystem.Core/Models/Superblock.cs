namespace FileSystem.Core.Models
{
    public class Superblock
    {
        public uint MagicNumber { get; set; } = 0xCAFEBABE; // Unique signature
        public int BlockSize { get; set; }
        public int TotalBlocks { get; set; }
        public int RootDirectoryInode { get; set; } = 0; // root = inode 0
        public int Version { get; set; } = 1;
    }
}
