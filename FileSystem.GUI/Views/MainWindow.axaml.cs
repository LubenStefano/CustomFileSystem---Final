using Avalonia.Controls;
using Avalonia.Interactivity;
using FileSystem.GUI.ViewModels;

namespace FileSystem.GUI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        var tree = this.FindControl<TreeView>("DirectoryTreeView");
        if (tree != null)
        {
            tree.SelectionChanged += (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm && tree.SelectedItem is DirectoryNodeViewModel selected)
                {
                    vm.SelectedDirectory = selected;
                }
            };

            tree.DoubleTapped += async (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm && tree.SelectedItem is DirectoryNodeViewModel selected)
                {
                    await vm.NavigateToDirectory(selected.FullPath);
                }
            };
        }

        var list = this.FindControl<ListBox>("FileListView");
        if (list != null)
        {
            list.DoubleTapped += async (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm && vm.SelectedFile != null && vm.SelectedFile.IsDirectory)
                {
                    var name = vm.SelectedFile.Name ?? "";
                    string fullPath;
                    if (Core.Utils.TextUtils.StartsWith(name, "/") || Core.Utils.TextUtils.StartsWith(name, "\\"))
                    {
                        fullPath = Core.Utils.TextUtils.ReplaceChar(name, '\\', '/');
                    }
                    else
                    {
                        if (Core.Utils.TextUtils.IsNullOrEmpty(vm.CurrentPath) || Core.Utils.TextUtils.EqualsOrdinal(vm.CurrentPath, "/"))
                            fullPath = "/" + name;
                        else
                            fullPath = Core.Utils.TextUtils.TrimEnd(vm.CurrentPath, '/') + "/" + name;
                    }

                    await vm.NavigateToDirectory(fullPath);
                }
            };
        }
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}