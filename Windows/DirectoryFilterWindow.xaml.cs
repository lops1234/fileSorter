using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FileTagger.Data;
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
            LoadFiles();
        }

        private void LoadTags()
        {
            using var context = new FileTagContext();
            var tags = context.Tags.OrderBy(t => t.Name).ToList();
            
            var filterItems = new List<TagFilterItem> { new TagFilterItem { Id = 0, Name = "All Files" } };
            filterItems.AddRange(tags.Select(t => new TagFilterItem { Id = t.Id, Name = t.Name }));
            
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
                
                using var context = new FileTagContext();
                var fileRecords = context.FileRecords
                    .Include(f => f.FileTags)
                    .ThenInclude(ft => ft.Tag)
                    .Where(f => files.Contains(f.FilePath))
                    .ToList();

                var fileDict = fileRecords.ToDictionary(f => f.FilePath, f => f);

                foreach (var filePath in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        var fileRecord = fileDict.GetValueOrDefault(filePath);
                        
                        var fileViewModel = new DirectoryFileViewModel
                        {
                            FileName = fileInfo.Name,
                            FilePath = filePath,
                            Extension = fileInfo.Extension,
                            FileSize = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTime,
                            TagsString = fileRecord != null 
                                ? string.Join(", ", fileRecord.FileTags.Select(ft => ft.Tag.Name))
                                : string.Empty
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

            if (TagFilterComboBox.SelectedItem is TagFilterItem selectedFilter && selectedFilter.Id != 0)
            {
                using var context = new FileTagContext();
                var taggedFilePaths = context.FileTags
                    .Include(ft => ft.FileRecord)
                    .Where(ft => ft.TagId == selectedFilter.Id)
                    .Select(ft => ft.FileRecord.FilePath)
                    .ToHashSet();

                filteredFiles = _allFiles.Where(f => taggedFilePaths.Contains(f.FilePath)).ToList();
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
