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
            LoadFiles();
        }

        private void LoadTags()
        {
            var tags = DatabaseManager.Instance.GetAllAvailableTags();
            
            var filterItems = new List<TagFilterItem> { new TagFilterItem { Name = "All Files" } };
            filterItems.AddRange(tags.Select(t => new TagFilterItem { Name = t.Name }));
            
            TagFilterComboBox.ItemsSource = filterItems;
            TagFilterComboBox.DisplayMemberPath = "Name";
            TagFilterComboBox.SelectedIndex = 0;
        }

        private void LoadFiles()
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

                ApplyCurrentFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyCurrentFilter()
        {
            var filteredFiles = _allFiles;

            if (TagFilterComboBox.SelectedItem is TagFilterItem selectedFilter && selectedFilter.Name != "All Files")
            {
                filteredFiles = _allFiles.Where(f => !string.IsNullOrEmpty(f.TagsString) && 
                    f.TagsString.Split(',').Select(t => t.Trim()).Contains(selectedFilter.Name)).ToList();
            }

            FilesDataGrid.ItemsSource = filteredFiles.OrderBy(f => f.FileName).ToList();
            FileCountTextBlock.Text = $"{filteredFiles.Count} files";
        }

        private void TagFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyCurrentFilter();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TagFilterComboBox.SelectedIndex = 0;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadTags();
            LoadFiles();
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
                    LoadFiles(); // Refresh the file list
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
