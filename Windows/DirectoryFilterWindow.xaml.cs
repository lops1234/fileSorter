using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FileTagger.Services;

namespace FileTagger.Windows
{
    public partial class DirectoryFilterWindow : Window
    {
        private readonly string _directoryPath;
        private List<DirectoryFileViewModel> _allFiles = new List<DirectoryFileViewModel>();

        public DirectoryFilterWindow(string directoryPath)
        {
            InitializeComponent();
            _directoryPath = directoryPath;
            DirectoryPathTextBlock.Text = directoryPath;
            LoadTags();
            LoadAllFiles(); // Load but don't display
            InitializeTagSearch();
        }

        private void InitializeTagSearch()
        {
            TagSearchTextBox.Text = "Enter search query...";
            TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            SearchStatusTextBlock.Text = "";
            FilesDataGrid.ItemsSource = new List<DirectoryFileViewModel>(); // Start with empty grid
            FileCountTextBlock.Text = "0 files";
        }

        private void LoadTags()
        {
            // Tags are now loaded dynamically for autocomplete
            // No need to populate a combo box
        }

        private void LoadAllFiles()
        {
            _allFiles.Clear();

            if (!Directory.Exists(_directoryPath)) return;

            try
            {
                var files = Directory.GetFiles(_directoryPath, "*.*", SearchOption.AllDirectories);
                var allTaggedFiles = DatabaseManager.Instance.GetAllFilesWithTags();
                var taggedFilesInDir = allTaggedFiles.Where(f => f.DirectoryPath == _directoryPath).ToDictionary(f => f.FullPath, f => f);

                foreach (var filePath in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        var taggedFile = taggedFilesInDir.GetValueOrDefault(filePath);
                        
                        var fileViewModel = new DirectoryFileViewModel
                        {
                            FileName = fileInfo.Name,
                            FilePath = filePath,
                            Extension = fileInfo.Extension,
                            FileSize = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTime,
                            TagsString = taggedFile?.TagsString ?? string.Empty
                        };

                        _allFiles.Add(fileViewModel);
                    }
                    catch
                    {
                        // Skip files that can't be accessed
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowFilteredFiles(string searchQuery = null)
        {
            try
            {
                var filteredFiles = _allFiles;

                if (!string.IsNullOrEmpty(searchQuery))
                {
                    var parseResult = TagSearchParser.ParseQuery(searchQuery);
                    
                    if (!parseResult.IsValid)
                    {
                        SearchStatusTextBlock.Text = $"Error: {parseResult.ErrorMessage}";
                        SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                        return;
                    }
                    
                    filteredFiles = _allFiles.Where(f => 
                    {
                        if (string.IsNullOrEmpty(f.TagsString))
                            return false;
                            
                        var fileTags = f.TagsString.Split(',').Select(tag => tag.Trim()).Where(tag => !string.IsNullOrEmpty(tag)).ToList();
                        return TagSearchParser.EvaluateQuery(searchQuery, fileTags);
                    }).ToList();
                    
                    SearchStatusTextBlock.Text = $"Found {filteredFiles.Count} files matching: {parseResult.NormalizedQuery}";
                    SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    SearchStatusTextBlock.Text = "";
                }

                FilesDataGrid.ItemsSource = filteredFiles.OrderBy(f => f.FileName).ToList();
                FileCountTextBlock.Text = $"{filteredFiles.Count} files";
            }
            catch (Exception)
            {
                SearchStatusTextBlock.Text = "Error filtering files";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                // Still show all files on error
                FilesDataGrid.ItemsSource = _allFiles.OrderBy(f => f.FileName).ToList();
                FileCountTextBlock.Text = $"{_allFiles.Count} files";
            }
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            var searchQuery = TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : TagSearchTextBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(searchQuery))
            {
                ShowFilteredFiles(); // Show all files
            }
            else
            {
                ShowFilteredFiles(searchQuery); // Show filtered files
            }
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TagSearchTextBox.Text = "Enter search query...";
            TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            SearchStatusTextBlock.Text = "";
            FilesDataGrid.ItemsSource = new List<DirectoryFileViewModel>(); // Clear grid
            FileCountTextBlock.Text = "0 files";
            TagSuggestionsPopup.IsOpen = false;
        }

        private void SearchHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpText = TagSearchParser.GetSearchSyntaxHelp();
            MessageBox.Show(helpText, "Search Syntax Help", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadTags();
            LoadAllFiles();
            // Don't automatically show files - require search
            InitializeTagSearch();
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is DirectoryFileViewModel selectedFile)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = selectedFile.FilePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is DirectoryFileViewModel selectedFile)
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{selectedFile.FilePath}\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ManageFileTags_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is DirectoryFileViewModel selectedFile)
            {
                var tagWindow = new TagManagementWindow(selectedFile.FilePath);
                tagWindow.Owner = this;
                if (tagWindow.ShowDialog() == true)
                {
                    LoadAllFiles(); // Refresh the file list
                    // Don't auto-refresh displayed files - user needs to search again
                }
            }
        }

        private void CopyFilePath_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is DirectoryFileViewModel selectedFile)
            {
                try
                {
                    System.Windows.Clipboard.SetText(selectedFile.FilePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not copy file path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #region Tag Search and Autocomplete

        private void TagSearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray)
            {
                TagSearchTextBox.Text = "";
                TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void TagSearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TagSearchTextBox.Text))
            {
                TagSearchTextBox.Text = "Enter tag name...";
                TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            }
            
            // Close suggestions when losing focus (unless clicking on suggestions)
            if (!TagSuggestionsPopup.IsMouseOver)
            {
                TagSuggestionsPopup.IsOpen = false;
            }
        }

        private void TagSearchTextBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Search_Click(sender, e);
                TagSuggestionsPopup.IsOpen = false;
                return;
            }

            if (e.Key == System.Windows.Input.Key.Escape)
            {
                TagSuggestionsPopup.IsOpen = false;
                return;
            }

            if (e.Key == System.Windows.Input.Key.Down && TagSuggestionsPopup.IsOpen)
            {
                if (TagSuggestionsListBox.Items.Count > 0)
                {
                    TagSuggestionsListBox.SelectedIndex = 0;
                    TagSuggestionsListBox.Focus();
                }
                return;
            }

            var searchText = TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : TagSearchTextBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(searchText))
            {
                TagSuggestionsPopup.IsOpen = false;
                return;
            }

            ShowTagSuggestions(searchText);
        }

        private void ShowTagSuggestions(string searchText)
        {
            var allTags = DatabaseManager.Instance.GetAllAvailableTags();
            var matchingTags = allTags
                .Where(t => t.Name.StartsWith(searchText, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .Take(10)
                .ToList();

            if (matchingTags.Any())
            {
                TagSuggestionsListBox.ItemsSource = matchingTags;
                TagSuggestionsPopup.IsOpen = true;
            }
            else
            {
                TagSuggestionsPopup.IsOpen = false;
            }
        }

        private void TagSuggestion_Selected(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TagSuggestionsListBox.SelectedItem is string selectedTag)
            {
                TagSearchTextBox.Text = selectedTag;
                TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Black;
                TagSuggestionsPopup.IsOpen = false;
                TagSearchTextBox.Focus();
                TagSearchTextBox.CaretIndex = TagSearchTextBox.Text.Length;
            }
        }

        private void TagSuggestionsListBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (TagSuggestionsListBox.SelectedItem is string selectedTag)
                {
                    TagSearchTextBox.Text = selectedTag;
                    TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Black;
                    TagSuggestionsPopup.IsOpen = false;
                    TagSearchTextBox.Focus();
                    TagSearchTextBox.CaretIndex = TagSearchTextBox.Text.Length;
                }
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                TagSuggestionsPopup.IsOpen = false;
                TagSearchTextBox.Focus();
            }
        }

        #endregion
    }

    public class DirectoryFileViewModel
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        public string TagsString { get; set; } = string.Empty;

        public string FileSizeFormatted
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024:F1} KB";
                if (FileSize < 1024 * 1024 * 1024) return $"{FileSize / (1024 * 1024):F1} MB";
                return $"{FileSize / (1024 * 1024 * 1024):F1} GB";
            }
        }
    }

    public class TagFilterItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
