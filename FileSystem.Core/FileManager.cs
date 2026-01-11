using FileSystem.Core.Models;
using System.Text;
using FileSystem.Core.Utils.Collections;

namespace FileSystem.Core
{
    public class FileManager
    {
        private readonly string _containerPath = "";
        private readonly BlockTable _blockTable = null!;
        private readonly DirectoryManager _directoryManager = null!;
        private readonly int _blockSize;
        private readonly int _totalBlocks;

        public FileManager(string containerPath, int blockSize, int totalBlocks, DirectoryManager directoryManager)
        {
            _containerPath = containerPath;
            _blockSize = blockSize;
            _totalBlocks = totalBlocks;
            _blockTable = new BlockTable(containerPath, blockSize, totalBlocks);
            _directoryManager = directoryManager;
        }

        public void WriteFileData(int fileInode, Stream sourceStream, FileEntry fileEntry)
        {
            if (fileEntry == null) throw new ArgumentException("FileEntry required");

            long totalBytesWritten = 0;
            byte[] buffer = new byte[_blockTable.BlockSize];

            while (totalBytesWritten < fileEntry.Size)
            {
                int bytesToRead = (int)Math.Min(buffer.Length, fileEntry.Size - totalBytesWritten);
                int bytesRead = sourceStream.Read(buffer, 0, bytesToRead);

                if (bytesRead == 0) break;

                var fullBlockForHash = new byte[_blockSize];

                Array.Clear(fullBlockForHash, 0, fullBlockForHash.Length);
                Array.Copy(buffer, 0, fullBlockForHash, 0, bytesRead);

                uint checksum = _blockTable.CalculateChecksum(fullBlockForHash, bytesRead);

                int found = _blockTable.FindBlockByChecksumAndData(checksum, fullBlockForHash, bytesRead);
                if (found != -1)
                {
                    _blockTable.IncrementRefCount(found);
                    fileEntry.BlockIndices!.Add(found);
                }
                else
                {
                    int free = _blockTable.FindFreeBlock();
                    _blockTable.Allocate(free);
                    _blockTable.WriteBlockData(free, fullBlockForHash, 0);
                    _blockTable.SetChecksum(free, checksum);
                    fileEntry.BlockIndices!.Add(free);
                }

                totalBytesWritten += bytesRead;
            }

            fileEntry.ModifiedDate = DateTime.UtcNow;
            SaveFileEntry(fileInode, fileEntry);

            // Commit: add inode to parent directory now that data+metadata are persisted
            try
            {
                _directoryManager.AddChildToDirectory(fileEntry.ParentDirectory, fileInode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARN: Failed to add inode {fileInode} to directory {fileEntry.ParentDirectory}: {ex.Message}");
            }
        }

        // ReadFileData removed in favor of block-level helpers like CopyFileOutToPath.

        // Stream file out by reading container blocks directly and writing them to host path
        public void CopyFileOutToPath(int fileInode, string resolvedTargetPath)
        {
            var fileEntry = GetFileEntry(fileInode) ?? throw new ArgumentException("File not found");
            using var dst = new FileStream(resolvedTargetPath, FileMode.Create, FileAccess.Write);

            long remaining = fileEntry.Size;
            int bs = _blockTable.BlockSize;

            for (int i = 0; i < fileEntry.BlockIndices.Count && remaining > 0; i++)
            {
                int physicalBlock = fileEntry.BlockIndices[i];
                byte[] blockData = _blockTable.ReadBlockData(physicalBlock);

                int toWrite = (int)Math.Min(remaining, bs);

                // Validate checksum for the bytes we will write (handles partial final block)
                uint stored = _blockTable.GetChecksum(physicalBlock);
                uint calc = _blockTable.CalculateChecksum(blockData, toWrite);
                if (stored != calc)
                {
                    Console.WriteLine($"ERROR: Block {physicalBlock} checksum mismatch (stored={stored} calc={calc})");
                    throw new InvalidDataException($"Block {physicalBlock} is corrupted (checksum mismatch).");
                }

                dst.Write(blockData, 0, toWrite);
                remaining -= toWrite;
            }
            dst.Flush();
        }

        public void DeleteFile(int fileInode)
        {
            var fileEntry = GetFileEntry(fileInode);
            if (fileEntry == null) return;

            if (fileEntry.BlockIndices != null)
            {
                for (int i = 0; i < fileEntry.BlockIndices.Count; i++)
                {
                    int bidx = fileEntry.BlockIndices[i];
                    int remaining = _blockTable.DecrementRefCount(bidx);
                    if (remaining == 0)
                    {
                        _blockTable.Deallocate(bidx);
                    }
                }
            }

            _directoryManager.RemoveChildFromDirectory(fileEntry.ParentDirectory, fileInode);
            DeleteFileEntry(fileInode);
        }

        public FileEntry? GetFileEntry(int fileInode)
        {
            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            long offset = Layout.FileAreaOffset(_totalBlocks) + (fileInode * Layout.FileEntrySize);
            if (offset >= fs.Length) return null;

            fs.Seek(offset, SeekOrigin.Begin);

            int nameLen = br.ReadInt32();
            if (nameLen <= 0 || nameLen > Layout.MaxFileNameLength) return null;
            if (fs.Position + nameLen > fs.Length) return null;

            var nameBytes = br.ReadBytes(nameLen);
            string name = Encoding.UTF8.GetString(nameBytes);

            if (fs.Position + Layout.FileSizeFieldSize + Layout.IsDirectoryFieldSize + Layout.BlockCountFieldSize > fs.Length) return null;
            
            long size = br.ReadInt64();
            bool isDir = br.ReadBoolean();
            int blockCount = br.ReadInt32();
            var blocks = new SimpleList<int>(blockCount + 2);
            
            for (int i = 0; i < blockCount; i++)
            {
                if (fs.Position + Layout.BlockIndexFieldSize > fs.Length) break;
                blocks.Add(br.ReadInt32());
            }

            if (fs.Position + Layout.DateTimeFieldSize + Layout.DateTimeFieldSize + Layout.ParentDirectoryFieldSize + Layout.ChecksumFieldSize > fs.Length) return null;
            
            DateTime created = DateTime.FromBinary(br.ReadInt64());
            DateTime modified = DateTime.FromBinary(br.ReadInt64());
            
            int parent = br.ReadInt32();
            uint checksum = br.ReadUInt32();

            return new FileEntry
            {
                Name = name,
                Size = size,
                IsDirectory = isDir,
                BlockIndices = blocks,
                CreatedDate = created,
                ModifiedDate = modified,
                ParentDirectory = parent,
                Checksum = checksum
            };
        }

        public void SaveFileEntry(int fileInode, FileEntry fileEntry)
        {
            // Validate name length
            var nameBytes = Encoding.UTF8.GetBytes(fileEntry.Name ?? "");
            if (nameBytes.Length > Layout.MaxFileNameLength) throw new ArgumentException($"File name too long (max {Layout.MaxFileNameLength} bytes)");

            // Estimate total size of the file entry when serialized
            long estimatedSize = Layout.NameLengthSize + nameBytes.Length
                + Layout.FileSizeFieldSize + Layout.IsDirectoryFieldSize
                + Layout.BlockCountFieldSize + (long)(fileEntry.BlockIndices?.Count ?? 0) * Layout.BlockIndexFieldSize
                + Layout.DateTimeFieldSize + Layout.DateTimeFieldSize + Layout.ParentDirectoryFieldSize + Layout.ChecksumFieldSize;

            if (estimatedSize > Layout.FileEntrySize) throw new InvalidOperationException($"FileEntry exceeds slot size ({estimatedSize} > {Layout.FileEntrySize})");

            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            long offset = Layout.FileAreaOffset(_totalBlocks) + (fileInode * Layout.FileEntrySize);
            fs.Seek(offset, SeekOrigin.Begin);

            bw.Write(nameBytes.Length);
            bw.Write(nameBytes);
            bw.Write(fileEntry.Size);
            bw.Write(fileEntry.IsDirectory);

            var blocks = fileEntry.BlockIndices!;
            int bc = blocks.Count;

            bw.Write(bc);

            for (int i = 0; i < bc; i++) bw.Write(blocks[i]);

            bw.Write(fileEntry.CreatedDate.ToBinary());
            bw.Write(fileEntry.ModifiedDate.ToBinary());
            bw.Write(fileEntry.ParentDirectory);
            bw.Write(fileEntry.Checksum);
        }

        public void DeleteFileEntry(int fileInode)
        {
            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);
            
            long offset = Layout.FileAreaOffset(_totalBlocks) + (fileInode * Layout.FileEntrySize);
            
            fs.Seek(offset, SeekOrigin.Begin);
            bw.Write(0);
        }

        // Expose reservation of a free file slot without writing metadata yet
        public int ReserveFileSlot()
        {
            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);
            
            for (int i = 0; i < Layout.MaxFiles; i++)
            {
                long offset = Layout.FileAreaOffset(_totalBlocks) + (i * Layout.FileEntrySize);
                
                if (offset + Layout.NameLengthSize >= fs.Length) return i;
                
                fs.Seek(offset, SeekOrigin.Begin);
                
                int nameLen = br.ReadInt32();
                if (nameLen == 0) return i;
            }
            
            throw new InvalidOperationException("No free file slots available");
        }

        public SimpleList<FileEntry> GetFilesInDirectory(int directoryInode)
        {
            var files = new SimpleList<FileEntry>();
            
            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);
            
            for (int fileInode = 0; fileInode < Layout.MaxFiles; fileInode++)
            {
                long offset = Layout.FileAreaOffset(_totalBlocks) + (fileInode * Layout.FileEntrySize);
                
                if (offset + Layout.NameLengthSize >= fs.Length) break;
                
                fs.Seek(offset, SeekOrigin.Begin);
                
                int nameLen = br.ReadInt32();
                
                if (nameLen <= 0 || nameLen > Layout.MaxFileNameLength) continue;
                
                var fe = GetFileEntry(fileInode);
                
                if (fe != null && fe.ParentDirectory == directoryInode && !fe.IsDirectory) files.Add(fe);
            }
            return files;
        }

        public SimpleList<int> GetFileInodesInDirectory(int directoryInode)
        {
            var fileInodes = new SimpleList<int>();
            
            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);
            
            for (int fileInode = 0; fileInode < Layout.MaxFiles; fileInode++)
            {
                long offset = Layout.FileAreaOffset(_totalBlocks) + (fileInode * Layout.FileEntrySize);
                
                if (offset + Layout.NameLengthSize >= fs.Length) break;
                
                fs.Seek(offset, SeekOrigin.Begin);
                
                int nameLen = br.ReadInt32();
                
                if (nameLen <= 0 || nameLen > Layout.MaxFileNameLength) continue;
                
                fs.Seek(nameLen, SeekOrigin.Current);
                
                long size = br.ReadInt64();
                bool isDir = br.ReadBoolean();
                int blockCount = br.ReadInt32();
                
                for (int i = 0; i < blockCount; i++) {
                    if (fs.Position + Layout.BlockIndexFieldSize > fs.Length) break;
                    
                    fs.Seek(Layout.BlockIndexFieldSize, SeekOrigin.Current);
                }
                
                long created = br.ReadInt64();
                long modified = br.ReadInt64();
                int parentDirectory = br.ReadInt32();
                uint checksum = br.ReadUInt32();
                
                if (!isDir && parentDirectory == directoryInode) fileInodes.Add(fileInode);
            }
            
            return fileInodes;
        }

        public SimpleList<int> GetAllFileInodes()
        {
            var list = new SimpleList<int>();
            
            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);
            
            for (int fileInode = 0; fileInode < Layout.MaxFiles; fileInode++)
            {
                long offset = Layout.FileAreaOffset(_totalBlocks) + (fileInode * Layout.FileEntrySize);
                
                if (offset + Layout.NameLengthSize >= fs.Length) break;
                
                fs.Seek(offset, SeekOrigin.Begin);
                
                int nameLen = br.ReadInt32();
                
                if (nameLen == 0 || nameLen > Layout.MaxFileNameLength) continue;
                
                list.Add(fileInode);
            }
            
            return list;
        }

        public int FindFileByName(int directoryInode, string fileName)
        {
            for (int fileInode = Layout.MaxFiles - 1; fileInode >= 0; fileInode--)
            {
                var entry = GetFileEntry(fileInode);
                
                if (entry != null && entry.Name == fileName && entry.ParentDirectory == directoryInode && !entry.IsDirectory) {
                    return fileInode;
                }
            }
            
            return -1;
        }
    }
}
