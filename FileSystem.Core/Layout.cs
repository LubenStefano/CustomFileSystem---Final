namespace FileSystem.Core
{
    public static class Layout
    {
        // Superblock: Magic (4) + BlockSize (4) + TotalBlocks (4) + RootDirectoryInode (4) + Version (4) = 20
        public const int SuperblockSize = 20;
        public const int JournalSize = 4; // single int32
        // Default values used across the project when creating containers
        public const int DefaultBlockSize = 1000;
        public const int DefaultTotalBlocks = 1000;

        // Block table 
        public const int BlockTableEntryIsUsedSize = 1;
        public const int BlockTableEntryRefCountSize = 4;
        public const int BlockTableEntryChecksumSize = 4;
        public const int BlockTableEntrySize = BlockTableEntryIsUsedSize + BlockTableEntryRefCountSize + BlockTableEntryChecksumSize;
        public const int BlockTableEntryRefCountOffset = BlockTableEntryIsUsedSize;
        public const int BlockTableEntryChecksumOffset = BlockTableEntryIsUsedSize + BlockTableEntryRefCountSize;

        // Offsets
        public static int BlockTableOffset => SuperblockSize + JournalSize; // usually 24
        public static int JournalOffset => SuperblockSize;

        // Directory area
        public const int DirectoryEntrySize = 1024;
        public const int MaxDirectories = 1000;
        public const int MaxDirectoryNameLength = 250;
        public const int MaxChildrenPerDirectory = 100;
        public static long DirectoryAreaSize => (long)DirectoryEntrySize * MaxDirectories;
        // Directory metadata sizes: parentInode(4) + createdDate(8) + modifiedDate(8) + childCount(4)
        public const int DirectoryMetadataHeaderSize = 4 + 8 + 8; // parent, created, modified
        public const int DirectoryMetadataSize = DirectoryMetadataHeaderSize + 4; // + childCount

        // File entries
        public const int FileEntrySize = 512;
        public const int MaxFiles = 1000;
        public const int MaxFileNameLength = 500;
        public static long FileEntriesAreaSize => (long)FileEntrySize * MaxFiles;

        // Computed offsets depending on total blocks and block size
        public static long BlockTableSize(int totalBlocks) => (long)totalBlocks * BlockTableEntrySize;
        public static long DirectoryAreaOffset(int totalBlocks) => BlockTableOffset + BlockTableSize(totalBlocks);
        public static long FileAreaOffset(int totalBlocks) => DirectoryAreaOffset(totalBlocks) + DirectoryAreaSize;
        public static long DataAreaOffset(int totalBlocks) => FileAreaOffset(totalBlocks) + FileEntriesAreaSize;
        public static long DataAreaSize(int totalBlocks, int blockSize) => (long)blockSize * totalBlocks;

        // Primitive sizes (centralized to avoid magic numbers)
        public const int SizeOfInt = 4;
        public const int SizeOfLong = 8;
        public const int SizeOfBool = 1;
        public const int SizeOfUInt = 4;

        // Field-specific sizes (semantic names to replace magic numbers)
        public const int NameLengthSize = SizeOfInt;
        public const int FileSizeFieldSize = SizeOfLong;
        public const int IsDirectoryFieldSize = SizeOfBool;
        public const int BlockCountFieldSize = SizeOfInt;
        public const int BlockIndexFieldSize = SizeOfInt;
        public const int DateTimeFieldSize = SizeOfLong;
        public const int ParentDirectoryFieldSize = SizeOfInt;
        public const int ChecksumFieldSize = SizeOfUInt;
        public const int ChildInodeFieldSize = SizeOfInt;

        public static long GetDataOffset(int blockIndex, int totalBlocks, int blockSize)
        {
            return DataAreaOffset(totalBlocks) + (long)blockIndex * blockSize;
        }
    }
}
