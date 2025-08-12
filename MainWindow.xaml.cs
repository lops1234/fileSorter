using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FileTagger.Services;
using Microsoft.Win32;
using FileTagger.Windows;
using System.Drawing;
using System.Windows.Media.Imaging;

namespace FileTagger
{
    public partial class MainWindow : Window
    {
        private bool _isClosing = false;

        public MainWindow()
        {
            InitializeComponent();
            CreateTrayIcon();
            LoadDirectories();
            LoadTags();
            // Don't auto-load files anymore - only when search is pressed
            LoadTagFilter();
        }

        #region Tray Icon Creation

        private void CreateTrayIcon()
        {
            try
            {
                // Create a simple icon programmatically
                using (var bitmap = new Bitmap(16, 16))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        // Clear background
                        graphics.Clear(Color.Transparent);
                        
                        // Draw a simple folder icon
                        using (var blueBrush = new SolidBrush(Color.SteelBlue))
                        using (var darkBlueBrush = new SolidBrush(Color.DarkBlue))
                        {
                            // Draw folder body
                            graphics.FillRectangle(blueBrush, 1, 4, 14, 10);
                            // Draw folder tab
                            graphics.FillRectangle(darkBlueBrush, 1, 4, 6, 2);
                            // Draw border
                            graphics.DrawRectangle(Pens.Black, 1, 4, 13, 9);
                            
                            // Add "T" for tags
                            using (var font = new Font(System.Drawing.FontFamily.GenericSansSerif, 6, System.Drawing.FontStyle.Bold))
                            {
                                graphics.DrawString("T", font, Brushes.White, 6, 7);
                            }
                        }
                    }
                    
                    // Convert to icon and set it
                    var handle = bitmap.GetHicon();
                    var icon = System.Drawing.Icon.FromHandle(handle);
                    TrayIcon.Icon = icon;
                }
            }
            catch
            {
                // If icon creation fails, the tray icon will use a default icon
            }
        }

        #endregion

        #region Window Events

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                TrayIcon.ShowBalloonTip("File Tagger", "Application minimized to system tray", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosing)
            {
                e.Cancel = true;
                Hide();
                TrayIcon.ShowBalloonTip("File Tagger", "Application minimized to system tray", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        #endregion

        #region Tray Context Menu Events

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ManageTags_Click(object sender, RoutedEventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            // Switch to tag management tab
            var tabControl = (TabControl)this.FindName("TabControl");
            if (tabControl != null && tabControl.Items.Count > 1)
            {
                tabControl.SelectedIndex = 1;
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _isClosing = true;
            Application.Current.Shutdown();
        }

        #endregion

        #region Directory Management

        private void LoadDirectories()
        {
            var directories = DatabaseManager.Instance.GetAllActiveDirectories();
            DirectoriesListBox.ItemsSource = directories;
        }

        private void AddDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var activeDirectories = DatabaseManager.Instance.GetAllActiveDirectories();
                if (!activeDirectories.Contains(dialog.SelectedPath))
                {
                    DatabaseManager.Instance.AddWatchedDirectory(dialog.SelectedPath);
                    LoadDirectories();
                    LoadTags();
                    LoadTagFilter();
                    
                    // Refresh context menus to ensure they work with new directory
                    ShellIntegration.RefreshContextMenus();
                }
                else
                {
                    MessageBox.Show("Directory is already being watched.", "Duplicate Directory", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void RemoveDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (DirectoriesListBox.SelectedItem is string selectedPath)
            {
                var result = MessageBox.Show($"Remove directory '{selectedPath}' from watched list?\nTags unique to this directory will be removed from the main tag list.", 
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    DatabaseManager.Instance.RemoveWatchedDirectory(selectedPath);
                    LoadDirectories();
                    LoadTags();
                    LoadTagFilter();
                    
                    // Refresh context menus after directory removal
                    ShellIntegration.RefreshContextMenus();
                }
            }
        }

        private void DirectoriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveDirectoryButton.IsEnabled = DirectoriesListBox.SelectedItem != null;
        }

        private void RefreshContextMenus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShellIntegration.RefreshContextMenus();
                MessageBox.Show("Context menus refreshed successfully!\n\nYou may need to wait a few seconds or restart Windows Explorer for changes to appear.", 
                    "Context Menus Refreshed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to refresh context menus: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Settings saved successfully!", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadDirectories();
        }

        #endregion

        #region Tag Management

        private void LoadTags()
        {
            var tags = DatabaseManager.Instance.GetAllAvailableTags();
            TagsDataGrid.ItemsSource = tags;
        }

        private void CreateTag_Click(object sender, RoutedEventArgs e)
        {
            var tagName = NewTagTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : NewTagTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                MessageBox.Show("Please enter a tag name.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existingTags = DatabaseManager.Instance.GetAllAvailableTags();
            if (existingTags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A tag with this name already exists.", "Duplicate Tag", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show("To create tags, add them to files in watched directories using the context menu.", 
                "Tag Creation", MessageBoxButton.OK, MessageBoxImage.Information);

            NewTagTextBox.Text = "Enter new tag name";
            NewTagTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            TagDescriptionTextBox.Text = "Tag description (optional)";
            TagDescriptionTextBox.Foreground = System.Windows.Media.Brushes.Gray;
        }

        private void DeleteTag_Click(object sender, RoutedEventArgs e)
        {
            if (TagsDataGrid.SelectedItem is TagInfo selectedTag)
            {
                var result = MessageBox.Show($"Delete tag '{selectedTag.Name}'?\nThis will remove the tag from all files in all directories.", 
                    "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Delete from all directory databases
                    var activeDirectories = DatabaseManager.Instance.GetAllActiveDirectories();
                    foreach (var directoryPath in activeDirectories)
                    {
                        try
                        {
                            using var dirDb = DatabaseManager.Instance.GetDirectoryDb(directoryPath);
                            var localTag = dirDb.LocalTags.FirstOrDefault(t => t.Name == selectedTag.Name);
                            if (localTag != null)
                            {
                                dirDb.LocalTags.Remove(localTag);
                                dirDb.SaveChanges();
                            }
                        }
                        catch
                        {
                            // Continue with other directories if one fails
                        }
                    }
                    
                    // Resync all tags
                    DatabaseManager.Instance.SynchronizeAllTags();
                    LoadTags();
                    LoadTagFilter();
                }
            }
        }

        private void RefreshTags_Click(object sender, RoutedEventArgs e)
        {
            LoadTags();
        }

        #endregion

        #region File Browser

        private void LoadFiles(string searchQuery = null)
        {
            try
            {
                var files = DatabaseManager.Instance.GetAllFilesWithTags();
                
                if (!string.IsNullOrEmpty(searchQuery))
                {
                    var parseResult = TagSearchParser.ParseQuery(searchQuery);
                    
                    if (!parseResult.IsValid)
                    {
                        SearchStatusTextBlock.Text = $"Error: {parseResult.ErrorMessage}";
                        SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                        return;
                    }
                    
                    files = files.Where(f => TagSearchParser.EvaluateQuery(searchQuery, f.Tags)).ToList();
                    
                    SearchStatusTextBlock.Text = $"Found {files.Count} files matching: {parseResult.NormalizedQuery}";
                    SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    SearchStatusTextBlock.Text = "";
                }
                
                var fileViewModels = files.Select(f => new FileViewModel
                {
                    FileName = f.FileName,
                    FilePath = f.FullPath,
                    LastModified = f.LastModified,
                    TagsString = f.TagsString
                }).ToList();

                FilesDataGrid.ItemsSource = fileViewModels;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SearchStatusTextBlock.Text = "Error loading files";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void LoadUntaggedFiles()
        {
            try
            {
                SearchStatusTextBlock.Text = "Loading untagged files...";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;

                var untaggedFiles = DatabaseManager.Instance.GetUntaggedFiles();
                var fileViewModels = untaggedFiles.Select(f => new FileViewModel
                {
                    FileName = f.FileName,
                    FilePath = f.FullPath,
                    TagsString = f.TagsString,
                    LastModified = f.LastModified
                }).ToList();

                FilesDataGrid.ItemsSource = fileViewModels;
                SearchStatusTextBlock.Text = $"Found {fileViewModels.Count} untagged files";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                SearchStatusTextBlock.Text = $"Error loading untagged files: {ex.Message}";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                MessageBox.Show($"Error loading untagged files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadTagFilter()
        {
            // Initialize with placeholder text
            TagSearchTextBox.Text = "Enter search query...";
            TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            SearchStatusTextBlock.Text = "";
            FilesDataGrid.ItemsSource = new List<FileViewModel>(); // Start with empty grid
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            var searchQuery = TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : TagSearchTextBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(searchQuery))
            {
                // Show untagged files when no search query is provided
                LoadUntaggedFiles();
            }
            else
            {
                LoadFiles(searchQuery); // Show filtered files
            }
        }

        private void SearchAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchStatusTextBlock.Text = "Loading all files from watched directories...";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;

                var allFiles = DatabaseManager.Instance.GetAllFilesInWatchedDirectories();
                var fileViewModels = allFiles.Select(f => new FileViewModel
                {
                    FileName = f.FileName,
                    FilePath = f.FullPath,
                    TagsString = f.TagsString,
                    LastModified = f.LastModified
                }).ToList();

                FilesDataGrid.ItemsSource = fileViewModels;
                SearchStatusTextBlock.Text = $"Found {fileViewModels.Count} files in watched directories";
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
            TagSearchTextBox.Text = "Enter search query...";
            TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            SearchStatusTextBlock.Text = "";
            FilesDataGrid.ItemsSource = new List<FileViewModel>(); // Clear grid
            TagSuggestionsPopup.IsOpen = false;
        }

        private void RefreshAllTags_Click(object sender, RoutedEventArgs e)
        {
            DatabaseManager.Instance.SynchronizeAllTags();
            LoadTags();
            MessageBox.Show("Tags refreshed successfully!", "Refresh Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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
                var files = FilesDataGrid.ItemsSource as List<FileViewModel>;
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

                // Create README file first
                TempResultsManager.Instance.CreateReadmeFile(searchQuery, files.Count);

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
                if (result.errors.Any())
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
            if (FilesDataGrid.SelectedItem is FileViewModel selectedFile)
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
            if (FilesDataGrid.SelectedItem is FileViewModel selectedFile)
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
            if (FilesDataGrid.SelectedItem is FileViewModel selectedFile)
            {
                var tagWindow = new TagManagementWindow(selectedFile.FilePath);
                tagWindow.Owner = this;
                if (tagWindow.ShowDialog() == true)
                {
                    // Refresh tags when tags are modified
                    DatabaseManager.Instance.SynchronizeAllTags();
                    LoadTags();
                    // Refresh current search results to show updated tags
                    var searchQuery = TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : TagSearchTextBox.Text?.Trim();
                    if (!string.IsNullOrEmpty(searchQuery))
                    {
                        LoadFiles(searchQuery);
                    }
                }
            }
        }

        private void OpenFileFolder_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is FileViewModel selectedFile)
            {
                try
                {
                    var directoryPath = Path.GetDirectoryName(selectedFile.FilePath);
                    if (!string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath))
                    {
                        // Open Explorer and select the file
                        Process.Start("explorer.exe", $"/select,\"{selectedFile.FilePath}\"");
                    }
                    else
                    {
                        MessageBox.Show("Directory not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyFilePath_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is FileViewModel selectedFile)
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

        #endregion

        #region Placeholder Text Handling

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (textBox.Foreground == System.Windows.Media.Brushes.Gray)
                {
                    textBox.Text = "";
                    textBox.Foreground = System.Windows.Media.Brushes.Black;
                }
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Foreground = System.Windows.Media.Brushes.Gray;
                if (textBox.Name == "NewTagTextBox")
                    textBox.Text = "Enter new tag name";
                else if (textBox.Name == "TagDescriptionTextBox")
                    textBox.Text = "Tag description (optional)";
            }
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
            // Extract the current tag being typed from complex query
            var currentTag = ExtractCurrentTag(searchText, TagSearchTextBox.CaretIndex);
            
            if (string.IsNullOrWhiteSpace(currentTag))
            {
                TagSuggestionsPopup.IsOpen = false;
                return;
            }

            var allTags = DatabaseManager.Instance.GetAllAvailableTags();
            var matchingTags = allTags
                .Where(t => t.Name.StartsWith(currentTag, StringComparison.OrdinalIgnoreCase))
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

        private string ExtractCurrentTag(string query, int caretPosition)
        {
            if (string.IsNullOrWhiteSpace(query) || caretPosition < 0)
                return string.Empty;

            // Ensure caret position is within bounds
            caretPosition = Math.Min(caretPosition, query.Length);

            // Find the start and end of the current tag around the caret position
            int start = caretPosition;
            int end = caretPosition;

            // Move start backwards to find beginning of current tag
            while (start > 0 && start - 1 < query.Length && !IsTagDelimiter(query[start - 1]))
            {
                start--;
            }

            // Move end forwards to find end of current tag
            while (end < query.Length && !IsTagDelimiter(query[end]))
            {
                end++;
            }

            if (start >= end || start >= query.Length)
                return string.Empty;

            var currentTag = query.Substring(start, end - start).Trim();
            
            // Remove leading minus sign for excluded tags
            if (currentTag.StartsWith("-"))
                currentTag = currentTag.Substring(1);

            return currentTag;
        }

        private bool IsTagDelimiter(char c)
        {
            return c == ' ' || c == '&' || c == '|' || c == '(' || c == ')';
        }

        private void TagSuggestion_Selected(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TagSuggestionsListBox.SelectedItem is string selectedTag)
            {
                InsertTagIntoQuery(selectedTag);
            }
        }

        private void InsertTagIntoQuery(string selectedTag)
        {
            var currentText = TagSearchTextBox.Text;
            var caretPosition = TagSearchTextBox.CaretIndex;
            
            if (TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray)
            {
                // Replace placeholder text
                TagSearchTextBox.Text = selectedTag;
                TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Black;
            }
            else
            {
                // Ensure caret position is within bounds
                caretPosition = Math.Min(caretPosition, currentText.Length);
                
                // Find the current tag being edited and replace it
                var currentTag = ExtractCurrentTag(currentText, caretPosition);
                if (!string.IsNullOrEmpty(currentTag))
                {
                    // Find start position of current tag
                    int start = caretPosition;
                    while (start > 0 && start - 1 < currentText.Length && !IsTagDelimiter(currentText[start - 1]))
                    {
                        start--;
                    }
                    
                    // Find end position of current tag
                    int end = caretPosition;
                    while (end < currentText.Length && !IsTagDelimiter(currentText[end]))
                    {
                        end++;
                    }
                    
                    // Preserve any minus prefix for exclusion
                    var prefix = "";
                    if (start < currentText.Length && currentText[start] == '-')
                    {
                        prefix = "-";
                        start++; // Skip the minus when calculating replacement
                    }
                    
                    // Replace the current tag
                    var newText = currentText.Substring(0, start) + prefix + selectedTag + currentText.Substring(end);
                    TagSearchTextBox.Text = newText;
                    TagSearchTextBox.CaretIndex = start + prefix.Length + selectedTag.Length;
                }
                else
                {
                    // Just append the tag
                    TagSearchTextBox.Text = currentText + " " + selectedTag;
                    TagSearchTextBox.CaretIndex = TagSearchTextBox.Text.Length;
                }
            }
            
            TagSuggestionsPopup.IsOpen = false;
            TagSearchTextBox.Focus();
        }

        private void TagSuggestionsListBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (TagSuggestionsListBox.SelectedItem is string selectedTag)
                {
                    InsertTagIntoQuery(selectedTag);
                }
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                TagSuggestionsPopup.IsOpen = false;
                TagSearchTextBox.Focus();
            }
        }

        #endregion

        #endregion
    }

    public class FileViewModel
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public string TagsString { get; set; } = string.Empty;
    }

    public class TagFilterItem
    {
        public string Name { get; set; } = string.Empty;
    }
}
