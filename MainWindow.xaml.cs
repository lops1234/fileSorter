using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FileTagger.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using FileTagger.Windows;

namespace FileTagger
{
    public partial class MainWindow : Window
    {
        private bool _isClosing = false;

        public MainWindow()
        {
            InitializeComponent();
            LoadDirectories();
            LoadTags();
            LoadFiles();
            LoadTagFilter();
        }

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
            using var context = new FileTagContext();
            var directories = context.WatchedDirectories.Where(d => d.IsActive).ToList();
            DirectoriesListBox.ItemsSource = directories.Select(d => d.DirectoryPath).ToList();
        }

        private void AddDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                using var context = new FileTagContext();
                var existingDir = context.WatchedDirectories.FirstOrDefault(d => d.DirectoryPath == dialog.SelectedPath);
                if (existingDir == null)
                {
                    context.WatchedDirectories.Add(new WatchedDirectory
                    {
                        DirectoryPath = dialog.SelectedPath,
                        IsActive = true
                    });
                    context.SaveChanges();
                    LoadDirectories();
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
                var result = MessageBox.Show($"Remove directory '{selectedPath}' from watched list?\nThis will not delete any files or tags.", 
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    using var context = new FileTagContext();
                    var directory = context.WatchedDirectories.FirstOrDefault(d => d.DirectoryPath == selectedPath);
                    if (directory != null)
                    {
                        directory.IsActive = false;
                        context.SaveChanges();
                        LoadDirectories();
                    }
                }
            }
        }

        private void DirectoriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveDirectoryButton.IsEnabled = DirectoriesListBox.SelectedItem != null;
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
            using var context = new FileTagContext();
            var tags = context.Tags.Include(t => t.FileTags).OrderBy(t => t.Name).ToList();
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

            using var context = new FileTagContext();
            var existingTag = context.Tags.FirstOrDefault(t => t.Name.ToLower() == tagName.ToLower());
            if (existingTag != null)
            {
                MessageBox.Show("A tag with this name already exists.", "Duplicate Tag", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var tagDescription = TagDescriptionTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : TagDescriptionTextBox.Text?.Trim();
            var tag = new Tag
            {
                Name = tagName,
                Description = string.IsNullOrWhiteSpace(tagDescription) ? null : tagDescription
            };

            context.Tags.Add(tag);
            context.SaveChanges();

            NewTagTextBox.Text = "Enter new tag name";
            NewTagTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            TagDescriptionTextBox.Text = "Tag description (optional)";
            TagDescriptionTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            LoadTags();
            LoadTagFilter();
        }

        private void DeleteTag_Click(object sender, RoutedEventArgs e)
        {
            if (TagsDataGrid.SelectedItem is Tag selectedTag)
            {
                var result = MessageBox.Show($"Delete tag '{selectedTag.Name}'?\nThis will remove the tag from all files.", 
                    "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    using var context = new FileTagContext();
                    var tag = context.Tags.Find(selectedTag.Id);
                    if (tag != null)
                    {
                        context.Tags.Remove(tag);
                        context.SaveChanges();
                        LoadTags();
                        LoadTagFilter();
                        LoadFiles();
                    }
                }
            }
        }

        private void RefreshTags_Click(object sender, RoutedEventArgs e)
        {
            LoadTags();
        }

        #endregion

        #region File Browser

        private void LoadFiles()
        {
            using var context = new FileTagContext();
            var files = context.FileRecords
                .Include(f => f.FileTags)
                .ThenInclude(ft => ft.Tag)
                .OrderBy(f => f.FileName)
                .ToList();

            var fileViewModels = files.Select(f => new FileViewModel
            {
                Id = f.Id,
                FileName = f.FileName,
                FilePath = f.FilePath,
                LastModified = f.LastModified,
                TagsString = string.Join(", ", f.FileTags.Select(ft => ft.Tag.Name))
            }).ToList();

            FilesDataGrid.ItemsSource = fileViewModels;
        }

        private void LoadTagFilter()
        {
            using var context = new FileTagContext();
            var tags = context.Tags.OrderBy(t => t.Name).ToList();
            
            var filterItems = new List<TagFilterItem> { new TagFilterItem { Id = 0, Name = "All Files" } };
            filterItems.AddRange(tags.Select(t => new TagFilterItem { Id = t.Id, Name = t.Name }));
            
            TagFilterComboBox.ItemsSource = filterItems;
            TagFilterComboBox.DisplayMemberPath = "Name";
            TagFilterComboBox.SelectedIndex = 0;
        }

        private void TagFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TagFilterComboBox.SelectedItem is TagFilterItem selectedFilter)
            {
                if (selectedFilter.Id == 0)
                {
                    LoadFiles(); // Show all files
                }
                else
                {
                    using var context = new FileTagContext();
                    var files = context.FileRecords
                        .Include(f => f.FileTags)
                        .ThenInclude(ft => ft.Tag)
                        .Where(f => f.FileTags.Any(ft => ft.TagId == selectedFilter.Id))
                        .OrderBy(f => f.FileName)
                        .ToList();

                    var fileViewModels = files.Select(f => new FileViewModel
                    {
                        Id = f.Id,
                        FileName = f.FileName,
                        FilePath = f.FilePath,
                        LastModified = f.LastModified,
                        TagsString = string.Join(", ", f.FileTags.Select(ft => ft.Tag.Name))
                    }).ToList();

                    FilesDataGrid.ItemsSource = fileViewModels;
                }
            }
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TagFilterComboBox.SelectedIndex = 0;
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
                    LoadFiles(); // Refresh the file list
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

        #endregion
    }

    public class FileViewModel
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public string TagsString { get; set; } = string.Empty;
    }

    public class TagFilterItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
