using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FileSystem.GUI.Dialogs
{
    public partial class CreateDirectoryDialog : Window
    {
        public CreateDirectoryDialog()
        {
            InitializeComponent();
            
            var directoryNameTextBox = this.FindControl<TextBox>("DirectoryNameTextBox");
            var createButton = this.FindControl<Button>("CreateButton");
            var cancelButton = this.FindControl<Button>("CancelButton");

            if (createButton != null) createButton.Click += CreateButton_Click;
            
            if (cancelButton != null) cancelButton.Click += CancelButton_Click;

            // Focus the text box
            directoryNameTextBox?.Focus();
        }

        private void CreateButton_Click(object? sender, RoutedEventArgs e)
        {
            var directoryNameTextBox = this.FindControl<TextBox>("DirectoryNameTextBox");
            
            if (!Core.Utils.TextUtils.IsNullOrWhiteSpace(directoryNameTextBox?.Text))
            {
                Close(Core.Utils.TextUtils.Trim(directoryNameTextBox?.Text ?? ""));
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
