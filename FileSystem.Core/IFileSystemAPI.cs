using FileSystem.Core.Models;

namespace FileSystem.Core
{
    public interface IFileSystemAPI
    {
        // File operations
        void CopyFileIn(string sourcePath, string targetName);
        void CopyFileOut(string fileName, string targetPath);
        void DeleteFile(string fileName);
        Utils.Collections.SimpleList<FileEntry> ListCurrentDirectory();
        
        // Directory operations
        void CreateDirectory(string directoryName);
        void ChangeDirectory(string directoryName);
        void RemoveDirectory(string directoryName);
        Utils.Collections.SimpleList<DirectoryEntry> ListAllDirectories();
        string GetCurrentPath();
        
        // Container operations
        void CreateContainer(string path, int blockSize, int totalBlocks);
        void OpenContainer(string path);
        void CloseContainer();
        
        // Status
        ContainerInfo GetContainerInfo();
    }
    
    public class ContainerInfo
    {
        public string Path { get; set; } = "";
        public int BlockSize { get; set; }
        public int TotalBlocks { get; set; }
        public int UsedBlocks { get; set; }
        public int FreeBlocks => TotalBlocks - UsedBlocks;
        public string CurrentDirectory { get; set; } = "/";
    }
}
