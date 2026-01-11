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
        private int _currentDirectoryInode = 0; // Root directory inode
        private FileManager? _fileManager;
        private DirectoryManager? _directoryManager;
        private BlockTable? _blockTable;

        public void CopyFileIn(string sourcePath, string targetName)
        {
            if (_containerManager == null || _fileManager == null) throw new InvalidOperationException("No container is open");

            // Resolve source path relative to container directory
            string containerDir = TextUtils.GetDirectoryName(_containerPath) ?? "";
            string resolvedSourcePath = TextUtils.IsPathRooted(sourcePath) 
                ? sourcePath 
                : TextUtils.CombinePaths(containerDir, sourcePath);

            if (!File.Exists(resolvedSourcePath)) throw new FileNotFoundException($"Source file not found: {resolvedSourcePath}");

            using var sourceStream = new FileStream(resolvedSourcePath, FileMode.Open, FileAccess.Read);
            long fileSize = sourceStream.Length;

            // Create file entry (reserve inode) and write blocks before committing metadata
            Console.WriteLine($"DEBUG: CopyFileIn - current directory inode={_currentDirectoryInode}, targetName={targetName}, fileSize={fileSize}");
            // Prevent duplicate filenames in the same directory
            if (_fileManager.FindFileByName(_currentDirectoryInode, targetName) != -1)
            {
                throw new InvalidOperationException($"A file named '{targetName}' already exists in the current directory.");
            }

            // Reserve inode slot without committing metadata yet
            int fileInode = _fileManager.ReserveFileSlot();
            Console.WriteLine($"DEBUG: CopyFileIn - reserved file inode={fileInode} under parent {_currentDirectoryInode}");

            // Write journal entry to mark in-progress inode
            _containerManager.WriteJournalInode(fileInode);

            // Build file entry in-memory
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
            // Delegate block-level streaming + dedup to FileManager
            _fileManager.WriteFileData(fileInode, sourceStream, fileEntry);

            // Clear journal (commit complete)
            _containerManager.ClearJournal();

            // After write, report block table usage
            if (_blockTable != null)
            {
                int used = _blockTable.GetUsedBlocksCount();
                Console.WriteLine($"DEBUG: CopyFileIn - BlockTable used blocks after write: {used}");
            }
        }

        public void CopyFileOut(string fileName, string targetPath)
        {
            if (_containerManager == null || _fileManager == null) throw new InvalidOperationException("No container is open");

            int fileInode = _fileManager.FindFileByName(_currentDirectoryInode, fileName);
            
            if (fileInode == -1)  throw new FileNotFoundException($"File not found: {fileName}");

            // Resolve target path relative to container directory
            string containerDir = TextUtils.GetDirectoryName(_containerPath) ?? "";
            string resolvedTargetPath = TextUtils.IsPathRooted(targetPath) 
                ? targetPath 
                : TextUtils.CombinePaths(containerDir, targetPath);

                // Stream out using FileManager helper (reads blocks directly)
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

            // Get files in the current directory
            var files = _fileManager.GetFilesInDirectory(_currentDirectoryInode);
            for (int i = 0; i < files.Count; i++) result.Add(files[i]);

            // Get subdirectories in the current directory
            var currentDir = _directoryManager.GetDirectory(_currentDirectoryInode);
            if (currentDir != null)
            {
                Console.WriteLine($"DEBUG: Listing directory '{currentDir.Name}' with {currentDir.ChildInodes.Count} children.");
                for (int i = 0; i < currentDir.ChildInodes.Count; i++)
                {
                    int childInode = currentDir.ChildInodes[i];
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

            Console.WriteLine($"DEBUG: Found {result.Count} items in directory");
            return result;
        }

        public SimpleList<DirectoryEntry> ListAllDirectories()
        {
            if (_directoryManager == null) throw new InvalidOperationException("No container is open");

            var directories = new SimpleList<DirectoryEntry>();

            DirectoryEntry? GetDirectoryOrNull(int inode)
            {
                return _directoryManager.GetDirectory(inode);
            }

            void WalkDirectory(DirectoryEntry dir)
            {
                directories.Add(dir);
                for (int i = 0; i < dir.ChildInodes.Count; i++)
                {
                    var childInode = dir.ChildInodes[i];
                    var child = GetDirectoryOrNull(childInode);
                    if (child != null)
                    {
                        WalkDirectory(child);
                    }
                }
            }

            var root = GetDirectoryOrNull(0) ?? _directoryManager.GetDirectory(0);
            
            if (root != null)
            {
                WalkDirectory(root);
            }

            return directories;
        }

        public void CreateDirectory(string directoryName)
        {
            if (_containerManager == null || _directoryManager == null)
                throw new InvalidOperationException("No container is open");

            int newInode = _directoryManager.CreateDirectory(directoryName, _currentDirectoryInode);

            // Debug: Confirm creation and list children
            var currentDir = _directoryManager.GetDirectory(_currentDirectoryInode);
            if (currentDir != null)
            {
                Console.WriteLine($"DEBUG: Directory '{directoryName}' created with inode {newInode}.Current directory now has {currentDir.ChildInodes.Count} children.");
            }
        }

        public void ChangeDirectory(string directoryName)
        {
            if (_containerManager == null || _directoryManager == null)
                throw new InvalidOperationException("No container is open");

            if (directoryName == "..")
            {
                // Go to parent directory
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
                // Navigate to child directory
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
                
                // insert at front by shifting
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

            // Recursively delete all contents (files and subdirectories) under this directory
            var deletedDirectoryInodes = new SimpleSetInt();

            void DeleteDirectoryRecursive(int inode)
            {
                var dir = _directoryManager.GetDirectory(inode);
                if (dir == null)
                {
                    // Still record inode so we can sweep any files that might reference it
                    deletedDirectoryInodes.Add(inode);
                    return;
                }

                // Record this directory inode as deleted (will be removed)
                deletedDirectoryInodes.Add(inode);

                // Delete files in this directory
                if (_fileManager != null)
                {
                    var fileInodes = _fileManager.GetFileInodesInDirectory(inode);
                    for (int i = 0; i < fileInodes.Count; i++)
                    {
                        var fInode = fileInodes[i];
                        try { _fileManager.DeleteFile(fInode); } catch { /* ignore individual failures */ }
                    }
                }

                // Recursively delete child directories
                var children = new SimpleList<int>(dir.ChildInodes.Count + 2);
                for (int i = 0; i < dir.ChildInodes.Count; i++) children.Add(dir.ChildInodes[i]);
                for (int i = 0; i < children.Count; i++) DeleteDirectoryRecursive(children[i]);

                // Remove this directory from its parent (if any)
                if (dir.ParentInode >= 0)
                {
                    _directoryManager.RemoveChildFromDirectory(dir.ParentInode, inode);
                }

                // Finally mark directory slot as deleted
                _directoryManager.DeleteDirectory(inode);
            }

            DeleteDirectoryRecursive(childInode);

            // Sweep: ensure there are no orphaned files that reference any deleted directory inode.
            // This protects against the case where directory inodes are reused and leftover file entries
            // still reference the old inode number as ParentDirectory.
            if (_fileManager != null)
            {
                try
                {
                    var allFileInodes = _fileManager.GetAllFileInodes();
                    for (int i = 0; i < allFileInodes.Count; i++)
                    {
                        var fInode = allFileInodes[i];
                        try
                        {
                            var fe = _fileManager.GetFileEntry(fInode);
                            if (fe != null && deletedDirectoryInodes.Contains(fe.ParentDirectory))
                            {
                                // Delete the orphaned file entry and free its blocks
                                _fileManager.DeleteFile(fInode);
                            }
                        }
                        catch { /* ignore per-file failures */ }
                    }
                }
                catch { /* ignore sweep failures */ }
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
            _containerManager = new ContainerManager(path, 0, 0); // Will load from superblock
            var superblock = _containerManager.LoadSuperblock();
            _containerPath = path;
            _currentDirectory = "/";
            _currentDirectoryInode = 0;
            
            InitializeManagers(superblock.BlockSize, superblock.TotalBlocks);
            
            // Check if root directory is properly initialized
            var rootDir = _directoryManager?.GetDirectory(0);
            if (rootDir == null)
            {
                Console.WriteLine("DEBUG: Root directory not found or corrupted, initializing...");
                _directoryManager?.InitializeRootDirectory();
            }
            else
            {
                Console.WriteLine($"DEBUG: Root directory loaded successfully with {rootDir.ChildInodes.Count} children");
            }

            // Recovery: check for an in-progress inode in the journal and roll it back
            try
            {
                int journalInode = _containerManager.ReadJournalInode();
                if (journalInode >= 0)
                {
                    Console.WriteLine($"RECOVER: Found in-progress inode {journalInode} in journal. Rolling back...");

                    if (_fileManager != null && _blockTable != null && _directoryManager != null)
                    {
                        // Load file entry and free its blocks
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
            
            // Create DirectoryManager first
            _directoryManager = new DirectoryManager(_containerPath, totalBlocks);
            
            // Pass DirectoryManager to FileManager to ensure they use the same instance
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
