using FileSystem.Core.Models;

namespace FileSystem.Core
{
    public class ContainerManager
    {
        private readonly string _path;
        private readonly int _blockSize;
        private readonly int _totalBlocks;
        private Superblock? _superblock;

        public ContainerManager(string path, int blockSize, int totalBlocks)
        {
            _path = path;
            _blockSize = blockSize;
            _totalBlocks = totalBlocks;
        }

        public void CreateContainer()
        {
            if (_blockSize <= 0 || _totalBlocks <= 0){
                throw new ArgumentException("Block size and total blocks must be positive.");
            }

            _superblock = new Superblock
            {
                BlockSize = _blockSize,
                TotalBlocks = _totalBlocks,
                RootDirectoryInode = 0
            };

            using var fs = new FileStream(_path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // Write Superblock
            bw.Write(_superblock.MagicNumber);
            bw.Write(_superblock.BlockSize);
            bw.Write(_superblock.TotalBlocks);
            bw.Write(_superblock.RootDirectoryInode);
            bw.Write(_superblock.Version);

            // Initialize single-slot journal with -1 (empty)
            bw.Write(-1);

            // Calculate required container size using centralized Layout
            long dataAreaSize = Layout.DataAreaSize(_totalBlocks, _blockSize);

            // Reserve: data area starts at computed offset; total size = data start + data area size
            long totalSize = Layout.DataAreaOffset(_totalBlocks) + dataAreaSize;

            // Allocate full container size
            Console.WriteLine($"DEBUG: Creating container with size {totalSize} bytes");
            fs.SetLength(totalSize);
            fs.Close();

            // Initialize Block Table
            var blockTable = new BlockTable(_path, _blockSize, _totalBlocks);
            blockTable.InitializeBlockTable();

            // Initialize Directory Manager and create root directory
            var directoryManager = new DirectoryManager(_path, _totalBlocks);
            directoryManager.InitializeRootDirectory();
        }

        public Superblock LoadSuperblock()
        {
            if (!File.Exists(_path)) {
                throw new IOException("Container file does not exist.");
            }

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            var sb = new Superblock
            {
                MagicNumber = br.ReadUInt32(),
                BlockSize = br.ReadInt32(),
                TotalBlocks = br.ReadInt32(),
                RootDirectoryInode = br.ReadInt32(),
                Version = br.ReadInt32()
            };

            // Validate container signature
            if (sb.MagicNumber != 0xCAFEBABE) {
                throw new IOException("Invalid container format!");
            }

            if (sb.BlockSize <= 0 || sb.TotalBlocks <= 0) {
                throw new IOException("Corrupted superblock: invalid block size or total blocks.");
            }

            _superblock = sb;
            return sb;
        }

        // Journal helpers
        public void WriteJournalInode(int inode)
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            fs.Seek(Layout.JournalOffset, SeekOrigin.Begin);
            bw.Write(inode);

            fs.Flush();
            bw.Flush();
        }

        public int ReadJournalInode()
        {
            // If the container file doesn't exist or is too small to contain the journal,
            // treat the journal as empty and return -1.
            if (!File.Exists(_path)) return -1;

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            if (fs.Length < Layout.JournalOffset + sizeof(int)) return -1;

            fs.Seek(Layout.JournalOffset, SeekOrigin.Begin);

            try
            {
                return br.ReadInt32();
            }
            catch (EndOfStreamException)
            {
                // Truncated file: treat journal as empty
                return -1;
            }
            catch (Exception ex)
            {
                // Surface all other unexpected errors as IOExceptions for clarity
                throw new IOException("Failed to read journal inode.", ex);
            }
        }

        public void ClearJournal()
        {
            WriteJournalInode(-1);
        }
    }
}
