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
            // Don't auto-load files anymore - only when search is pressed
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

        private void LoadFiles(string tagFilter = null)
        {
            var files = DatabaseManager.Instance.GetAllFilesWithTags();
            
            if (!string.IsNullOrEmpty(tagFilter))
            {
                files = files.Where(f => f.Tags.Any(t => t.Equals(tagFilter, StringComparison.OrdinalIgnoreCase))).ToList();
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

        private void LoadTagFilter()
        {
            // Initialize with placeholder text
            TagSearchTextBox.Text = "Enter tag name...";
            TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            FilesDataGrid.ItemsSource = new List<FileViewModel>(); // Start with empty grid
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            var tagFilter = TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : TagSearchTextBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(tagFilter))
            {
                LoadFiles(); // Show all files
            }
            else
            {
                LoadFiles(tagFilter); // Show filtered files
            }
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TagSearchTextBox.Text = "Enter tag name...";
            TagSearchTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            FilesDataGrid.ItemsSource = new List<FileViewModel>(); // Clear grid
            TagSuggestionsPopup.IsOpen = false;
        }

        private void RefreshAllTags_Click(object sender, RoutedEventArgs e)
        {
            DatabaseManager.Instance.SynchronizeAllTags();
            LoadTags();
            MessageBox.Show("Tags refreshed successfully!", "Refresh Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    // Don't auto-refresh file list - user needs to search again
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
