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

        public int FindFreeBlock()
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite);
            using var br = new BinaryReader(fs);

            for (int i = 0; i < _totalBlocks; i++)
            {
                fs.Seek(TableOffset + i * EntrySize, SeekOrigin.Begin);

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
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            fs.Seek(TableOffset + blockIndex * EntrySize, SeekOrigin.Begin);

            bw.Write((byte)1);  // mark used
            bw.Write(1);        // refcount = 1
            bw.Write((uint)0);  // checksum = set later

            bw.Flush();
            fs.Flush();
        }

        public void Deallocate(int blockIndex)
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            fs.Seek(TableOffset + blockIndex * EntrySize, SeekOrigin.Begin);

            bw.Write((byte)0);  // mark as free
            bw.Write(0);        // refcount = 0
            bw.Write((uint)0);  // checksum = 0

            bw.Flush();
            fs.Flush();
        }

        public void SetChecksum(int blockIndex, uint checksum)
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            fs.Seek(TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryChecksumOffset, SeekOrigin.Begin); // Skip IsUsed + RefCount

            bw.Write(checksum);
            bw.Flush();

            fs.Flush();
        }

        public int GetRefCount(int blockIndex)
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            fs.Seek(TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryRefCountOffset, SeekOrigin.Begin); // after IsUsed

            return br.ReadInt32();
        }

        public void IncrementRefCount(int blockIndex)
        {
            int current = GetRefCount(blockIndex);

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            fs.Seek(TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryRefCountOffset, SeekOrigin.Begin);

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
            fs.Seek(TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryRefCountOffset, SeekOrigin.Begin);

            bw.Write(updated);
            bw.Flush();

            fs.Flush();

            return updated;
        }

        // Find a block that has identical data (compare up to provided length) and same checksum
        public int FindBlockByChecksumAndData(uint checksum, byte[] data, int validLength)
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

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
                    // Read block data and compare
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
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            fs.Seek(TableOffset + blockIndex * EntrySize + Layout.BlockTableEntryChecksumOffset, SeekOrigin.Begin);

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

                // Check if we're beyond file bounds
                if (seekPos >= fs.Length)
                    break;

                fs.Seek(seekPos, SeekOrigin.Begin);

                // Check if we can read at least 1 byte
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
            return Layout.GetDataOffset(blockIndex, _totalBlocks, _blockSize);
        }

        public void WriteBlockData(int blockIndex, byte[] data, int offset = 0)
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write);

            long dataOffset = GetDataOffset(blockIndex);
            fs.Seek(dataOffset + offset, SeekOrigin.Begin);

            int bytesToWrite = Math.Min(data.Length, _blockSize - offset);
            fs.Write(data, 0, bytesToWrite);
        }

        public byte[] ReadBlockData(int blockIndex)
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);

            long dataOffset = GetDataOffset(blockIndex);

            // Check if we're trying to read beyond file bounds
            if (dataOffset >= fs.Length) return new byte[_blockSize];

            fs.Seek(dataOffset, SeekOrigin.Begin);

            byte[] buffer = new byte[_blockSize];
            int bytesRead = fs.Read(buffer, 0, _blockSize);

            // If we read less than block size, fill the rest with zeros
            if (bytesRead < _blockSize)
            {
                for (int i = bytesRead; i < _blockSize; i++)
                {
                    buffer[i] = 0;
                }
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

                bw.Write((byte)0);  // IsUsed = false
                bw.Write(0);        // RefCount = 0
                bw.Write((uint)0);  // Checksum = 0
            }
        }
    }
}
