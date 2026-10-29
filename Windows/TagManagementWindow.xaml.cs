using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FileTagger.Services;

namespace FileTagger.Windows
{
    public class TagDisplayItem
    {
        public string TagName { get; set; }
        public int FileCount { get; set; }
        public int TotalFiles { get; set; }
        
        public override string ToString()
        {
            if (FileCount == TotalFiles && TotalFiles > 1)
                return $"{TagName} (all {FileCount})";
            else if (TotalFiles > 1)
                return $"{TagName} ({FileCount}/{TotalFiles})";
            else
                return TagName;
        }
    }

    public partial class TagManagementWindow : Window
    {
        private readonly List<string> _filePaths;
        private readonly List<string> _originalFilePaths;
        private List<string> _autocompleteTagNames = new List<string>();
        private readonly List<string> _currentTagNames = new List<string>();
        private ManageTagsSnapshot _preloadedSnapshot;

        public TagManagementWindow(List<string> filePaths) : this(filePaths, null)
        {
        }

        public TagManagementWindow(List<string> filePaths, ManageTagsSnapshot preloadedSnapshot)
        {
            InitializeComponent();
            _preloadedSnapshot = preloadedSnapshot;

            if (filePaths == null || filePaths.Count == 0)
                throw new ArgumentException("No file paths were provided.", nameof(filePaths));

            _originalFilePaths = new List<string>(filePaths);
            _filePaths = new List<string>();

            try
            {
            // Process each file path (handle temp files)
            foreach (var filePath in filePaths)
            {
                if (TempResultsManager.Instance.IsInTempDirectory(filePath))
                {
                    var mappedPath = TempResultsManager.Instance.GetOriginalFilePath(filePath);
                    if (!string.IsNullOrEmpty(mappedPath))
                    {
                        _filePaths.Add(mappedPath);
                    }
                    else
                    {
                        _filePaths.Add(filePath);
                    }
                }
                else
                {
                    _filePaths.Add(filePath);
                }
            }
            
            UpdateFileHeader();
            LoadCurrentTags();

            Loaded += (_, __) =>
            {
                Dispatcher.BeginInvoke(new Action(LoadAvailableTags),
                    System.Windows.Threading.DispatcherPriority.Background);
            };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load tag management window: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Adds files to an already open window (e.g. late Explorer processes from the same selection).
        /// </summary>
        public void AddFiles(IEnumerable<string> filePaths)
        {
            var added = false;
            foreach (var filePath in filePaths)
            {
                var resolvedPath = filePath;
                if (TempResultsManager.Instance.IsInTempDirectory(filePath))
                {
                    var mappedPath = TempResultsManager.Instance.GetOriginalFilePath(filePath);
                    if (!string.IsNullOrEmpty(mappedPath))
                        resolvedPath = mappedPath;
                }

                if (_filePaths.Contains(resolvedPath, StringComparer.OrdinalIgnoreCase))
                    continue;

                _originalFilePaths.Add(filePath);
                _filePaths.Add(resolvedPath);
                added = true;
            }

            if (!added)
                return;

            _preloadedSnapshot = null;
            UpdateFileHeader();
            LoadCurrentTags();
            LoadAvailableTags();
        }

        private void UpdateFileHeader()
        {
            TempFileInfoPanel.Visibility = System.Windows.Visibility.Collapsed;

            if (_filePaths.Count == 1)
            {
                FileHeaderTextBlock.Text = "File:";
                FilePathTextBlock.Text = _filePaths[0];

                var originalPath = _originalFilePaths[0];
                if (TempResultsManager.Instance.IsInTempDirectory(originalPath))
                {
                    var mappedPath = TempResultsManager.Instance.GetOriginalFilePath(originalPath);
                    if (!string.IsNullOrEmpty(mappedPath))
                    {
                        TempFileInfoPanel.Visibility = System.Windows.Visibility.Visible;
                        TempFilePathTextBlock.Text = originalPath;
                        OriginalFilePathTextBlock.Text = mappedPath;
                        FilePathTextBlock.Text = mappedPath;
                    }
                }
            }
            else
            {
                FileHeaderTextBlock.Text = $"Managing {_filePaths.Count} files:";
                FilePathTextBlock.Text = string.Join("\n", _filePaths.Take(5));
                if (_filePaths.Count > 5)
                {
                    FilePathTextBlock.Text += $"\n... and {_filePaths.Count - 5} more";
                }
            }
        }

        private void EnsureTagNamesLoaded()
        {
            if (_autocompleteTagNames.Count == 0)
                _autocompleteTagNames = DatabaseManager.Instance.GetManageTagsSnapshot(_filePaths).AllTagNames;
        }

        // Convenience constructor for backward compatibility (single file)
        public TagManagementWindow(string filePath) : this(new List<string> { filePath })
        {
        }

        private void LoadFileRecord()
        {
            // File record will be created automatically when tags are added
            // This is now handled by the DatabaseManager
        }

        private void LoadCurrentTags()
        {
            _currentTagNames.Clear();

            if (_filePaths.Count == 0)
            {
                CurrentTagsListBox.ItemsSource = new List<TagDisplayItem>();
                return;
            }

            var allFileTags = new List<List<string>>();
            if (_preloadedSnapshot != null)
            {
                _autocompleteTagNames = _preloadedSnapshot.AllTagNames;

                foreach (var filePath in _filePaths)
                {
                    allFileTags.Add(
                        _preloadedSnapshot.TagsByFile.TryGetValue(filePath, out var tags)
                            ? tags
                            : new List<string>());
                }

                _preloadedSnapshot = null;
            }
            else
            {
                var snapshot = DatabaseManager.Instance.GetManageTagsSnapshot(_filePaths);
                _autocompleteTagNames = snapshot.AllTagNames;

                foreach (var filePath in _filePaths)
                {
                    allFileTags.Add(
                        snapshot.TagsByFile.TryGetValue(filePath, out var tags)
                            ? tags
                            : new List<string>());
                }
            }

            if (_filePaths.Count == 1)
            {
                // Single file - show all tags
                var displayItems = allFileTags[0]
                    .OrderBy(t => t)
                    .Select(t => new TagDisplayItem 
                    { 
                        TagName = t, 
                        FileCount = 1, 
                        TotalFiles = 1 
                    })
                    .ToList();
                _currentTagNames.AddRange(displayItems.Select(t => t.TagName));
                CurrentTagsListBox.ItemsSource = displayItems;
            }
            else
            {
                // Multiple files - show all tags from any file (union)
                // Count how many files have each tag
                var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var fileTags in allFileTags)
                {
                    foreach (var tag in fileTags)
                    {
                        if (!tagCounts.ContainsKey(tag))
                            tagCounts[tag] = 0;
                        tagCounts[tag]++;
                    }
                }

                // Create display items with count indicators
                var displayItems = tagCounts
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => new TagDisplayItem
                    {
                        TagName = kvp.Key,
                        FileCount = kvp.Value,
                        TotalFiles = _filePaths.Count
                    })
                    .ToList();

                _currentTagNames.AddRange(displayItems.Select(t => t.TagName));
                CurrentTagsListBox.ItemsSource = displayItems;
            }
        }

        private void LoadAvailableTags()
        {
            EnsureTagNamesLoaded();

            var availableTags = _autocompleteTagNames
                .Where(name => !_currentTagNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                .OrderBy(t => t)
                .ToList();

            AvailableTagsListBox.ItemsSource = availableTags;
        }

        private void RefreshTagNameCache()
        {
            _autocompleteTagNames = DatabaseManager.Instance.GetAllTagNamesForPrompt();
        }

        private void CurrentTagsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var count = CurrentTagsListBox.SelectedItems.Count;
            RemoveSelectedTagButton.IsEnabled = count > 0;
            RemoveSelectedTagButton.Content = count > 1 ? $"Remove Selected ({count})" : "Remove Selected";
        }

        private void CurrentTagsListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Delete && CurrentTagsListBox.SelectedItems.Count > 0)
            {
                RemoveTag_Click(sender, e);
                e.Handled = true;
            }
        }

        private void RemoveSelectedTag_Click(object sender, RoutedEventArgs e)
        {
            RemoveTag_Click(sender, e);
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            var selectedTags = CurrentTagsListBox.SelectedItems.OfType<TagDisplayItem>().ToList();
            if (selectedTags.Count == 0)
                return;

            try
            {
                // Remove each tag from all selected files (only removes from files that have it)
                foreach (var selectedTag in selectedTags)
                {
                    foreach (var filePath in _filePaths)
                    {
                        DatabaseManager.Instance.RemoveTagFromFile(filePath, selectedTag.TagName);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing tags: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshTagNameCache();
            LoadCurrentTags();
            LoadAvailableTags();
        }

        private void CreateAndAddTag_Click(object sender, RoutedEventArgs e)
        {
            var tagName = NewTagTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : NewTagTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                MessageBox.Show("Please enter a tag name.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Add tag to all selected files
                foreach (var filePath in _filePaths)
                {
                    DatabaseManager.Instance.AddTagToFile(filePath, tagName);
                }
                
                NewTagTextBox.Text = "Create new tag";
                NewTagTextBox.Foreground = System.Windows.Media.Brushes.Gray;
                RefreshTagNameCache();
                LoadCurrentTags();
                LoadAvailableTags();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding tag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateOnlyTag_Click(object sender, RoutedEventArgs e)
        {
            var tagName = NewTagTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : NewTagTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                MessageBox.Show("Please enter a tag name.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Get unique directories from all files
                var directories = _filePaths
                    .Select(f => Path.GetDirectoryName(f))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Create tag in all applicable directories
                foreach (var directory in directories)
                {
                    DatabaseManager.Instance.CreateStandaloneTag(directory, tagName);
                }
                
                MessageBox.Show($"Tag '{tagName}' created successfully!", "Tag Created", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                NewTagTextBox.Text = "Create new tag";
                NewTagTextBox.Foreground = System.Windows.Media.Brushes.Gray;
                RefreshTagNameCache();
                LoadAvailableTags();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating tag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddSelectedTags_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableTagsListBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select one or more tags to add.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Add each selected tag to all files
                foreach (string tagName in AvailableTagsListBox.SelectedItems)
                {
                    foreach (var filePath in _filePaths)
                    {
                        DatabaseManager.Instance.AddTagToFile(filePath, tagName);
                    }
                }
                
                RefreshTagNameCache();
                LoadCurrentTags();
                LoadAvailableTags();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding tags: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Ensure tags are synchronized when closing
            var directories = _filePaths
                .Select(f => Path.GetDirectoryName(f))
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var directory in directories)
            {
                DatabaseManager.Instance.SynchronizeDirectoryTags(directory);
            }
            
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

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
                textBox.Text = "Create new tag";
            }
        }

        #endregion

        #region Tag Autocomplete

        private void NewTagTextBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                TagSuggestionsPopup.IsOpen = false;
                CreateAndAddTag_Click(sender, e);
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

            var searchText = NewTagTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : NewTagTextBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(searchText))
            {
                TagSuggestionsPopup.IsOpen = false;
                return;
            }

            ShowTagSuggestions(searchText);
        }

        private void ShowTagSuggestions(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                TagSuggestionsPopup.IsOpen = false;
                return;
            }

            EnsureTagNamesLoaded();
            var matchingTags = _autocompleteTagNames
                .Where(name => name.StartsWith(searchText, StringComparison.OrdinalIgnoreCase))
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
                NewTagTextBox.Text = selectedTag;
                NewTagTextBox.Foreground = System.Windows.Media.Brushes.Black;
                TagSuggestionsPopup.IsOpen = false;
                NewTagTextBox.Focus();
                NewTagTextBox.CaretIndex = NewTagTextBox.Text.Length;
            }
        }

        private void TagSuggestionsListBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (TagSuggestionsListBox.SelectedItem is string selectedTag)
                {
                    NewTagTextBox.Text = selectedTag;
                    NewTagTextBox.Foreground = System.Windows.Media.Brushes.Black;
                    TagSuggestionsPopup.IsOpen = false;
                    NewTagTextBox.Focus();
                    NewTagTextBox.CaretIndex = NewTagTextBox.Text.Length;
                }
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                TagSuggestionsPopup.IsOpen = false;
                NewTagTextBox.Focus();
            }
        }

        #endregion
    }
}
