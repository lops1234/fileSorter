using System;
using System.IO;
using System.Linq;
using System.Windows;
using FileTagger.Data;
using FileTagger.Windows;

namespace FileTagger
{
    public static class CommandLineHandler
    {
        public static bool HandleCommandLineArgs(string[] args)
        {
            if (args.Length < 2) return false;

            var command = args[0];
            var filePath = args[1];

            switch (command)
            {
                case "--manage-tags":
                    return HandleManageTags(filePath);
                
                case "--view-tags":
                    return HandleViewTags(filePath);
                
                case "--filter-directory":
                    return HandleFilterDirectory(filePath);
                
                default:
                    return false;
            }
        }

        private static bool HandleManageTags(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                if (!ShellIntegration.IsInWatchedDirectory(filePath))
                {
                    MessageBox.Show("This file is not in a watched directory. Please add the directory to File Tagger settings first.", 
                        "File Tagger", MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }

                var tagWindow = new TagManagementWindow(filePath);
                tagWindow.ShowDialog();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error managing tags: {ex.Message}", "File Tagger Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }

        private static bool HandleViewTags(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                using var context = new FileTagContext();
                var fileRecord = context.FileRecords
                    .Where(f => f.FilePath == filePath)
                    .Select(f => new { f.FileName, Tags = f.FileTags.Select(ft => ft.Tag.Name).ToList() })
                    .FirstOrDefault();

                if (fileRecord == null || !fileRecord.Tags.Any())
                {
                    MessageBox.Show($"No tags found for:\n{Path.GetFileName(filePath)}", 
                        "File Tags", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var tagsText = string.Join("\n• ", fileRecord.Tags);
                    MessageBox.Show($"Tags for {fileRecord.FileName}:\n\n• {tagsText}", 
                        "File Tags", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error viewing tags: {ex.Message}", "File Tagger Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }

        private static bool HandleFilterDirectory(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath)) return false;

                var filterWindow = new DirectoryFilterWindow(directoryPath);
                filterWindow.ShowDialog();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error filtering directory: {ex.Message}", "File Tagger Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }
    }
}
