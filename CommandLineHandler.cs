using System;
using System.IO;
using System.Linq;
using System.Windows;
using FileTagger.Services;
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
                
                // Check if this is a temp file - if so, get the original file path
                string originalFilePath = filePath;
                if (TempResultsManager.Instance.IsInTempDirectory(filePath))
                {
                    var mappedPath = TempResultsManager.Instance.GetOriginalFilePath(filePath);
                    if (!string.IsNullOrEmpty(mappedPath))
                    {
                        originalFilePath = mappedPath;
                    }
                    else
                    {
                        MessageBox.Show("Cannot find original file location for this temporary file.", 
                            "File Tagger Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return true;
                    }
                }
                else if (!ShellIntegration.IsInWatchedDirectory(filePath))
                {
                    MessageBox.Show("This file is not in a watched directory. Please add the directory to File Tagger settings first.", 
                        "File Tagger", MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }

                var tagWindow = new TagManagementWindow(originalFilePath);
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

                // Check if this is a temp file - if so, get the original file path
                string originalFilePath = filePath;
                string displayNote = "";
                if (TempResultsManager.Instance.IsInTempDirectory(filePath))
                {
                    var mappedPath = TempResultsManager.Instance.GetOriginalFilePath(filePath);
                    if (!string.IsNullOrEmpty(mappedPath))
                    {
                        originalFilePath = mappedPath;
                        displayNote = $"\n\n(Viewing tags for original file: {Path.GetFileName(originalFilePath)})";
                    }
                    else
                    {
                        MessageBox.Show("Cannot find original file location for this temporary file.", 
                            "File Tagger Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return true;
                    }
                }

                var allFiles = DatabaseManager.Instance.GetAllFilesWithTags();
                var fileRecord = allFiles.FirstOrDefault(f => f.FullPath == originalFilePath);

                if (fileRecord == null || !fileRecord.Tags.Any())
                {
                    MessageBox.Show($"No tags found for:\n{Path.GetFileName(originalFilePath)}{displayNote}", 
                        "File Tags", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var tagsText = string.Join("\n• ", fileRecord.Tags);
                    MessageBox.Show($"Tags for {fileRecord.FileName}:\n\n• {tagsText}{displayNote}", 
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
