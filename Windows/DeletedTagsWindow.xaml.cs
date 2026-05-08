using System;
using System.Collections.Generic;
using System.Windows;
using FileTagger.Services;

namespace FileTagger.Windows
{
    public partial class DeletedTagsWindow : Window
    {
        public DeletedTagsWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => Reload();
        }

        private void Reload()
        {
            var items = DatabaseManager.Instance.GetDeletedTags();
            DeletedTagsListView.ItemsSource = items.ConvertAll(i => new DeletedTagRow(i));
            FilePathsList.ItemsSource = null;
        }

        private DeletedTagRow SelectedRow =>
            DeletedTagsListView.SelectedItem as DeletedTagRow;

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow is not { } row) return;

            var confirm = MessageBox.Show(
                $"Restore tag \"{row.Name}\"?\n\nThis will re-create the tag and restore its file associations.",
                "Restore Tag", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                DatabaseManager.Instance.RestoreDeletedTag(row.Id);
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restore tag: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowFiles_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow is not { } row) return;
            FilePathsList.ItemsSource = row.FilePaths;
            FileDetailExpander.IsExpanded = true;
        }

        private void CleanupAll_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Permanently remove ALL deleted-tag records?\n\n" +
                "This cannot be undone. Tags removed this way cannot be restored and will no longer be propagated to remote databases on the next push.",
                "Clean Up All Deleted Tags", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                DatabaseManager.Instance.CleanupLocalDeletedTags();
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }

    internal sealed class DeletedTagRow
    {
        private readonly DatabaseManager.DeletedTagInfo _info;

        public DeletedTagRow(DatabaseManager.DeletedTagInfo info) => _info = info;

        public int            Id              => _info.Id;
        public string         Name            => _info.Name;
        public string         DirectoryPath   => System.IO.Path.GetFileName(_info.DirectoryPath.TrimEnd('\\', '/'))
                                                 + " …";
        public string         DeletedAtDisplay => _info.DeletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public string         FileSummary     => _info.FilePaths.Count == 0
                                                     ? "(no file associations)"
                                                     : $"{_info.FilePaths.Count} file(s)";
        public List<string>   FilePaths       => _info.FilePaths;
    }
}
