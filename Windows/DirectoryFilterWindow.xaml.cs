using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FileTagger.Services;
using Microsoft.EntityFrameworkCore;

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

        private void ShowUntaggedFiles()
        {
            try
            {
                SearchStatusTextBlock.Text = "Loading untagged files...";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;

                var untaggedFiles = _allFiles.Where(f => string.IsNullOrEmpty(f.TagsString)).ToList();

                FilesDataGrid.ItemsSource = untaggedFiles.OrderBy(f => f.FileName).ToList();
                SearchStatusTextBlock.Text = $"Found {untaggedFiles.Count} untagged files";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                FileCountTextBlock.Text = $"{untaggedFiles.Count} files";
            }
            catch (Exception ex)
            {
                SearchStatusTextBlock.Text = $"Error loading untagged files: {ex.Message}";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                FilesDataGrid.ItemsSource = _allFiles.OrderBy(f => f.FileName).ToList();
                FileCountTextBlock.Text = $"{_allFiles.Count} files";
            }
        }

        private List<FileTagger.Services.FileWithTags> GetAllFilesInDirectory()
        {
            var fileMap = new Dictionary<string, FileTagger.Services.FileWithTags>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                // Get all files from the file system in this directory
                var allFiles = Directory.GetFiles(_directoryPath, "*", SearchOption.AllDirectories);
                
                using var dirDb = Services.DatabaseManager.Instance.GetDirectoryDb(_directoryPath);
                var taggedFilesMap = dirDb.LocalFileRecords
                    .Include(f => f.LocalFileTags)
                    .ThenInclude(lft => lft.LocalTag)
                    .ToDictionary(f => Path.Combine(_directoryPath, f.RelativePath), 
                                f => f, 
                                StringComparer.OrdinalIgnoreCase);

                foreach (var filePath in allFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        var tags = new List<string>();
                        
                        // Get tags if file is in database
                        if (taggedFilesMap.TryGetValue(filePath, out var taggedFile))
                        {
                            tags = taggedFile.LocalFileTags.Select(lft => lft.LocalTag.Name).ToList();
                        }

                        fileMap[filePath] = new FileTagger.Services.FileWithTags
                        {
                            FileName = fileInfo.Name,
                            FullPath = filePath,
                            DirectoryPath = _directoryPath,
                            LastModified = fileInfo.LastWriteTime,
                            FileSize = fileInfo.Length,
                            Tags = tags
                        };
                    }
                    catch
                    {
                        // Skip files that can't be accessed
                        continue;
                    }
                }
            }
            catch
            {
                // Return empty list on directory access errors
            }

            return fileMap.Values.OrderBy(f => f.FileName).ToList();
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            // Cancel any ongoing copy operations
            Services.TempResultsManager.Instance.CancelCurrentCopyOperation();
            
            var searchQuery = TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : TagSearchTextBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(searchQuery))
            {
                ShowUntaggedFiles(); // Show untagged files when no search query
            }
            else
            {
                ShowFilteredFiles(searchQuery); // Show filtered files
            }
        }

        private void SearchAll_Click(object sender, RoutedEventArgs e)
        {
            // Cancel any ongoing copy operations
            Services.TempResultsManager.Instance.CancelCurrentCopyOperation();
            
            try
            {
                SearchStatusTextBlock.Text = "Loading all files from directory...";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;

                var allFiles = GetAllFilesInDirectory();
                var fileViewModels = allFiles.Select(f => new DirectoryFileViewModel
                {
                    FileName = f.FileName,
                    FilePath = f.FullPath,
                    TagsString = f.TagsString,
                    LastModified = f.LastModified
                }).ToList();

                FilesDataGrid.ItemsSource = fileViewModels;
                SearchStatusTextBlock.Text = $"Found {fileViewModels.Count} files in directory";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                SearchStatusTextBlock.Text = $"Error loading files: {ex.Message}";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                MessageBox.Show($"Error loading all files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            // Cancel any ongoing copy operations
            Services.TempResultsManager.Instance.CancelCurrentCopyOperation();
            
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

        private async void OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var files = FilesDataGrid.ItemsSource as List<DirectoryFileViewModel>;
                if (files == null || !files.Any())
                {
                    MessageBox.Show("No files to open. Please run a search first.", "No Results", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Get current search query for README
                var searchQuery = TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "All files" : TagSearchTextBox.Text?.Trim();
                if (string.IsNullOrEmpty(searchQuery))
                    searchQuery = "All files";

                // Prepare temp directory
                var tempDir = TempResultsManager.Instance.PrepareTempDirectory();

                // Create README file first with directory context
                var readmeContent = $"File Tagger - Directory Search Results\n" +
                                   $"=====================================\n\n" +
                                   $"Source Directory: {_directoryPath}\n" +
                                   $"Search Query: {searchQuery}\n" +
                                   $"Files Found: {files.Count}\n" +
                                   $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                                   $"These are copies of files that matched your search criteria.\n" +
                                   $"Original files remain in their original locations.\n" +
                                   $"This temporary directory will be cleaned up when File Tagger closes.\n\n" +
                                   $"Files will appear here as they are copied...";

                var readmePath = Path.Combine(tempDir, "README.txt");
                File.WriteAllText(readmePath, readmeContent);

                // Open Explorer immediately with README
                Process.Start("explorer.exe", $"\"{tempDir}\"");

                // Update status to show copying is starting
                SearchStatusTextBlock.Text = "Copying files to temporary directory... (0/0)";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;

                // Start copying files asynchronously
                var filePaths = files.Select(f => f.FilePath).ToList();
                var result = await TempResultsManager.Instance.CopySearchResultsAsync(filePaths, (copied, total) =>
                {
                    // Update progress on UI thread
                    Dispatcher.Invoke(() =>
                    {
                        SearchStatusTextBlock.Text = $"Copying files to temporary directory... ({copied}/{total})";
                    });
                });

                // Show final status
                if (result.wasCancelled)
                {
                    SearchStatusTextBlock.Text = $"File copying cancelled ({result.copiedCount} files copied before cancellation)";
                    SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
                }
                else if (result.errors.Any())
                {
                    SearchStatusTextBlock.Text = $"Copied {result.copiedCount} files with {result.errors.Count} errors";
                    SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    
                    // Show error details if not too many
                    if (result.errors.Count <= 5)
                    {
                        var errorMessage = $"Copied {result.copiedCount} files successfully.\n\nErrors:\n{string.Join("\n", result.errors)}";
                        MessageBox.Show(errorMessage, "Copy Results", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        var errorMessage = $"Copied {result.copiedCount} files successfully.\n\nErrors ({result.errors.Count} total):\n{string.Join("\n", result.errors.Take(3))}\n... and {result.errors.Count - 3} more errors.";
                        MessageBox.Show(errorMessage, "Copy Results", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    SearchStatusTextBlock.Text = $"Successfully copied {result.copiedCount} files to Explorer";
                    SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening search results in Explorer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SearchStatusTextBlock.Text = "Error opening search results";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is DirectoryFileViewModel selectedFile)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(selectedFile.FilePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenFileFolder_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is DirectoryFileViewModel selectedFile)
            {
                try
                {
                    // Open Explorer and select the file
                    Process.Start("explorer.exe", $"/select,\"{selectedFile.FilePath}\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadTags();
            LoadAllFiles();
            // Don't automatically show files - require search
            InitializeTagSearch();
        }

        private void ManageFileTags_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is DirectoryFileViewModel selectedFile)
            {
                var tagWindow = new TagManagementWindow(selectedFile.FilePath);
                tagWindow.Owner = this;
                if (tagWindow.ShowDialog() == true)
                {
                    // Refresh the file list to show updated tags
                    LoadAllFiles();
                    
                    // Refresh current search results if there's an active filter
                    var searchQuery = TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : TagSearchTextBox.Text?.Trim();
                    if (!string.IsNullOrEmpty(searchQuery))
                    {
                        ShowFilteredFiles(searchQuery);
                    }
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
                    MessageBox.Show("File path copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not copy path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

