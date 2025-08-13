using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FileTagger.Services;

namespace FileTagger.Windows
{
    public partial class TagManagementWindow : Window
    {
        private readonly string _filePath;
        private readonly string _originalFilePath;

        public TagManagementWindow(string filePath)
        {
            InitializeComponent();
            _originalFilePath = filePath;
            _filePath = filePath;
            
            // Check if this is a temp file and show appropriate info
            if (TempResultsManager.Instance.IsInTempDirectory(filePath))
            {
                var mappedPath = TempResultsManager.Instance.GetOriginalFilePath(filePath);
                if (!string.IsNullOrEmpty(mappedPath))
                {
                    _filePath = mappedPath; // Use original file path for all operations
                    
                    // Show temp file info
                    TempFileInfoPanel.Visibility = System.Windows.Visibility.Visible;
                    TempFilePathTextBlock.Text = filePath;
                    OriginalFilePathTextBlock.Text = mappedPath;
                    FilePathTextBlock.Text = mappedPath;
                }
                else
                {
                    FilePathTextBlock.Text = filePath;
                }
            }
            else
            {
                TempFileInfoPanel.Visibility = System.Windows.Visibility.Collapsed;
                FilePathTextBlock.Text = filePath;
            }
            
            LoadCurrentTags();
            LoadAvailableTags();
        }

        private void LoadFileRecord()
        {
            // File record will be created automatically when tags are added
            // This is now handled by the DatabaseManager
        }

        private void LoadCurrentTags()
        {
            var allFiles = DatabaseManager.Instance.GetAllFilesWithTags();
            var currentFile = allFiles.FirstOrDefault(f => f.FullPath == _filePath);
            
            if (currentFile != null)
            {
                CurrentTagsListBox.ItemsSource = currentFile.Tags;
            }
            else
            {
                CurrentTagsListBox.ItemsSource = new List<string>();
            }
        }

        private void LoadAvailableTags()
        {
            var allTags = DatabaseManager.Instance.GetAllAvailableTags();
            var allFiles = DatabaseManager.Instance.GetAllFilesWithTags();
            var currentFile = allFiles.FirstOrDefault(f => f.FullPath == _filePath);
            var currentTags = currentFile?.Tags ?? new List<string>();

            var availableTags = allTags
                .Where(t => !currentTags.Contains(t.Name))
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
            if (CurrentTagsListBox.SelectedItem is string selectedTagName)
            {
                try
                {
                    // Get the appropriate watched directory for this file
                    var watchedDirectory = DatabaseManager.Instance.GetWatchedDirectoryForFile(_filePath);
                    if (string.IsNullOrEmpty(watchedDirectory))
                    {
                        MessageBox.Show("This file is not in a watched directory. Please add the directory to File Tagger settings first.", 
                            "File Tagger", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var relativePath = Path.GetRelativePath(watchedDirectory, _filePath);

                    using var dirDb = DatabaseManager.Instance.GetDirectoryDb(watchedDirectory);
                    var fileRecord = dirDb.LocalFileRecords.FirstOrDefault(f => f.RelativePath == relativePath);
                    if (fileRecord != null)
                    {
                        var tag = dirDb.LocalTags.FirstOrDefault(t => t.Name == selectedTagName);
                        if (tag != null)
                        {
                            var fileTag = dirDb.LocalFileTags.FirstOrDefault(ft => ft.LocalFileRecordId == fileRecord.Id && ft.LocalTagId == tag.Id);
                            if (fileTag != null)
                            {
                                dirDb.LocalFileTags.Remove(fileTag);
                                dirDb.SaveChanges();

                                // Sync changes to main database
                                DatabaseManager.Instance.SynchronizeDirectoryTags(watchedDirectory);
                                
                                LoadCurrentTags();
                                LoadAvailableTags();
                            }
                        }
                    }
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
                DatabaseManager.Instance.AddTagToFile(_filePath, tagName);
                
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
                var directoryPath = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    DatabaseManager.Instance.CreateStandaloneTag(directoryPath, tagName);
                    
                    MessageBox.Show($"Tag '{tagName}' created successfully!", "Tag Created", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    NewTagTextBox.Text = "Create new tag";
                    NewTagTextBox.Foreground = System.Windows.Media.Brushes.Gray;
                    LoadAvailableTags();
                }
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
                foreach (string tagName in AvailableTagsListBox.SelectedItems)
                {
                    DatabaseManager.Instance.AddTagToFile(_filePath, tagName);
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
            var directoryPath = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                DatabaseManager.Instance.SynchronizeDirectoryTags(directoryPath);
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
    }
}
