using FileSystem.Core.Models;
using FileSystem.Core.Utils.Collections;
using System.Text;

namespace FileSystem.Core
{
    public class DirectoryManager
    {
        private readonly string _containerPath;
        private readonly int _totalBlocks;

        public DirectoryManager(string containerPath, int totalBlocks)
        {
            _containerPath = containerPath;
            _totalBlocks = totalBlocks;
        }

        public DirectoryEntry? GetDirectory(int inodeIndex)
        {
            if (inodeIndex < 0 || inodeIndex >= Layout.MaxDirectories) return null;

            if (inodeIndex == 0)
            {
                using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                long offset = Layout.DirectoryAreaOffset(_totalBlocks);


                if (offset >= fs.Length)
                {
                    return null;
                }

                fs.Seek(offset, SeekOrigin.Begin);

                int nameLength;
                try
                {
                    nameLength = br.ReadInt32();
                }
                catch (Exception)
                {
                    return null;
                }


                if (nameLength < 0 || nameLength > Layout.MaxDirectoryNameLength || nameLength == 0)
                {
                    return null;
                }

                try
                {
                    if (fs.Position + nameLength > fs.Length)
                    {
                        return null;
                    }

                    byte[] nameBytes = br.ReadBytes(nameLength);
                    string name = Encoding.UTF8.GetString(nameBytes);

                    if (fs.Position + Layout.DirectoryMetadataSize > fs.Length)
                    {
                        return null;
                    }

                    int parentInode = br.ReadInt32();

                    DateTime createdDate = DateTime.FromBinary(br.ReadInt64());
                    DateTime modifiedDate = DateTime.FromBinary(br.ReadInt64());

                    int childCount = br.ReadInt32();

                    if (childCount < 0 || childCount > Layout.MaxChildrenPerDirectory)
                    {
                        return null;
                    }

                    var childInodes = new SimpleList<int>();
                    for (int i = 0; i < Layout.MaxChildrenPerDirectory; i++)
                    {
                        if (fs.Position + Layout.ChildInodeFieldSize > fs.Length) break;

                        int childInode = br.ReadInt32();

                        if (i < childCount && childInode != -1)
                        {
                            childInodes.Add(childInode);
                        }
                    }

                    return new DirectoryEntry
                    {
                        Name = name,
                        InodeIndex = 0,
                        IsDirectory = true,
                        ParentInode = parentInode,
                        ChildInodes = childInodes,
                        CreatedDate = createdDate,
                        ModifiedDate = modifiedDate
                    };
                }
                catch (Exception)
                {
                    return null;
                }
            }

            using var fs2 = new FileStream(_containerPath, FileMode.Open, FileAccess.Read);
            using var br2 = new BinaryReader(fs2);

            long offset2 = Layout.DirectoryAreaOffset(_totalBlocks) + (inodeIndex * Layout.DirectoryEntrySize);

            if (offset2 >= fs2.Length) return null;

            fs2.Seek(offset2, SeekOrigin.Begin);

            int nameLength2 = br2.ReadInt32();
            if (nameLength2 == 0 || nameLength2 > Layout.MaxDirectoryNameLength) return null;

            if (fs2.Position + nameLength2 > fs2.Length) return null;

            byte[] nameBytes2 = br2.ReadBytes(nameLength2);
            string name2 = Encoding.UTF8.GetString(nameBytes2);

            if (fs2.Position + Layout.DirectoryMetadataSize > fs2.Length) return null;

            int parentInode2 = br2.ReadInt32();
            DateTime createdDate2 = DateTime.FromBinary(br2.ReadInt64());
            DateTime modifiedDate2 = DateTime.FromBinary(br2.ReadInt64());

            int childCount2 = br2.ReadInt32();
            if (childCount2 < 0 || childCount2 > Layout.MaxChildrenPerDirectory) return null;


            var childInodes2 = new SimpleList<int>();

            for (int i = 0; i < Layout.MaxChildrenPerDirectory; i++)
            {
                if (fs2.Position + Layout.ChildInodeFieldSize > fs2.Length) break;

                int childInode = br2.ReadInt32();

                if (i < childCount2 && childInode != -1)
                {
                    childInodes2.Add(childInode);
                }
            }

            return new DirectoryEntry
            {
                Name = name2,
                InodeIndex = inodeIndex,
                IsDirectory = true,
                ParentInode = parentInode2,
                ChildInodes = childInodes2,
                CreatedDate = createdDate2,
                ModifiedDate = modifiedDate2
            };
        }

        public int CreateDirectory(string name, int parentInode)
        {
            int newInode = FindFreeDirectorySlot();
            if (newInode == -1) throw new InvalidOperationException("No free directory slots available");

            var newDir = new DirectoryEntry
            {
                Name = name,
                IsDirectory = true,
                InodeIndex = newInode,
                ParentInode = parentInode,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                ChildInodes = new SimpleList<int>()
            };

            SaveDirectory(newDir);

            AddChildToDirectory(parentInode, newInode);

            return newInode;
        }

        public void SaveDirectory(DirectoryEntry directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));

            if (directory.InodeIndex < 0 || directory.InodeIndex >= Layout.MaxDirectories)
                throw new ArgumentException("Invalid inode index.");

            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            long offset = Layout.DirectoryAreaOffset(_totalBlocks) + (directory.InodeIndex * Layout.DirectoryEntrySize);
            fs.Seek(offset, SeekOrigin.Begin);

            byte[] nameBytes = Encoding.UTF8.GetBytes(directory.Name);
            bw.Write(nameBytes.Length);
            bw.Write(nameBytes);

            bw.Write(directory.ParentInode);
            bw.Write(directory.CreatedDate.ToBinary());
            bw.Write(directory.ModifiedDate.ToBinary());

            int childCount = directory.ChildInodes.Count;
            bw.Write(childCount);

            for (int i = 0; i < childCount; i++)
            {
                bw.Write(directory.ChildInodes[i]);
            }
            for (int i = childCount; i < Layout.MaxChildrenPerDirectory; i++)
            {
                bw.Write(-1);
            }
        }

        public void DeleteDirectory(int inodeIndex)
        {
            if (inodeIndex < 0 || inodeIndex >= Layout.MaxDirectories) throw new ArgumentException("Invalid inode index.");

            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            long offset = Layout.DirectoryAreaOffset(_totalBlocks) + (inodeIndex * Layout.DirectoryEntrySize);
            fs.Seek(offset, SeekOrigin.Begin);

            bw.Write(0);
        }

        private int FindFreeDirectorySlot()
        {
            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            for (int i = 1; i < Layout.MaxDirectories; i++) // Start from 1, 0 is root
            {
                long offset = Layout.DirectoryAreaOffset(_totalBlocks) + (i * Layout.DirectoryEntrySize);

                if (offset >= fs.Length) break;

                fs.Seek(offset, SeekOrigin.Begin);

                int nameLength = br.ReadInt32();

                if (nameLength == 0) return i;
            }

            throw new InvalidOperationException("No free directory slots available");
        }

        public void AddChildToDirectory(int parentInode, int childInode)
        {
            if (parentInode < 0 || parentInode >= Layout.MaxDirectories || childInode < 0 || childInode >= Layout.MaxDirectories) return;

            var parentDir = GetDirectory(parentInode);

            if (parentDir != null)
            {
                if (parentDir.ChildInodes.IndexOf(childInode) < 0)
                {
                    parentDir.ChildInodes.Add(childInode);
                    parentDir.ModifiedDate = DateTime.UtcNow;

                    string cl = "";
                    for (int ci = 0; ci < parentDir.ChildInodes.Count; ci++)
                    {
                        if (ci > 0) cl += ", ";
                        cl += parentDir.ChildInodes[ci].ToString();
                    }

                    SaveDirectory(parentDir);

                    var verifyDir = GetDirectory(parentInode);
                    if (verifyDir != null)
                    {
                        string clv = "";
                        for (int ci = 0; ci < verifyDir.ChildInodes.Count; ci++)
                        {
                            if (ci > 0) clv += ", ";
                            clv += verifyDir.ChildInodes[ci].ToString();
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Child {childInode} already exists in parent {parentInode}");
                }
            }
            else
            {
                Console.WriteLine($"Could not load parent directory {parentInode}");
            }
        }

        public void RemoveChildFromDirectory(int parentInode, int childInode)
        {
            if (parentInode < 0 || parentInode >= Layout.MaxDirectories || childInode < 0 || childInode >= Layout.MaxDirectories) return;

            var parentDir = GetDirectory(parentInode);

            if (parentDir != null)
            {
                parentDir.ChildInodes.Remove(childInode);
                parentDir.ModifiedDate = DateTime.Now;
                SaveDirectory(parentDir);
            }
        }

        public int FindChildByName(int parentInode, string name)
        {
            if (parentInode < 0 || parentInode >= Layout.MaxDirectories || Utils.TextUtils.IsNullOrWhiteSpace(name)) return -1;

            var parentDir = GetDirectory(parentInode);
            if (parentDir == null) return -1;

            for (int i = 0; i < parentDir.ChildInodes.Count; i++)
            {
                int childInode = parentDir.ChildInodes[i];
                var childDir = GetDirectory(childInode);

                if (childDir != null && childDir.Name == name)
                {
                    return childInode;
                }
            }

            return -1;
        }

        public void InitializeRootDirectory()
        {
            using var fs = new FileStream(_containerPath, FileMode.Open, FileAccess.ReadWrite);

            long requiredSize = Layout.DirectoryAreaOffset(_totalBlocks) + Layout.DirectoryAreaSize;

            if (fs.Length < requiredSize)
            {
                fs.SetLength(requiredSize);
            }
            fs.Close();

            var rootDir = new DirectoryEntry
            {
                Name = "/",
                InodeIndex = 0,
                IsDirectory = true,
                ParentInode = -1,
                ChildInodes = new SimpleList<int>(),
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            SaveDirectory(rootDir);
        }
    }
}
