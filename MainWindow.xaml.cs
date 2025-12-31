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
        
        // Track edited tags: OriginalName -> (NewName, NewDescription)
        private Dictionary<string, (string NewName, string NewDescription)> _editedTags = new Dictionary<string, (string, string)>();
        private string _currentEditingTagName = null;

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
            var directoryViewModels = new List<DirectoryViewModel>();
            var centralDbPath = DatabaseManager.Instance.GetCentralDatabasePath();
            
            foreach (var dir in directories)
            {
                var viewModel = new DirectoryViewModel
                {
                    DirectoryPath = dir,
                    DatabasePath = centralDbPath + $" (data for {Path.GetFileName(dir)})"
                };
                
                viewModel.StatusColor = System.Windows.Media.Brushes.Green;
                
                // Check for folder databases (old system or for push/pull)
                var folderDbPath = Path.Combine(dir, ".filetagger", "tags.db");
                if (File.Exists(folderDbPath))
                {
                    viewModel.HasWarning = true;
                    viewModel.WarningMessage = "📁 Has folder database (can Pull/Push)";
                    viewModel.StatusColor = System.Windows.Media.Brushes.Blue;
                }
                
                // Check for duplicates
                var duplicates = new List<string>();
                for (int i = 1; i <= 20; i++)
                {
                    var dupPath = Path.Combine(dir, $".filetagger ({i})", "tags.db");
                    if (File.Exists(dupPath))
                    {
                        duplicates.Add($".filetagger ({i})");
                    }
                }
                
                if (duplicates.Any())
                {
                    viewModel.HasWarning = true;
                    viewModel.WarningMessage = $"⚠️ Duplicates found: {string.Join(", ", duplicates)} - Use Cleanup!";
                    viewModel.StatusColor = System.Windows.Media.Brushes.Orange;
                }
                
                directoryViewModels.Add(viewModel);
            }
            
            DirectoriesListBox.ItemsSource = directoryViewModels;
            
            // Update database info if a directory is selected
            if (DirectoriesListBox.SelectedItem is DirectoryViewModel selectedViewModel)
            {
                UpdateDatabaseInfo(selectedViewModel.DirectoryPath);
            }
            else
            {
                // Show central database info when no directory is selected
                CurrentDatabaseLabel.Text = "Central Database (all tags stored locally):";
                CurrentDatabasePath.Text = centralDbPath;
                CurrentDatabasePath.Foreground = File.Exists(centralDbPath) 
                    ? System.Windows.Media.Brushes.Green 
                    : System.Windows.Media.Brushes.Red;
            }
        }
        
        private void UpdateDatabaseInfo(string directoryPath)
        {
            var centralDbPath = DatabaseManager.Instance.GetCentralDatabasePath();
            CurrentDatabaseLabel.Text = "Central Database (all tags stored here):";
            CurrentDatabasePath.Text = centralDbPath;
            CurrentDatabasePath.Foreground = File.Exists(centralDbPath) 
                ? System.Windows.Media.Brushes.Green 
                : System.Windows.Media.Brushes.Red;
            
            // Check for folder databases
            var folderDbPath = Path.Combine(directoryPath, ".filetagger", "tags.db");
            if (File.Exists(folderDbPath))
            {
                CurrentDatabasePath.Text += $"\n📁 Folder backup exists: {folderDbPath}";
            }
            
            // Check for duplicate databases
            var duplicates = new List<string>();
            for (int i = 1; i <= 20; i++)
            {
                var dupPath = Path.Combine(directoryPath, $".filetagger ({i})", "tags.db");
                if (File.Exists(dupPath))
                {
                    duplicates.Add($".filetagger ({i})");
                }
            }
            
            if (duplicates.Any())
            {
                CurrentDatabasePath.Text += $"\n⚠️ Duplicate folder DBs: {string.Join(", ", duplicates)}";
                CurrentDatabasePath.Foreground = System.Windows.Media.Brushes.Orange;
            }
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
            if (DirectoriesListBox.SelectedItem is DirectoryViewModel selectedViewModel)
            {
                var result = MessageBox.Show($"Remove directory '{selectedViewModel.DirectoryPath}' from watched list?\nTags unique to this directory will be removed from the main tag list.", 
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    DatabaseManager.Instance.RemoveWatchedDirectory(selectedViewModel.DirectoryPath);
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
            
            // Update the database path label
            if (DirectoriesListBox.SelectedItem is DirectoryViewModel selectedViewModel)
            {
                UpdateDatabaseInfo(selectedViewModel.DirectoryPath);
            }
            else
            {
                // Show central database when no directory is selected
                var centralDbPath = DatabaseManager.Instance.GetCentralDatabasePath();
                CurrentDatabaseLabel.Text = "Central Database (all tags stored locally):";
                CurrentDatabasePath.Text = centralDbPath;
                CurrentDatabasePath.Foreground = File.Exists(centralDbPath) 
                    ? System.Windows.Media.Brushes.Green 
                    : System.Windows.Media.Brushes.Red;
            }
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

        private void DiagnoseContextMenus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var diagnosticInfo = ShellIntegration.GetDiagnosticInfo();
                MessageBox.Show(diagnosticInfo, "Context Menu Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error getting diagnostic info: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void MergeDuplicates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "MERGE: Find and merge duplicate .filetagger databases:\n\n" +
                    "What it does:\n" +
                    "  1. Scans ALL watched directories for duplicates like:\n" +
                    "     • .filetagger (1)\n" +
                    "     • .filetagger (2)\n" +
                    "     • etc.\n" +
                    "  2. Imports all data from duplicates into central database\n" +
                    "  3. Deletes the duplicate folders\n" +
                    "  4. Keeps the original .filetagger folder intact\n\n" +
                    "Use this when you only want to clean up duplicates but\n" +
                    "keep the original folder database.\n\n" +
                    "Do you want to continue?",
                    "Merge Duplicate Databases",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var mergeResult = DatabaseManager.Instance.MergeAllDuplicateFileTaggerDatabases();

                    if (mergeResult.DuplicateDatabasesFound == 0)
                    {
                        MessageBox.Show("No duplicate databases found!\n\nYour databases are clean.",
                            "No Duplicates", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var message = $"Database merge completed!\n\n" +
                                 $"Directories checked: {DatabaseManager.Instance.GetAllActiveDirectories().Count}\n" +
                                 $"Directories with duplicates: {mergeResult.DirectoriesWithDuplicates}\n" +
                                 $"Duplicate databases found: {mergeResult.DuplicateDatabasesFound}\n" +
                                 $"Duplicate databases merged: {mergeResult.DuplicateDatabasesDeleted}\n\n" +
                                 $"Tags merged: {mergeResult.TagsMerged}\n" +
                                 $"Files merged: {mergeResult.FilesMerged}\n" +
                                 $"Associations merged: {mergeResult.AssociationsMerged}";

                    if (mergeResult.Errors.Any())
                    {
                        message += $"\n\n⚠️ Errors encountered: {mergeResult.Errors.Count}\n" +
                                  string.Join("\n", mergeResult.Errors.Take(5));
                        if (mergeResult.Errors.Count > 5)
                        {
                            message += $"\n... and {mergeResult.Errors.Count - 5} more errors";
                        }
                        
                        // Check if any errors are about locked databases
                        if (mergeResult.Errors.Any(e => e.Contains("locked") || e.Contains("being used")))
                        {
                            message += "\n\n💡 TIP: Close all applications that might be accessing the databases\n" +
                                      "(e.g., database viewers, file explorers, other FileTagger instances)\n" +
                                      "and try again.";
                        }
                    }

                    var messageType = mergeResult.Errors.Any() ? MessageBoxImage.Warning : MessageBoxImage.Information;
                    MessageBox.Show(message, "Merge Complete", MessageBoxButton.OK, messageType);

                    // Refresh the UI to show updated data
                    LoadDirectories();
                    LoadTags();
                    LoadTagFilter();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during database merge: {ex.Message}",
                    "Merge Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void VerifyFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "This will check all tagged files to ensure they still exist.\n\n" +
                    "Missing files will be removed from the database and tag counts will be updated.\n\n" +
                    "This fixes issues where tags show a count but searching returns no results.\n\n" +
                    "Do you want to continue?",
                    "Verify Tagged Files",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var verificationResult = DatabaseManager.Instance.VerifyAndCleanupTaggedFiles();

                    var message = $"File verification completed!\n\n" +
                                 $"Total files checked: {verificationResult.TotalFilesChecked}\n" +
                                 $"Existing files: {verificationResult.ExistingFilesCount}\n" +
                                 $"Missing files removed: {verificationResult.MissingFilesCount}";

                    if (verificationResult.AffectedTags.Any())
                    {
                        message += $"\n\nTags affected: {verificationResult.AffectedTags.Count}\n";
                        var tagList = string.Join(", ", verificationResult.AffectedTags.Take(10));
                        message += tagList;
                        if (verificationResult.AffectedTags.Count > 10)
                        {
                            message += $", and {verificationResult.AffectedTags.Count - 10} more...";
                        }
                    }

                    if (verificationResult.Errors.Any())
                    {
                        message += $"\n\nErrors encountered: {verificationResult.Errors.Count}\n" +
                                  string.Join("\n", verificationResult.Errors.Take(5));
                        if (verificationResult.Errors.Count > 5)
                        {
                            message += $"\n... and {verificationResult.Errors.Count - 5} more errors";
                        }
                    }

                    if (verificationResult.MissingFilesCount > 0)
                    {
                        message += "\n\nTag counts have been updated. Try searching again!";
                    }

                    MessageBox.Show(message, "Verification Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Refresh the UI to show updated data
                    LoadDirectories();
                    LoadTags();
                    LoadTagFilter();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during file verification: {ex.Message}",
                    "Verification Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScanForExisting_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show progress message
                var originalText = ScanForExistingButton.Content.ToString();
                ScanForExistingButton.Content = "Scanning...";
                ScanForExistingButton.IsEnabled = false;

                // Perform the scan
                var discoveredDirectories = DatabaseManager.Instance.DiscoverAndImportExistingDatabases();

                if (discoveredDirectories.Any())
                {
                    var directoryList = string.Join("\n• ", discoveredDirectories);
                    MessageBox.Show($"Successfully discovered and imported {discoveredDirectories.Count} directories with existing .filetagger databases:\n\n• {directoryList}", 
                        "Directories Discovered", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Refresh the UI to show newly imported directories
                    LoadDirectories();
                    LoadTags();
                    LoadTagFilter();
                    
                    // Refresh context menus to ensure they work with new directories
                    ShellIntegration.RefreshContextMenus();
                }
                else
                {
                    MessageBox.Show("No new .filetagger directories were found. All existing databases are already being watched.", 
                        "No New Directories Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning for existing databases: {ex.Message}", 
                    "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Restore button state
                ScanForExistingButton.Content = "Scan for Existing Databases";
                ScanForExistingButton.IsEnabled = true;
            }
        }

        private void PullFromFolder_Click(object sender, RoutedEventArgs e)
        {
            if (DirectoriesListBox.SelectedItem is not DirectoryViewModel selectedViewModel)
            {
                MessageBox.Show("Please select a directory first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var result = MessageBox.Show(
                    $"Pull tags from folder database into central database?\n\n" +
                    $"Directory: {selectedViewModel.DirectoryPath}\n\n" +
                    $"This will import all tags from any .filetagger databases in this folder.",
                    "Pull from Folder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var pullResult = DatabaseManager.Instance.PullFromFolder(selectedViewModel.DirectoryPath);

                    var message = $"Pull completed!\n\n" +
                                 $"Databases found: {pullResult.DatabasesFound}\n" +
                                 $"Databases pulled: {pullResult.DatabasesPulled}\n" +
                                 $"Tags imported: {pullResult.TagsImported}\n" +
                                 $"Files imported: {pullResult.FilesImported}\n" +
                                 $"Associations imported: {pullResult.AssociationsImported}";

                    if (pullResult.Errors.Any())
                    {
                        message += $"\n\nErrors: {string.Join("\n", pullResult.Errors.Take(5))}";
                    }

                    MessageBox.Show(message, "Pull Complete", MessageBoxButton.OK, 
                        pullResult.Errors.Any() ? MessageBoxImage.Warning : MessageBoxImage.Information);

                    LoadDirectories();
                    LoadTags();
                    LoadTagFilter();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during pull: {ex.Message}", "Pull Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PushToFolder_Click(object sender, RoutedEventArgs e)
        {
            if (DirectoriesListBox.SelectedItem is not DirectoryViewModel selectedViewModel)
            {
                MessageBox.Show("Please select a directory first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var result = MessageBox.Show(
                    $"Push tags to folder database?\n\n" +
                    $"Directory: {selectedViewModel.DirectoryPath}\n\n" +
                    $"This will export all tags for this directory to:\n" +
                    $"{Path.Combine(selectedViewModel.DirectoryPath, ".filetagger", "tags.db")}\n\n" +
                    $"Use this to backup tags or sync with another computer via cloud storage.",
                    "Push to Folder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var pushResult = DatabaseManager.Instance.PushToFolder(selectedViewModel.DirectoryPath);

                    var message = $"Push completed!\n\n" +
                                 $"Tags exported: {pushResult.TagsExported}\n" +
                                 $"Files exported: {pushResult.FilesExported}\n" +
                                 $"Associations exported: {pushResult.AssociationsExported}";

                    if (pushResult.Errors.Any())
                    {
                        message += $"\n\nErrors: {string.Join("\n", pushResult.Errors.Take(5))}";
                    }

                    MessageBox.Show(message, "Push Complete", MessageBoxButton.OK, 
                        pushResult.Errors.Any() ? MessageBoxImage.Warning : MessageBoxImage.Information);

                    LoadDirectories();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during push: {ex.Message}", "Push Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CleanupFolder_Click(object sender, RoutedEventArgs e)
        {
            if (DirectoriesListBox.SelectedItem is not DirectoryViewModel selectedViewModel)
            {
                MessageBox.Show("Please select a directory first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var result = MessageBox.Show(
                    $"CLEANUP: Complete reset of folder databases\n\n" +
                    $"Directory: {selectedViewModel.DirectoryPath}\n\n" +
                    $"What it does:\n" +
                    $"  1. Import ALL data from folder into central database\n" +
                    $"  2. DELETE ALL .filetagger folders:\n" +
                    $"     • .filetagger (the original)\n" +
                    $"     • .filetagger (1), (2), etc. (duplicates)\n" +
                    $"  3. Create fresh .filetagger with clean database\n\n" +
                    $"This completely fixes Google Drive sync conflicts\n" +
                    $"by starting fresh with a single clean database.\n\n" +
                    $"⚠️ All folder databases will be deleted!",
                    "Cleanup Folder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var cleanupResult = DatabaseManager.Instance.CleanupFolder(selectedViewModel.DirectoryPath);

                    var message = $"Cleanup completed!\n\n" +
                                 $"Directories deleted: {cleanupResult.DirectoriesDeleted}\n";

                    if (cleanupResult.PullResult != null)
                    {
                        message += $"\nPull: {cleanupResult.PullResult.TagsImported} tags, {cleanupResult.PullResult.FilesImported} files imported";
                    }

                    if (cleanupResult.PushResult != null)
                    {
                        message += $"\nPush: {cleanupResult.PushResult.TagsExported} tags, {cleanupResult.PushResult.FilesExported} files exported";
                    }

                    if (cleanupResult.Errors.Any())
                    {
                        message += $"\n\nErrors: {string.Join("\n", cleanupResult.Errors.Take(5))}";
                    }

                    MessageBox.Show(message, "Cleanup Complete", MessageBoxButton.OK, 
                        cleanupResult.Errors.Any() ? MessageBoxImage.Warning : MessageBoxImage.Information);

                    LoadDirectories();
                    LoadTags();
                    LoadTagFilter();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during cleanup: {ex.Message}", "Cleanup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Tag Management

        private List<TagInfo> _allTags = new List<TagInfo>();
        // Track original database names for each TagInfo object
        private Dictionary<TagInfo, string> _originalTagNames = new Dictionary<TagInfo, string>();

        private void LoadTags()
        {
            _allTags = DatabaseManager.Instance.GetAllAvailableTags();
            // Store original names for edit tracking
            _originalTagNames.Clear();
            foreach (var tag in _allTags)
            {
                _originalTagNames[tag] = tag.Name;
            }
            ApplyTagFilter();
        }

        private void ApplyTagFilter()
        {
            var filterText = TagFilterTextBox?.Text?.Trim() ?? "";
            
            IEnumerable<TagInfo> filteredTags = _allTags;
            
            if (!string.IsNullOrEmpty(filterText))
            {
                filteredTags = _allTags.Where(t => 
                    t.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                    (t.Description?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var tagsList = filteredTags.ToList();
            TagsDataGrid.ItemsSource = tagsList;
            
            // Update count label
            if (TagCountLabel != null)
            {
                if (string.IsNullOrEmpty(filterText))
                {
                    TagCountLabel.Text = $"{_allTags.Count} tags";
                }
                else
                {
                    TagCountLabel.Text = $"Showing {tagsList.Count} of {_allTags.Count} tags";
                }
            }
        }

        private void TagFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyTagFilter();
        }

        private void ClearTagFilter_Click(object sender, RoutedEventArgs e)
        {
            TagFilterTextBox.Text = "";
            ApplyTagFilter();
        }

        private void CreateTag_Click(object sender, RoutedEventArgs e)
        {
            var tagName = NewTagTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : NewTagTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                MessageBox.Show("Please enter a tag name.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var tagDescription = TagDescriptionTextBox.Foreground == System.Windows.Media.Brushes.Gray ? "" : TagDescriptionTextBox.Text?.Trim();

            var existingTags = DatabaseManager.Instance.GetAllAvailableTags();
            if (existingTags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A tag with this name already exists.", "Duplicate Tag", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var activeDirectories = DatabaseManager.Instance.GetAllActiveDirectories();
            if (!activeDirectories.Any())
            {
                MessageBox.Show("No directories are configured for tagging. Please add directories in the Settings tab first.", 
                    "No Directories", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Create standalone tag in all active directories
                DatabaseManager.Instance.CreateStandaloneTagInAllDirectories(tagName, tagDescription);
                
                MessageBox.Show($"Tag '{tagName}' created successfully in all watched directories!", 
                    "Tag Created", MessageBoxButton.OK, MessageBoxImage.Information);

                // Clear input fields
                NewTagTextBox.Text = "Enter new tag name";
                NewTagTextBox.Foreground = System.Windows.Media.Brushes.Gray;
                TagDescriptionTextBox.Text = "Tag description (optional)";
                TagDescriptionTextBox.Foreground = System.Windows.Media.Brushes.Gray;
                
                // Refresh tags display
                LoadTags();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating tag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteTag_Click(object sender, RoutedEventArgs e)
        {
            if (TagsDataGrid.SelectedItem is TagInfo selectedTag)
            {
                var result = MessageBox.Show($"Delete tag '{selectedTag.Name}'?\nThis will remove the tag from all files in all directories.", 
                    "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Delete tag from central database
                        DatabaseManager.Instance.DeleteTag(selectedTag.Name);
                        LoadTags();
                        LoadTagFilter();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error deleting tag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void RefreshTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Resynchronize all tags from directory databases
                // This will clean up tags with zero usage count
                DatabaseManager.Instance.SynchronizeAllTags();
                LoadTags();
                ClearTagEdits();
                MessageBox.Show("Tags refreshed and orphaned tags cleaned up successfully!", 
                    "Refresh Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing tags: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TagsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteTagButton.IsEnabled = TagsDataGrid.SelectedItem != null;
        }

        private void TagsDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            // Store the ORIGINAL database name when editing starts
            if (e.Row.Item is TagInfo tagInfo)
            {
                // Use tracked original name, not the current (possibly already edited) UI value
                if (_originalTagNames.TryGetValue(tagInfo, out var originalName))
                {
                    _currentEditingTagName = originalName;
                }
                else
                {
                    _currentEditingTagName = tagInfo.Name;
                }
            }
        }

        private void TagsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Cancel)
            {
                _currentEditingTagName = null;
                return;
            }

            if (e.Row.Item is TagInfo tagInfo && !string.IsNullOrEmpty(_currentEditingTagName))
            {
                var originalDbName = _currentEditingTagName;
                
                // Get the edited value directly from the TextBox
                string editedValue = "";
                if (e.EditingElement is TextBox textBox)
                {
                    editedValue = textBox.Text?.Trim() ?? "";
                }

                // Determine which column was edited
                var columnHeader = e.Column.Header?.ToString() ?? "";
                
                // Get current values (use existing edit if available, otherwise from tagInfo)
                string currentName;
                string currentDescription;
                
                if (_editedTags.TryGetValue(originalDbName, out var existing))
                {
                    currentName = existing.NewName;
                    currentDescription = existing.NewDescription;
                }
                else
                {
                    currentName = tagInfo.Name ?? "";
                    currentDescription = tagInfo.Description ?? "";
                }

                // Update the appropriate field
                if (columnHeader == "Name")
                {
                    currentName = editedValue;
                }
                else if (columnHeader == "Description")
                {
                    currentDescription = editedValue;
                }

                // Store the edit
                _editedTags[originalDbName] = (currentName, currentDescription);

                // Enable save button
                SaveTagChangesButton.IsEnabled = _editedTags.Count > 0;
                _currentEditingTagName = null;
            }
        }

        private void SaveTagChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_editedTags.Count == 0)
            {
                MessageBox.Show("No changes to save.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                int renamedCount = 0;
                int descriptionUpdatedCount = 0;
                var errors = new List<string>();

                foreach (var edit in _editedTags)
                {
                    var originalName = edit.Key;
                    var (newName, newDescription) = edit.Value;

                    // Check if name was changed
                    if (!string.Equals(originalName, newName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(newName))
                        {
                            errors.Add($"Cannot rename '{originalName}' to empty name.");
                            continue;
                        }

                        // Check for duplicate name
                        var existingTags = DatabaseManager.Instance.GetAllAvailableTags();
                        if (existingTags.Any(t => t.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) && 
                                                   !t.Name.Equals(originalName, StringComparison.OrdinalIgnoreCase)))
                        {
                            errors.Add($"Cannot rename '{originalName}' to '{newName}' - tag already exists.");
                            continue;
                        }

                        DatabaseManager.Instance.UpdateTagName(originalName, newName);
                        renamedCount++;
                    }

                    // Update description
                    DatabaseManager.Instance.UpdateTagDescription(newName.Length > 0 ? newName : originalName, newDescription);
                    descriptionUpdatedCount++;
                }

                // Clear edits and reload
                ClearTagEdits();
                LoadTags();
                LoadTagFilter();

                var message = new List<string>();
                if (renamedCount > 0)
                    message.Add($"{renamedCount} tag(s) renamed");
                if (descriptionUpdatedCount > 0)
                    message.Add($"{descriptionUpdatedCount} description(s) updated");
                if (errors.Count > 0)
                    message.Add($"\n\nErrors:\n" + string.Join("\n", errors));

                MessageBox.Show(string.Join(", ", message), "Save Complete", 
                    MessageBoxButton.OK, errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearTagEdits()
        {
            _editedTags.Clear();
            SaveTagChangesButton.IsEnabled = false;
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
            // Cancel any ongoing copy operations
            TempResultsManager.Instance.CancelCurrentCopyOperation();
            
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
            // Cancel any ongoing copy operations
            TempResultsManager.Instance.CancelCurrentCopyOperation();
            
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
            // Cancel any ongoing copy operations
            TempResultsManager.Instance.CancelCurrentCopyOperation();
            
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

                var filePaths = files.Select(f => f.FilePath).ToList();

                // Check if search parameters have changed
                if (!TempResultsManager.Instance.HasSearchParametersChanged(searchQuery, filePaths))
                {
                    // Same search parameters - just open Explorer with existing files
                    var tempDir = TempResultsManager.Instance.GetTempDirectoryPath();
                    if (Directory.Exists(tempDir))
                    {
                        Process.Start("explorer.exe", $"\"{tempDir}\"");
                        SearchStatusTextBlock.Text = $"Opened existing search results in Explorer ({files.Count} files)";
                        SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                        return;
                    }
                }

                // Different search parameters or no existing temp directory - copy files
                // Prepare temp directory
                var tempDirPath = TempResultsManager.Instance.PrepareTempDirectory();

                // Create README file first
                TempResultsManager.Instance.CreateReadmeFile(searchQuery, files.Count);

                // Open Explorer immediately with README
                Process.Start("explorer.exe", $"\"{tempDirPath}\"");

                // Update status to show copying is starting
                SearchStatusTextBlock.Text = "Copying files to temporary directory... (0/0)";
                SearchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;

                // Start copying files asynchronously
                var result = await TempResultsManager.Instance.CopySearchResultsAsync(filePaths, searchQuery, (copied, total) =>
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
            return c == ' ' || c == '&' || c == '|' || c == '(' || c == ')' || c == '\'' || c == '"';
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
            
            // Quote the tag if it contains spaces
            var quotedTag = TagSearchParser.QuoteTagIfNeeded(selectedTag);
            
            if (TagSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray)
            {
                // Replace placeholder text
                TagSearchTextBox.Text = quotedTag;
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
                    // Find start position of current tag (including any opening quote)
                    int start = caretPosition;
                    while (start > 0 && start - 1 < currentText.Length && !IsTagDelimiter(currentText[start - 1]))
                    {
                        start--;
                    }
                    // Include opening quote if present
                    if (start > 0 && (currentText[start - 1] == '\'' || currentText[start - 1] == '"'))
                    {
                        start--;
                    }
                    
                    // Find end position of current tag (including any closing quote)
                    int end = caretPosition;
                    while (end < currentText.Length && !IsTagDelimiter(currentText[end]))
                    {
                        end++;
                    }
                    // Include closing quote if present
                    if (end < currentText.Length && (currentText[end] == '\'' || currentText[end] == '"'))
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
                    
                    // Replace the current tag with the quoted version
                    var newText = currentText.Substring(0, start) + prefix + quotedTag + currentText.Substring(end);
                    TagSearchTextBox.Text = newText;
                    TagSearchTextBox.CaretIndex = start + prefix.Length + quotedTag.Length;
                }
                else
                {
                    // Just append the tag
                    TagSearchTextBox.Text = currentText + " " + quotedTag;
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

    public class DirectoryViewModel
    {
        public string DirectoryPath { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
        public System.Windows.Media.Brush StatusColor { get; set; } = System.Windows.Media.Brushes.Gray;
        public bool HasWarning { get; set; } = false;
        public string WarningMessage { get; set; } = string.Empty;
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
