using System.Collections.ObjectModel;

namespace FileSystem.GUI.ViewModels
{
    public class DirectoryNodeViewModel : ViewModelBase
    {
        private string _name = "";
        private string _fullPath = "";
        private bool _isExpanded;
        private bool _isSelected;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string FullPath
        {
            get => _fullPath;
            set => SetProperty(ref _fullPath, value);
        }

        public ObservableCollection<DirectoryNodeViewModel> Children { get; } = new();

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public RelayCommand? OpenCommand { get; set; }
    }
}
