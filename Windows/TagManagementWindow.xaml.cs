using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FileTagger.Data;
using Microsoft.EntityFrameworkCore;

namespace FileTagger.Windows
{
    public partial class TagManagementWindow : Window
    {
        private readonly string _filePath;
        private FileRecord _fileRecord;

        public TagManagementWindow(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;
            FilePathTextBlock.Text = filePath;
            LoadFileRecord();
            LoadCurrentTags();
            LoadAvailableTags();
        }

        private void LoadFileRecord()
        {
            using var context = new FileTagContext();
            _fileRecord = context.FileRecords.FirstOrDefault(f => f.FilePath == _filePath);
            
            if (_fileRecord == null && File.Exists(_filePath))
            {
                // Create new file record
                _fileRecord = new FileRecord
                {
                    FilePath = _filePath,
                    FileName = Path.GetFileName(_filePath),
                    LastModified = File.GetLastWriteTime(_filePath)
                };
                context.FileRecords.Add(_fileRecord);
                context.SaveChanges();
            }
        }

        private void LoadCurrentTags()
        {
            if (_fileRecord == null) return;

            using var context = new FileTagContext();
            var fileTags = context.FileTags
                .Include(ft => ft.Tag)
                .Where(ft => ft.FileRecordId == _fileRecord.Id)
                .Select(ft => ft.Tag)
                .OrderBy(t => t.Name)
                .ToList();

            CurrentTagsListBox.ItemsSource = fileTags.Select(t => t.Name).ToList();
        }

        private void LoadAvailableTags()
        {
            if (_fileRecord == null) return;

            using var context = new FileTagContext();
            var currentTagIds = context.FileTags
                .Where(ft => ft.FileRecordId == _fileRecord.Id)
                .Select(ft => ft.TagId)
                .ToList();

            var availableTags = context.Tags
                .Where(t => !currentTagIds.Contains(t.Id))
                .OrderBy(t => t.Name)
                .Select(t => t.Name)
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
            if (CurrentTagsListBox.SelectedItem is string selectedTagName && _fileRecord != null)
            {
                using var context = new FileTagContext();
                var tag = context.Tags.FirstOrDefault(t => t.Name == selectedTagName);
                if (tag != null)
                {
                    var fileTag = context.FileTags.FirstOrDefault(ft => ft.FileRecordId == _fileRecord.Id && ft.TagId == tag.Id);
                    if (fileTag != null)
                    {
                        context.FileTags.Remove(fileTag);
                        context.SaveChanges();
                        LoadCurrentTags();
                        LoadAvailableTags();
                    }
                }
            }
        }

        private void CreateAndAddTag_Click(object sender, RoutedEventArgs e)
        {
            var tagName = NewTagTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : NewTagTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(tagName) || _fileRecord == null)
            {
                MessageBox.Show("Please enter a tag name.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var context = new FileTagContext();
            
            // Check if tag already exists
            var existingTag = context.Tags.FirstOrDefault(t => t.Name.ToLower() == tagName.ToLower());
            if (existingTag == null)
            {
                // Create new tag
                existingTag = new Tag { Name = tagName };
                context.Tags.Add(existingTag);
                context.SaveChanges();
            }

            // Check if file already has this tag
            var existingFileTag = context.FileTags.FirstOrDefault(ft => ft.FileRecordId == _fileRecord.Id && ft.TagId == existingTag.Id);
            if (existingFileTag == null)
            {
                // Add tag to file
                context.FileTags.Add(new FileTag
                {
                    FileRecordId = _fileRecord.Id,
                    TagId = existingTag.Id
                });
                context.SaveChanges();
                
                NewTagTextBox.Text = "Create new tag";
                NewTagTextBox.Foreground = System.Windows.Media.Brushes.Gray;
                LoadCurrentTags();
                LoadAvailableTags();
            }
            else
            {
                MessageBox.Show("This file already has this tag.", "Duplicate Tag", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AddSelectedTags_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableTagsListBox.SelectedItems.Count == 0 || _fileRecord == null)
            {
                MessageBox.Show("Please select one or more tags to add.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var context = new FileTagContext();
            foreach (string tagName in AvailableTagsListBox.SelectedItems)
            {
                var tag = context.Tags.FirstOrDefault(t => t.Name == tagName);
                if (tag != null)
                {
                    var existingFileTag = context.FileTags.FirstOrDefault(ft => ft.FileRecordId == _fileRecord.Id && ft.TagId == tag.Id);
                    if (existingFileTag == null)
                    {
                        context.FileTags.Add(new FileTag
                        {
                            FileRecordId = _fileRecord.Id,
                            TagId = tag.Id
                        });
                    }
                }
            }
            
            context.SaveChanges();
            LoadCurrentTags();
            LoadAvailableTags();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
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
