using FileSystem.Core;
using FileSystem.Core.Models;
using FileSystem.GUI.Dialogs;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;
using System.IO;
using FileSystem.Core.Utils;
using FileSystem.Core.Utils.Collections;
using Avalonia.Controls.ApplicationLifetimes;

namespace FileSystem.GUI.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly IFileSystemAPI _fileSystemAPI;
        private string _currentPath = "/";
        private ContainerInfo? _containerInfo;
        private bool _isContainerOpen = false;
        private string _statusMessage = "Ready";
        private FileItemViewModel? _selectedFile;
        private DirectoryNodeViewModel? _selectedDirectory;
        private readonly SimpleStack<string> _backHistory = new SimpleStack<string>();
        private readonly SimpleStack<string> _forwardHistory = new SimpleStack<string>();

        public MainWindowViewModel()
        {
            _fileSystemAPI = new FileSystemImplementation();

            Files = new ObservableCollection<FileItemViewModel>();
            DirectoryTree = new ObservableCollection<DirectoryNodeViewModel>();

            OpenContainerCommand = new RelayCommand(OpenContainer);
            CreateContainerCommand = new RelayCommand(CreateContainer);
            CopyInCommand = new RelayCommand(CopyFileIn, () => IsContainerOpen);
            CopyOutCommand = new RelayCommand(CopyFileOut, () => SelectedFile != null && !SelectedFile.IsDirectory);
            DeleteCommand = new RelayCommand(DeleteFile, () => SelectedFile != null);
            CreateDirectoryCommand = new RelayCommand(CreateDirectory, () => IsContainerOpen);
            RefreshCommand = new RelayCommand(RefreshView, () => IsContainerOpen);
            GoUpCommand = new RelayCommand(GoUp, () => IsContainerOpen);
            GoRootCommand = new RelayCommand(GoRoot, () => IsContainerOpen);
            GoBackCommand = new RelayCommand(GoBack, () => _backHistory.Count > 0);
            GoForwardCommand = new RelayCommand(GoForward, () => _forwardHistory.Count > 0);
            ExitCommand = new RelayCommand(() => Environment.Exit(0));
        }

        public ObservableCollection<FileItemViewModel> Files { get; }
        public ObservableCollection<DirectoryNodeViewModel> DirectoryTree { get; }

        public string CurrentPath
        {
            get => _currentPath;
            set => SetProperty(ref _currentPath, value);
        }

        public bool IsContainerOpen
        {
            get => _isContainerOpen;
            set
            {
                if (SetProperty(ref _isContainerOpen, value))
                {
                    CopyInCommand.RaiseCanExecuteChanged();
                    CreateDirectoryCommand.RaiseCanExecuteChanged();
                    RefreshCommand.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(EmptyStateMessage));
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(FileListHeader));
                }
            }
        }

        public ContainerInfo? ContainerInfo
        {
            get => _containerInfo;
            set => SetProperty(ref _containerInfo, value);
        }

        public FileItemViewModel? SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (SetProperty(ref _selectedFile, value))
                {
                    CopyOutCommand.RaiseCanExecuteChanged();
                    DeleteCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public DirectoryNodeViewModel? SelectedDirectory
        {
            get => _selectedDirectory;
            set { }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string FileListHeader => IsContainerOpen ? $"Files in {CurrentPath}" : "No Container Open";

        public string EmptyStateMessage => IsContainerOpen ? "This directory is empty" : "Open or create a container to get started";

        public bool IsEmpty => Files.Count == 0;

        private string _containerPathText = "";
        private string _usedBlocksText = "";
        private string _freeBlocksText = "";
        private string _blockSizeText = "";

        public string ContainerPathText
        {
            get => _containerPathText;
            set => SetProperty(ref _containerPathText, value);
        }

        public string UsedBlocksText
        {
            get => _usedBlocksText;
            set => SetProperty(ref _usedBlocksText, value);
        }

        public string FreeBlocksText
        {
            get => _freeBlocksText;
            set => SetProperty(ref _freeBlocksText, value);
        }

        public string BlockSizeText
        {
            get => _blockSizeText;
            set => SetProperty(ref _blockSizeText, value);
        }

        public RelayCommand OpenContainerCommand { get; }
        public RelayCommand CreateContainerCommand { get; }
        public RelayCommand CopyInCommand { get; }
        public RelayCommand CopyOutCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand CreateDirectoryCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand GoBackCommand { get; private set; }
        public RelayCommand GoForwardCommand { get; private set; }
        public RelayCommand ExitCommand { get; }

        private async Task OpenContainer()
        {
            try
            {
                var window = GetMainWindow();
                if (window == null) return;

                var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Open Container File",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        // Explicit manual filter for container files (only .bin)
                        new FilePickerFileType("Container Files") { Patterns = ["*.bin"] },
                        new FilePickerFileType("All Files") { Patterns = ["*"] }
                    ]
                });

                if (files.Count > 0)
                {
                    var file = files[0];
                    var path = file.TryGetLocalPath();
                    if (!TextUtils.IsNullOrEmpty(path))
                    {
                        try
                        {
                            _fileSystemAPI.OpenContainer(path!);
                            IsContainerOpen = true;
                            StatusMessage = $"Opened container: {Path.GetFileName(path)}";
                            await RefreshView();
                        }
                        catch (Exception ex)
                        {
                            IsContainerOpen = false;
                            ContainerInfo = null;
                            StatusMessage = $"Error opening container: {ex.Message}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                IsContainerOpen = false;
                ContainerInfo = null;
                StatusMessage = $"Error opening container: {ex.Message}";
            }
        }

        private async Task CreateContainer()
        {
            try
            {
                var window = GetMainWindow();
                if (window == null) return;

                var dialog = new CreateContainerDialog();
                var result = await dialog.ShowDialog<CreateContainerDialog.CreateContainerResult?>(window);

                if (result != null)
                {
                    _fileSystemAPI.CreateContainer(result.Path, result.BlockSize, result.TotalBlocks);
                    IsContainerOpen = true;
                    StatusMessage = $"Created container: {Path.GetFileName(result.Path)}";
                    await RefreshView();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating container: {ex.Message}";
            }
        }

        private async Task CopyFileIn()
        {
            try
            {
                var window = GetMainWindow();
                if (window == null) return;

                var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select File to Copy In",
                    AllowMultiple = false
                });

                if (files.Count > 0)
                {
                    var file = files[0];
                    var sourcePath = file.TryGetLocalPath();
                    var fileName = Path.GetFileName(sourcePath);

                    if (!TextUtils.IsNullOrEmpty(sourcePath) && !TextUtils.IsNullOrEmpty(fileName))
                    {
                        if (SelectedDirectory != null)
                        {
                            ChangeDirectoryByFullPath(SelectedDirectory.FullPath);
                            await RefreshView();
                        }

                        _fileSystemAPI.CopyFileIn(sourcePath!, fileName!);
                        StatusMessage = $"Copied file: {fileName}";
                        await RefreshView();
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error copying file in: {ex.Message}";
            }
        }

        private async Task CopyFileOut()
        {
            if (SelectedFile == null) return;

            try
            {
                var window = GetMainWindow();
                if (window == null) return;

                var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save File As",
                    SuggestedFileName = SelectedFile.Name
                });

                if (file != null)
                {
                    var targetPath = file.TryGetLocalPath();
                    if (!TextUtils.IsNullOrEmpty(targetPath))
                    {
                        _fileSystemAPI.CopyFileOut(SelectedFile.Name, targetPath!);
                        StatusMessage = $"Copied file out: {SelectedFile.Name}";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error copying file out: {ex.Message}";
            }
        }

        private async Task DeleteFile()
        {
            if (SelectedFile == null) return;

            try
            {
                var window = GetMainWindow();
                if (window == null) return;

                var result = await ShowMessageBox(window,
                    "Confirm Delete",
                    $"Are you sure you want to delete '{SelectedFile.Name}'?",
                    MessageBoxButtons.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    if (SelectedFile.IsDirectory)
                    {
                        _fileSystemAPI.RemoveDirectory(SelectedFile.Name);
                        StatusMessage = $"Deleted directory: {SelectedFile.Name}";
                    }
                    else
                    {
                        _fileSystemAPI.DeleteFile(SelectedFile.Name);
                        StatusMessage = $"Deleted file: {SelectedFile.Name}";
                    }

                    SelectedFile = null;
                    await RefreshView();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error deleting: {ex.Message}";
            }
        }

        private async Task CreateDirectory()
        {
            try
            {
                var window = GetMainWindow();
                if (window == null) return;

                var dialog = new CreateDirectoryDialog();
                var result = await dialog.ShowDialog<string?>(window);

                if (!TextUtils.IsNullOrWhiteSpace(result))
                {
                    string dirName = TextUtils.Trim(result ?? "");
                    if (dirName == "." || dirName == ".." || TextUtils.IndexOfAny(dirName, new[] { '/', '\\' }) >= 0)
                    {
                        StatusMessage = "Invalid directory name (must not contain '/' or '\\', and cannot be '.' or '..').";
                        return;
                    }
                    try
                    {
                        if (SelectedDirectory != null && SelectedDirectory.FullPath != CurrentPath)
                        {
                            ChangeDirectoryByFullPath(SelectedDirectory.FullPath);
                        }

                        _fileSystemAPI.CreateDirectory(dirName);
                        StatusMessage = $"Created directory: {dirName}";
                        await RefreshView();
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Error creating directory: {ex.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating directory: {ex.Message}";
            }
        }

        private Task RefreshView()
        {
            if (!IsContainerOpen)
            {
                Files.Clear();
                DirectoryTree.Clear();
                ContainerInfo = null;
                CurrentPath = "/";
                StatusMessage = "No container open.";
                return Task.CompletedTask;
            }

            try
            {
                ContainerInfo = _fileSystemAPI.GetContainerInfo();
                CurrentPath = _fileSystemAPI.GetCurrentPath();

                if (ContainerInfo != null)
                {
                    ContainerPathText = $"Container: {ContainerInfo.Path}";
                    UsedBlocksText = $"Used: {ContainerInfo.UsedBlocks} blocks";
                    FreeBlocksText = $"Free: {ContainerInfo.FreeBlocks} blocks";
                    BlockSizeText = $"Block Size: {ContainerInfo.BlockSize} bytes";
                }
                else
                {
                    ContainerPathText = "";
                    UsedBlocksText = "";
                    FreeBlocksText = "";
                    BlockSizeText = "";
                }

                var files = _fileSystemAPI.ListCurrentDirectory();
                Files.Clear();

                var count = files.Count;
                var arr = new FileEntry[count];
                for (int i = 0; i < count; i++) arr[i] = files[i];

                for (int i = 1; i < count; i++)
                {
                    var key = arr[i];
                    int j = i - 1;
                    while (j >= 0)
                    {
                        var a = arr[j];
                        int cmp;
                        if (a.IsDirectory && !key.IsDirectory) cmp = -1;
                        else if (!a.IsDirectory && key.IsDirectory) cmp = 1;
                        else cmp = TextUtils.CompareIgnoreCase(a.Name, key.Name);
                        if (cmp <= 0) break;
                        arr[j + 1] = arr[j];
                        j--;
                    }
                    arr[j + 1] = key;
                }
                for (int i = 0; i < count; i++) Files.Add(new FileItemViewModel(arr[i]));

                BuildDirectoryTreeFromCurrentFiles();

                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(FileListHeader));

                int dirCount = 0;
                foreach (var f in Files) if (f.IsDirectory) dirCount++;
                int treeCount = DirectoryTree.Count;
                StatusMessage = $"Refreshed - {Files.Count} items ({dirCount} dirs), TreeRoots={treeCount}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error refreshing: {ex.Message}";
            }
            return Task.CompletedTask;
        }

        private void BuildDirectoryTreeFromCurrentFiles()
        {
            DirectoryTree.Clear();

            try
            {
                var directories = _fileSystemAPI.ListAllDirectories();
                if (directories == null || directories.Count == 0)
                {
                    var defaultRootNode = new DirectoryNodeViewModel
                    {
                        Name = "/",
                        FullPath = "/",
                        IsExpanded = true,
                        IsSelected = true,
                        OpenCommand = new RelayCommand(async () => await NavigateToDirectory("/"))
                    };
                    DirectoryTree.Add(defaultRootNode);
                    _selectedDirectory = defaultRootNode;
                    OnPropertyChanged(nameof(SelectedDirectory));
                    OnPropertyChanged(nameof(DirectoryTree));
                    return;
                }

                var lookup = new SimpleHashTableInt<DirectoryEntry>(directories.Count + 4);
                for (int i = 0; i < directories.Count; i++)
                {
                    var d = directories[i];
                    if (!lookup.ContainsKey(d.InodeIndex)) lookup.Put(d.InodeIndex, d);
                }

                string BuildFullPath(DirectoryEntry? dir)
                {
                    if (dir == null) return "/";
                    if (dir.InodeIndex == 0 || dir.ParentInode < 0) return "/";
                    var parts = new SimpleList<string>();
                    var current = dir;
                    while (current != null && current.ParentInode >= 0 && lookup.TryGet(current.ParentInode, out var parent))
                    {
                        parts.Add(current.Name);
                        current = parent;
                    }

                    var rev = new SimpleList<string>(parts.Count);
                    for (int i = parts.Count - 1; i >= 0; i--) rev.Add(parts[i]);

                    string joined = "";

                    for (int i = 0; i < rev.Count; i++)
                    {
                        if (i > 0) joined += "/";
                        joined += rev[i];
                    }
                    return "/" + joined;
                }

                var visited = new SimpleSetInt(directories.Count + 4);
                DirectoryEntry? rootEntry = null;

                if (lookup.ContainsKey(0)) { lookup.TryGet(0, out var re); rootEntry = re; }
                else if (directories.Count > 0) rootEntry = directories[0];
                else rootEntry = null;

                if (rootEntry == null)
                {
                    StatusMessage = "No root directory found.";
                    return;
                }

                var rootNode = new DirectoryNodeViewModel
                {
                    Name = rootEntry.InodeIndex == 0 ? "/" : rootEntry.Name,
                    FullPath = BuildFullPath(rootEntry),
                    OpenCommand = new RelayCommand(async () => await NavigateToDirectory(BuildFullPath(rootEntry)))
                };

                var queue = new SimpleQueue<(DirectoryEntry entry, DirectoryNodeViewModel node)>();
                queue.Enqueue((rootEntry!, rootNode));
                visited.Add(rootEntry.InodeIndex);

                while (queue.Count > 0)
                {
                    var tuple = queue.Dequeue();
                    var entry = tuple.entry;
                    var node = tuple.node;

                    for (int ci = 0; ci < entry.ChildInodes.Count; ci++)
                    {
                        int childInode = entry.ChildInodes[ci];
                        if (!lookup.TryGet(childInode, out var childEntry)) continue;
                        if (visited.Contains(childEntry.InodeIndex))
                        {
                            continue;
                        }

                        var childFullPath = BuildFullPath(childEntry);
                        var childNode = new DirectoryNodeViewModel
                        {
                            Name = childEntry.Name,
                            FullPath = childFullPath,
                            OpenCommand = new RelayCommand(async () => await NavigateToDirectory(childFullPath))
                        };

                        node.Children.Add(childNode);
                        visited.Add(childEntry.InodeIndex);
                        queue.Enqueue((childEntry, childNode));
                    }

                    if (!TextUtils.IsNullOrEmpty(node.FullPath) && (node.FullPath == "/" || TextUtils.StartsWith(CurrentPath, node.FullPath + "/") || TextUtils.EqualsOrdinal(CurrentPath, node.FullPath)))
                    {
                        node.IsExpanded = true;
                    }

                    if (TextUtils.EqualsOrdinal(node.FullPath, CurrentPath))
                    {
                        node.IsSelected = true;
                        _selectedDirectory = node;
                        OnPropertyChanged(nameof(SelectedDirectory));
                    }
                }

                DirectoryTree.Add(rootNode);
                OnPropertyChanged(nameof(DirectoryTree));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error building directory tree: {ex.Message}";
            }
        }

        public RelayCommand GoUpCommand { get; }
        public RelayCommand GoRootCommand { get; }

        private async Task GoUp()
        {
            try
            {
                _fileSystemAPI.ChangeDirectory("..");
                await RefreshView();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error going up: {ex.Message}";
            }
        }

        private async Task GoRoot()
        {
            try
            {
                _fileSystemAPI.ChangeDirectory("/");
                await RefreshView();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error going root: {ex.Message}";
            }
        }

        public async Task NavigateToDirectory(string directoryName)
        {
            try
            {
                if (!TextUtils.IsNullOrEmpty(CurrentPath))
                {
                    _backHistory.Push(CurrentPath);
                    _forwardHistory.Clear();
                    GoBackCommand.RaiseCanExecuteChanged();
                    GoForwardCommand.RaiseCanExecuteChanged();
                }

                ChangeDirectoryByFullPath(directoryName);
                await RefreshView();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error navigating directory: {ex.Message}";
            }
        }

        private async Task GoBack()
        {
            if (_backHistory.Count == 0) return;
            var prev = _backHistory.Pop();
            _forwardHistory.Push(CurrentPath);
            GoBackCommand.RaiseCanExecuteChanged();
            GoForwardCommand.RaiseCanExecuteChanged();

            ChangeDirectoryByFullPath(prev);
            await RefreshView();
        }

        private async Task GoForward()
        {
            if (_forwardHistory.Count == 0) return;
            var next = _forwardHistory.Pop();
            _backHistory.Push(CurrentPath);
            GoBackCommand.RaiseCanExecuteChanged();
            GoForwardCommand.RaiseCanExecuteChanged();

            ChangeDirectoryByFullPath(next);
            await RefreshView();
        }

        private void ChangeDirectoryByFullPath(string directoryName)
        {
            if (TextUtils.IsNullOrWhiteSpace(directoryName)) return;

            if (directoryName == "/")
            {
                _fileSystemAPI.ChangeDirectory("/");
            }
            else if (TextUtils.StartsWith(directoryName, "/"))
            {
                _fileSystemAPI.ChangeDirectory("/");
                var parts = TextUtils.Split(directoryName, '/', true);
                for (int i = 0; i < parts.Count; i++)
                {
                    _fileSystemAPI.ChangeDirectory(parts[i]);
                }
            }
            else
            {
                _fileSystemAPI.ChangeDirectory(directoryName);
            }
        }

        private Window? GetMainWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }
            return null;
        }

        private Task<MessageBoxResult> ShowMessageBox(Window parent, string title, string message, MessageBoxButtons buttons)
        {
            return Task.FromResult(MessageBoxResult.Yes);
        }
    }

    public enum MessageBoxButtons
    {
        OK,
        YesNo
    }

    public enum MessageBoxResult
    {
        OK,
        Yes,
        No
    }
}
