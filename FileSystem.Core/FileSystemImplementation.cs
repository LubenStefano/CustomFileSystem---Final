using FileSystem.Core.Models;
using FileSystem.Core.Utils;
using FileSystem.Core.Utils.Collections;

namespace FileSystem.Core
{
    public class FileSystemImplementation : IFileSystemAPI
    {
        private ContainerManager? _containerManager;
        private string? _containerPath;
        private string _currentDirectory = "/";
        private int _currentDirectoryInode = 0;
        private FileManager? _fileManager;
        private DirectoryManager? _directoryManager;
        private BlockTable? _blockTable;

        public void CopyFileIn(string sourcePath, string targetName)
        {
            if (_containerManager == null || _fileManager == null) throw new InvalidOperationException("No container is open");

            string containerDir = TextUtils.GetDirectoryName(_containerPath) ?? "";
            string resolvedSourcePath = TextUtils.IsPathRooted(sourcePath)
                ? sourcePath
                : TextUtils.CombinePaths(containerDir, sourcePath);

            if (!File.Exists(resolvedSourcePath)) throw new FileNotFoundException($"Source file not found: {resolvedSourcePath}");

            using var sourceStream = new FileStream(resolvedSourcePath, FileMode.Open, FileAccess.Read);
            long fileSize = sourceStream.Length;

            if (_fileManager.FindFileByName(_currentDirectoryInode, targetName) != -1)
            {
                throw new InvalidOperationException($"A file named '{targetName}' already exists in the current directory.");
            }

            int fileInode = _fileManager.ReserveFileSlot();

            _containerManager.WriteJournalInode(fileInode);

            var fileEntry = new FileEntry
            {
                Name = targetName,
                Size = fileSize,
                IsDirectory = false,
                BlockIndices = new SimpleList<int>(),
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                ParentDirectory = _currentDirectoryInode,
                Checksum = 0
            };

            _fileManager.WriteFileData(fileInode, sourceStream, fileEntry);

            _containerManager.ClearJournal();
        }

        public void CopyFileOut(string fileName, string targetPath)
        {
            if (_containerManager == null || _fileManager == null) throw new InvalidOperationException("No container is open");

            int fileInode = _fileManager.FindFileByName(_currentDirectoryInode, fileName);

            if (fileInode == -1) throw new FileNotFoundException($"File not found: {fileName}");

            string containerDir = TextUtils.GetDirectoryName(_containerPath) ?? "";
            string resolvedTargetPath = TextUtils.IsPathRooted(targetPath)
                ? targetPath
                : TextUtils.CombinePaths(containerDir, targetPath);

            _fileManager.CopyFileOutToPath(fileInode, resolvedTargetPath);
        }

        public void DeleteFile(string fileName)
        {
            if (_containerManager == null || _fileManager == null) throw new InvalidOperationException("No container is open");

            int fileInode = _fileManager.FindFileByName(_currentDirectoryInode, fileName);
            if (fileInode == -1) throw new FileNotFoundException($"File not found: {fileName}");

            _fileManager.DeleteFile(fileInode);
        }

        public SimpleList<FileEntry> ListCurrentDirectory()
        {
            if (_containerManager == null || _fileManager == null || _directoryManager == null) throw new InvalidOperationException("No container is open");

            var result = new SimpleList<FileEntry>();

            var files = _fileManager.GetFilesInDirectory(_currentDirectoryInode);
            for (int i = 0; i < files.Count; i++) result.Add(files[i]);

            var currentDir = _directoryManager.GetDirectory(_currentDirectoryInode);
            if (currentDir != null)
            {
                for (int i = 0; i < currentDir.ChildInodes.Count; i++)
                {
                    int childInode = currentDir.ChildInodes[i];

                    // Defensive: skip self-referential child entries which can
                    // appear due to corruption or logic errors. Showing the
                    // current directory as its own child confuses the UI.
                    if (childInode == _currentDirectoryInode) continue;

                    var childDir = _directoryManager.GetDirectory(childInode);

                    if (childDir != null)
                    {
                        result.Add(new FileEntry
                        {
                            Name = childDir.Name,
                            Size = 0, // Directories don't have size
                            IsDirectory = true,
                            CreatedDate = childDir.CreatedDate,
                            ModifiedDate = childDir.ModifiedDate,
                            ParentDirectory = _currentDirectoryInode
                        });
                    }
                }
            }

            return result;
        }

        public SimpleList<DirectoryEntry> ListAllDirectories()
        {
            if (_directoryManager == null) throw new InvalidOperationException("No container is open");

            var directories = new SimpleList<DirectoryEntry>();

            // Iterative BFS traversal with visited set to avoid recursion and protect
            // against cycles/corruption that would cause a StackOverflowException.
            var visited = new SimpleSetInt(16);
            var root = _directoryManager.GetDirectory(0);
            if (root == null) return directories;

            var queue = new SimpleQueue<int>();
            queue.Enqueue(root.InodeIndex);
            visited.Add(root.InodeIndex);

            while (queue.Count > 0)
            {
                var inode = queue.Dequeue();
                var dir = _directoryManager.GetDirectory(inode);
                if (dir == null) continue;

                directories.Add(dir);

                for (int i = 0; i < dir.ChildInodes.Count; i++)
                {
                    int child = dir.ChildInodes[i];
                    if (!visited.Contains(child))
                    {
                        visited.Add(child);
                        queue.Enqueue(child);
                    }
                }
            }

            return directories;
        }

        public void CreateDirectory(string directoryName)
        {
            if (_containerManager == null || _directoryManager == null)
                throw new InvalidOperationException("No container is open");

            _directoryManager.CreateDirectory(directoryName, _currentDirectoryInode);
        }

        public void ChangeDirectory(string directoryName)
        {
            if (_containerManager == null || _directoryManager == null)
                throw new InvalidOperationException("No container is open");

            if (directoryName == "..")
            {
                var currentDir = _directoryManager.GetDirectory(_currentDirectoryInode);
                if (currentDir != null && currentDir.ParentInode >= 0)
                {
                    _currentDirectoryInode = currentDir.ParentInode;
                    UpdateCurrentPath();
                }
            }
            else if (directoryName == "/" || directoryName == "\\")
            {
                _currentDirectoryInode = 0; // Root
                _currentDirectory = "/";
            }
            else
            {
                int childInode = _directoryManager.FindChildByName(_currentDirectoryInode, directoryName);
                if (childInode == -1)
                    throw new DirectoryNotFoundException($"Directory not found: {directoryName}");

                _currentDirectoryInode = childInode;
                UpdateCurrentPath();
            }
        }

        private void UpdateCurrentPath()
        {
            if (_directoryManager == null) return;

            var pathParts = new SimpleList<string>();
            int currentInode = _currentDirectoryInode;

            while (currentInode > 0)
            {
                var dir = _directoryManager.GetDirectory(currentInode);
                if (dir == null) break;

                var tmp = new SimpleList<string>(pathParts.Count + 1);
                tmp.Add(dir.Name);

                for (int i = 0; i < pathParts.Count; i++) tmp.Add(pathParts[i]);

                pathParts = tmp;
                currentInode = dir.ParentInode;
            }

            if (pathParts.Count > 0)
            {
                string p = "";
                for (int i = 0; i < pathParts.Count; i++)
                {
                    if (i > 0) p += "/";
                    p += pathParts[i];
                }
                _currentDirectory = "/" + p;
            }
            else _currentDirectory = "/";
        }

        public void RemoveDirectory(string directoryName)
        {
            if (_containerManager == null || _directoryManager == null)
                throw new InvalidOperationException("No container is open");

            if (_directoryManager == null) throw new InvalidOperationException("No container is open");

            int childInode = _directoryManager.FindChildByName(_currentDirectoryInode, directoryName);

            if (childInode == -1)
                throw new DirectoryNotFoundException($"Directory not found: {directoryName}");

            var deletedDirectoryInodes = new SimpleSetInt();

            void DeleteDirectoryRecursive(int inode)
            {
                var dir = _directoryManager.GetDirectory(inode);
                if (dir == null)
                {
                    deletedDirectoryInodes.Add(inode);
                    return;
                }

                deletedDirectoryInodes.Add(inode);

                if (_fileManager != null)
                {
                    var fileInodes = _fileManager.GetFileInodesInDirectory(inode);
                    for (int i = 0; i < fileInodes.Count; i++)
                    {
                        var fInode = fileInodes[i];
                        _fileManager.DeleteFile(fInode);
                    }
                }

                var children = new SimpleList<int>(dir.ChildInodes.Count + 2);

                for (int i = 0; i < dir.ChildInodes.Count; i++) children.Add(dir.ChildInodes[i]);

                for (int i = 0; i < children.Count; i++) DeleteDirectoryRecursive(children[i]);

                if (dir.ParentInode >= 0)
                {
                    _directoryManager.RemoveChildFromDirectory(dir.ParentInode, inode);
                }

                _directoryManager.DeleteDirectory(inode);
            }

            DeleteDirectoryRecursive(childInode);

            if (_fileManager != null)
            {
                var allFileInodes = _fileManager.GetAllFileInodes();
                for (int i = 0; i < allFileInodes.Count; i++)
                {
                    var fInode = allFileInodes[i];
                    var fe = _fileManager.GetFileEntry(fInode);
                    if (fe != null && deletedDirectoryInodes.Contains(fe.ParentDirectory))
                    {
                        // Delete the orphaned file entry and free its blocks
                        _fileManager.DeleteFile(fInode);
                    }
                }
            }
        }

        public string GetCurrentPath()
        {
            return _currentDirectory;
        }

        public void CreateContainer(string path, int blockSize, int totalBlocks)
        {
            _containerManager = new ContainerManager(path, blockSize, totalBlocks);
            _containerManager.CreateContainer();
            _containerPath = path;
            _currentDirectory = "/";
            _currentDirectoryInode = 0;

            InitializeManagers(blockSize, totalBlocks);
        }

        public void OpenContainer(string path)
        {
            _containerManager = new ContainerManager(path, 0, 0);
            var superblock = _containerManager.LoadSuperblock();
            _containerPath = path;
            _currentDirectory = "/";
            _currentDirectoryInode = 0;

            InitializeManagers(superblock.BlockSize, superblock.TotalBlocks);

            var rootDir = _directoryManager?.GetDirectory(0);
            if (rootDir == null)
            {
                _directoryManager?.InitializeRootDirectory();
            }

            try
            {
                int journalInode = _containerManager.ReadJournalInode();
                if (journalInode >= 0)
                {
                    Console.WriteLine($"RECOVER: Found in-progress inode {journalInode} in journal. Rolling back...");

                    if (_fileManager != null && _blockTable != null && _directoryManager != null)
                    {
                        var fe = _fileManager.GetFileEntry(journalInode);
                        if (fe != null)
                        {
                            for (int i = 0; i < fe.BlockIndices.Count; i++)
                            {
                                int b = fe.BlockIndices[i];
                                try
                                {
                                    int rem = _blockTable.DecrementRefCount(b);
                                    if (rem == 0) _blockTable.Deallocate(b);
                                }
                                catch { }
                            }
                        }

                        // Remove file entry and clear any directory references
                        try { _fileManager.DeleteFileEntry(journalInode); } catch { }
                        try { _directoryManager.RemoveChildFromDirectory(0, journalInode); } catch { }
                    }

                    _containerManager.ClearJournal();
                    Console.WriteLine("RECOVER: Rollback complete.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RECOVER: Error during recovery: {ex.Message}");
            }
        }

        private void InitializeManagers(int blockSize, int totalBlocks)
        {
            if (_containerPath == null) return;

            _directoryManager = new DirectoryManager(_containerPath, totalBlocks);

            _fileManager = new FileManager(_containerPath, blockSize, totalBlocks, _directoryManager);

            _blockTable = new BlockTable(_containerPath, blockSize, totalBlocks);
        }

        public void CloseContainer()
        {
            _containerManager = null;
            _containerPath = null;
            _currentDirectory = "/";
            _currentDirectoryInode = 0;
            _fileManager = null;
            _directoryManager = null;
            _blockTable = null;
        }

        public ContainerInfo GetContainerInfo()
        {
            if (_containerManager == null || _containerPath == null || _blockTable == null) throw new InvalidOperationException("No container is open");

            var superblock = _containerManager.LoadSuperblock();
            int usedBlocks = _blockTable.GetUsedBlocksCount();

            return new ContainerInfo
            {
                Path = _containerPath,
                BlockSize = superblock.BlockSize,
                TotalBlocks = superblock.TotalBlocks,
                UsedBlocks = usedBlocks,
                CurrentDirectory = _currentDirectory
            };
        }
    }
}
