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

        public TagManagementWindow(List<string> filePaths)
        {
            InitializeComponent();
            _originalFilePaths = new List<string>(filePaths);
            _filePaths = new List<string>();
            
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
            
            // Update UI based on file count
            if (_filePaths.Count == 1)
            {
                FileHeaderTextBlock.Text = "File:";
                FilePathTextBlock.Text = _filePaths[0];
                TempFileInfoPanel.Visibility = System.Windows.Visibility.Collapsed;
                
                // Check if single file is from temp directory
                if (TempResultsManager.Instance.IsInTempDirectory(filePaths[0]))
                {
                    var mappedPath = TempResultsManager.Instance.GetOriginalFilePath(filePaths[0]);
                    if (!string.IsNullOrEmpty(mappedPath))
                    {
                        TempFileInfoPanel.Visibility = System.Windows.Visibility.Visible;
                        TempFilePathTextBlock.Text = filePaths[0];
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
                TempFileInfoPanel.Visibility = System.Windows.Visibility.Collapsed;
            }
            
            LoadCurrentTags();
            LoadAvailableTags();
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
            if (_filePaths.Count == 0)
            {
                CurrentTagsListBox.ItemsSource = new List<TagDisplayItem>();
                return;
            }

            // Get tags for all files
            var allFileTags = new List<List<string>>();
            foreach (var filePath in _filePaths)
            {
                var tags = DatabaseManager.Instance.GetTagsForFile(filePath);
                allFileTags.Add(tags);
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

                CurrentTagsListBox.ItemsSource = displayItems;
            }
        }

        private void LoadAvailableTags()
        {
            var allTags = DatabaseManager.Instance.GetAllAvailableTags();
            
            // Get current tags
            var currentTags = (CurrentTagsListBox.ItemsSource as List<TagDisplayItem> ?? new List<TagDisplayItem>())
                .Select(t => t.TagName)
                .ToList();

            // Show tags that are not already on the files
            var availableTags = allTags
                .Where(t => !currentTags.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .OrderBy(t => t)
                .ToList();

            AvailableTagsListBox.ItemsSource = availableTags;
        }

        private void CurrentTagsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveSelectedTagButton.IsEnabled = CurrentTagsListBox.SelectedItem != null;
        }

        private void RemoveSelectedTag_Click(object sender, RoutedEventArgs e)
        {
            RemoveTag_Click(sender, e);
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentTagsListBox.SelectedItem is TagDisplayItem selectedTag)
            {
                try
                {
                    // Remove tag from all selected files (only removes from files that have it)
                    foreach (var filePath in _filePaths)
                    {
                        DatabaseManager.Instance.RemoveTagFromFile(filePath, selectedTag.TagName);
                    }
                    
                    LoadCurrentTags();
                    LoadAvailableTags();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error removing tag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
