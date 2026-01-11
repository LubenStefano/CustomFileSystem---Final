using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FileSystem.Core;

namespace FileSystem.GUI.Dialogs
{
    public partial class CreateContainerDialog : Window
    {
        public CreateContainerDialog()
        {
            InitializeComponent();
            
            var pathTextBox = this.FindControl<TextBox>("PathTextBox");
            var browseButton = this.FindControl<Button>("BrowseButton");
            var blockSizeComboBox = this.FindControl<ComboBox>("BlockSizeComboBox");
            var totalBlocksNumeric = this.FindControl<NumericUpDown>("TotalBlocksNumeric");
            var sizeIndicator = this.FindControl<TextBlock>("SizeIndicator");
            var createButton = this.FindControl<Button>("CreateButton");
            var cancelButton = this.FindControl<Button>("CancelButton");

            if (browseButton != null) browseButton.Click += BrowseButton_Click;
            
            if (createButton != null) createButton.Click += CreateButton_Click;
            
            if (cancelButton != null) cancelButton.Click += CancelButton_Click;

            if (blockSizeComboBox != null && totalBlocksNumeric != null && sizeIndicator != null)
            {
                blockSizeComboBox.SelectionChanged += (s, e) => UpdateSizeIndicator();
                totalBlocksNumeric.ValueChanged += (s, e) => UpdateSizeIndicator();
                UpdateSizeIndicator();
            }

            void UpdateSizeIndicator()
            {
                if (blockSizeComboBox?.SelectedItem is ComboBoxItem selectedItem &&
                    totalBlocksNumeric != null && sizeIndicator != null)
                {
                        if (int.TryParse(selectedItem.Content?.ToString(), out int blockSize))
                        {
                            decimal tbvDec = totalBlocksNumeric.Value ?? (decimal)Layout.DefaultTotalBlocks;
                            decimal totalSizeDec = (decimal)blockSize * tbvDec;
                            long totalSize = (long)totalSizeDec;
                            string sizeText = FormatBytes(totalSize);
                            sizeIndicator.Text = $"Container Size: ~{sizeText}";
                        }
                }
            }
        }

        private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Container File",
                SuggestedFileName = "filesystem.bin",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Container Files") { Patterns = new[] { "*.bin" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (file != null)
            {
                var pathTextBox = this.FindControl<TextBox>("PathTextBox");
                if (pathTextBox != null)
                {
                    pathTextBox.Text = file.TryGetLocalPath();
                }
            }
        }

        private void CreateButton_Click(object? sender, RoutedEventArgs e)
        {
            var pathTextBox = this.FindControl<TextBox>("PathTextBox");
            var blockSizeComboBox = this.FindControl<ComboBox>("BlockSizeComboBox");
            var totalBlocksNumeric = this.FindControl<NumericUpDown>("TotalBlocksNumeric");

            if (pathTextBox?.Text is not string path || FileSystem.Core.Utils.TextUtils.IsNullOrWhiteSpace(path))
            {
                return;
            }

                if (blockSizeComboBox?.SelectedItem is not ComboBoxItem selectedItem ||
                !int.TryParse(selectedItem.Content?.ToString(), out int blockSize))
            {
                blockSize = Layout.DefaultBlockSize; // Default
            }

            int totalBlocks = (int)(totalBlocksNumeric?.Value ?? Layout.DefaultTotalBlocks);

            var result = new CreateContainerResult
            {
                Path = path,
                BlockSize = blockSize,
                TotalBlocks = totalBlocks
            };

            Close(result);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private static string FormatBytes(long bytes)
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

        public class CreateContainerResult
        {
            public string Path { get; set; } = "";
            public int BlockSize { get; set; }
            public int TotalBlocks { get; set; }
        }
    }
}
