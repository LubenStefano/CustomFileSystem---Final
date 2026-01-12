namespace FileSystem.Core
{
    public class BlockTable
    {
        private readonly string _path;
        private readonly int _blockSize;
        private readonly int _totalBlocks;

        public int BlockSize => _blockSize;
        private long TableOffset => Layout.BlockTableOffset;
        private long EntrySize => Layout.BlockTableEntrySize;


        public BlockTable(string path, int blockSize, int totalBlocks)
        {
            _path = path;
            _blockSize = blockSize;
            _totalBlocks = totalBlocks;
        }

        private void ValidateBlockIndex(int blockIndex)
        {
            if (blockIndex < 0 || blockIndex >= _totalBlocks)
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
        }

        public int FindFreeBlock()
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite);
            using var br = new BinaryReader(fs);

            for (int i = 0; i < _totalBlocks; i++)
            {
                long metaPos = TableOffset + i * EntrySize;

                // If the metadata area for this entry is beyond file length, stop.
                if (metaPos + EntrySize > fs.Length) break;

                fs.Seek(metaPos, SeekOrigin.Begin);

                byte isUsed = br.ReadByte();

                if (isUsed == 0)
                {
                    return i;
                }
            }

            throw new IOException("No free blocks available!");
        }

        public void Allocate(int blockIndex)
        {
            ValidateBlockIndex(blockIndex);

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            long metaPos = TableOffset + blockIndex * EntrySize;
            if (metaPos + EntrySize > fs.Length) throw new IOException("Block table area is truncated");

            fs.Seek(metaPos, SeekOrigin.Begin);

            bw.Write((byte)1);  // isUsed
            bw.Write(1);        // refcount
            bw.Write((uint)0);  // later checksum

            bw.Flush();
            fs.Flush();
        }

        public void Deallocate(int blockIndex)
        {
            ValidateBlockIndex(blockIndex);

            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                long dataPos = GetDataOffset(blockIndex);
                if (dataPos + _blockSize <= fs.Length)
                {
                    fs.Seek(dataPos, SeekOrigin.Begin);

                    bw.Write(new byte[_blockSize]);
                    
                    bw.Flush();
                    fs.Flush();
                }

                long metaPos = TableOffset + blockIndex * EntrySize;

                if (metaPos + EntrySize > fs.Length) throw new IOException("Block table area is truncated");

                fs.Seek(metaPos, SeekOrigin.Begin);
                bw.Write((byte)0);
                bw.Write(0);
                bw.Write((uint)0);

                bw.Flush();
                fs.Flush();
            }
        }

        public void SetChecksum(int blockIndex, uint checksum)
        {
            ValidateBlockIndex(blockIndex);

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            long metaPos = TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryChecksumOffset;
            if (metaPos + sizeof(uint) > fs.Length) throw new IOException("Block table area is truncated");

            fs.Seek(metaPos, SeekOrigin.Begin);

            bw.Write(checksum);
            bw.Flush();

            fs.Flush();
        }

        public int GetRefCount(int blockIndex)
        {
            ValidateBlockIndex(blockIndex);

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            long metaPos = TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryRefCountOffset;
            if (metaPos + sizeof(int) > fs.Length) throw new IOException("Block table area is truncated");

            fs.Seek(metaPos, SeekOrigin.Begin);

            return br.ReadInt32();
        }

        public void IncrementRefCount(int blockIndex)
        {
            int current = GetRefCount(blockIndex);

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            long metaPos = TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryRefCountOffset;
            if (metaPos + sizeof(int) > fs.Length) throw new IOException("Block table area is truncated");

            fs.Seek(metaPos, SeekOrigin.Begin);

            bw.Write(current + 1);
            bw.Flush();

            fs.Flush();
        }

        public int DecrementRefCount(int blockIndex)
        {
            int current = GetRefCount(blockIndex);
            int updated = Math.Max(0, current - 1);

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);

            using var bw = new BinaryWriter(fs);
            long metaPos = TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryRefCountOffset;
            if (metaPos + sizeof(int) > fs.Length) throw new IOException("Block table area is truncated");
            fs.Seek(metaPos, SeekOrigin.Begin);

            bw.Write(updated);
            bw.Flush();

            fs.Flush();

            return updated;
        }

        public int FindBlockByChecksumAndData(uint checksum, byte[] data, int validLength)
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            if (data == null) throw new ArgumentNullException(nameof(data));
            if (validLength <= 0 || validLength > _blockSize) return -1;

            for (int i = 0; i < _totalBlocks; i++)
            {
                long metaPos = TableOffset + i * EntrySize;

                if (metaPos + EntrySize > fs.Length) break;

                fs.Seek(metaPos, SeekOrigin.Begin);

                byte isUsed = br.ReadByte();
                int refCount = br.ReadInt32();
                uint storedChecksum = br.ReadUInt32();

                if (isUsed == 1 && refCount > 0 && storedChecksum == checksum)
                {
                    long dataPos = GetDataOffset(i);

                    if (dataPos + validLength > fs.Length) continue;

                    fs.Seek(dataPos, SeekOrigin.Begin);

                    byte[] existing = br.ReadBytes(validLength);

                    bool equal = true;
                    for (int b = 0; b < validLength; b++)
                    {
                        if (existing[b] != data[b]) { equal = false; break; }

                    }
                    if (equal) return i;
                }
            }

            return -1;
        }

        public uint GetChecksum(int blockIndex)
        {
            ValidateBlockIndex(blockIndex);

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            long metaPos = TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryChecksumOffset;
            if (metaPos + sizeof(uint) > fs.Length) throw new IOException("Block table area is truncated");

            fs.Seek(metaPos, SeekOrigin.Begin);

            return br.ReadUInt32();
        }

        public int GetUsedBlocksCount()
        {
            int usedCount = 0;
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            for (int i = 0; i < _totalBlocks; i++)
            {
                long seekPos = TableOffset + i * EntrySize;

                if (seekPos >= fs.Length)
                    break;

                fs.Seek(seekPos, SeekOrigin.Begin);

                if (fs.Position >= fs.Length)
                    break;

                byte isUsed = br.ReadByte();
                if (isUsed == 1)
                {
                    usedCount++;
                }
            }
            return usedCount;
        }

        public long GetDataOffset(int blockIndex)
        {
            ValidateBlockIndex(blockIndex);
            return Layout.GetDataOffset(blockIndex, _totalBlocks, _blockSize);
        }

        public void WriteBlockData(int blockIndex, byte[] data, int offset = 0)
        {
            ValidateBlockIndex(blockIndex);
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset >= _blockSize) throw new ArgumentOutOfRangeException(nameof(offset));

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);

            long dataOffset = GetDataOffset(blockIndex);
            fs.Seek(dataOffset + offset, SeekOrigin.Begin);

            int bytesToWrite = Math.Min(data.Length, _blockSize - offset);
            if (bytesToWrite > 0) fs.Write(data, 0, bytesToWrite);
        }

        public byte[] ReadBlockData(int blockIndex)
        {
            ValidateBlockIndex(blockIndex);

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);

            long dataOffset = GetDataOffset(blockIndex);

            byte[] buffer = new byte[_blockSize];
            if (dataOffset >= fs.Length) return buffer;

            fs.Seek(dataOffset, SeekOrigin.Begin);

            int bytesRead = fs.Read(buffer, 0, _blockSize);

            if (bytesRead < _blockSize)
            {
                for (int i = bytesRead; i < _blockSize; i++) buffer[i] = 0;
            }

            return buffer;
        }

        public uint CalculateChecksum(byte[] data, int length)
        {
            uint checksum = 0;
            int len = Math.Min(length, data.Length);

            for (int i = 0; i < len; i++)
            {
                checksum ^= (uint)(data[i] * 31);
            }

            return checksum;
        }

        public void InitializeBlockTable()
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            for (int i = 0; i < _totalBlocks; i++)
            {
                fs.Seek(TableOffset + i * EntrySize, SeekOrigin.Begin);

                bw.Write((byte)0);
                bw.Write(0);
                bw.Write((uint)0);
            }
        }
    }
}
