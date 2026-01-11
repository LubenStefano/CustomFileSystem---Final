using FileSystem.Core.Models;
using System;

namespace FileSystem.GUI.ViewModels
{
    public class FileItemViewModel : ViewModelBase
    {
        private readonly FileEntry _fileEntry;

        public FileItemViewModel(FileEntry fileEntry)
        {
            _fileEntry = fileEntry;
        }

        public string Name => _fileEntry.Name;
        public long Size => _fileEntry.Size;
        public bool IsDirectory => _fileEntry.IsDirectory;
        public DateTime ModifiedDate => _fileEntry.ModifiedDate;
        public string Icon => IsDirectory ? "📁" : "📄";
        public string SizeText => IsDirectory ? "<DIR>" : FormatFileSize(Size);

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
