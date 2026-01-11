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
            if (_blockSize <= 0 || _totalBlocks <= 0)
            {
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

            //superblock
            bw.Write(_superblock.MagicNumber);
            bw.Write(_superblock.BlockSize);
            bw.Write(_superblock.TotalBlocks);
            bw.Write(_superblock.RootDirectoryInode);
            bw.Write(_superblock.Version);

            // journal
            bw.Write(-1);

            long dataAreaSize = Layout.DataAreaSize(_totalBlocks, _blockSize);

            long totalSize = Layout.DataAreaOffset(_totalBlocks) + dataAreaSize;

            fs.SetLength(totalSize);
            fs.Close();

            var blockTable = new BlockTable(_path, _blockSize, _totalBlocks);
            blockTable.InitializeBlockTable();

            var directoryManager = new DirectoryManager(_path, _totalBlocks);
            directoryManager.InitializeRootDirectory();
        }

        public Superblock LoadSuperblock()
        {
            if (!File.Exists(_path))
            {
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

            if (sb.MagicNumber != 0xCAFEBABE)
            {
                throw new IOException("Invalid container format!");
            }

            if (sb.BlockSize <= 0 || sb.TotalBlocks <= 0)
            {
                throw new IOException("Corrupted superblock: invalid block size or total blocks.");
            }

            _superblock = sb;
            return sb;
        }

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
                return -1;
            }
            catch (Exception ex)
            {
                throw new IOException("Failed to read journal inode.", ex);
            }
        }

        public void ClearJournal()
        {
            WriteJournalInode(-1);
        }
    }
}
