using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using FileTagger.Services;
using FileTagger.Windows;

namespace FileTagger
{
    public static class CommandLineHandler
    {
        private static readonly string TempFileBasePath = Path.Combine(Path.GetTempPath(), "FileTagger_Selection");
        private static readonly int CollectionWindowMs = 500; // Wait 500ms to collect all file selections

        public static bool HandleCommandLineArgs(string[] args)
        {
            if (args.Length < 2) return false;

            var command = args[0];
            
            switch (command)
            {
                case "--manage-tags":
                    // Collect all file paths from remaining arguments
                    var filePaths = new List<string>();
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (File.Exists(args[i]))
                        {
                            filePaths.Add(args[i]);
                        }
                    }
                    
                    if (filePaths.Count > 0)
                    {
                        return HandleManageTags(filePaths);
                    }
                    return false;
                
                case "--view-tags":
                    return HandleViewTags(args[1]);
                
                case "--filter-directory":
                    return HandleFilterDirectory(args[1]);
                
                default:
                    return false;
            }
        }

        private static readonly object _fileLock = new object();

        private static bool HandleManageTags(List<string> filePaths)
        {
            try
            {
                if (filePaths == null || filePaths.Count == 0) return false;

                // Use a named mutex to coordinate between multiple instances
                bool isFirstInstance = false;
                Mutex collectionMutex = null;
                
                try
                {
                    collectionMutex = new Mutex(true, "FileTagger_ManageTags_Collection", out isFirstInstance);
                    
                    // Create/append to temporary file with selected file paths
                    var tempFilePath = $"{TempFileBasePath}_{DateTime.Now:yyyyMMdd}.txt";
                    
                    // Write our files to the temp file
                    WriteFilePaths(tempFilePath, filePaths);
                    
                    if (isFirstInstance)
                    {
                        // First instance - wait to collect all files from other instances
                        Thread.Sleep(CollectionWindowMs);
                        
                        // Read all collected file paths
                        var allFilePaths = ReadFilePaths(tempFilePath, filePaths);
                        
                        // Process all collected files
                        var processedPaths = new List<string>();
                        foreach (var path in allFilePaths)
                        {
                            // Check if this is a temp file - if so, get the original file path
                            string originalFilePath = path;
                            if (TempResultsManager.Instance.IsInTempDirectory(path))
                            {
                                var mappedPath = TempResultsManager.Instance.GetOriginalFilePath(path);
                                if (!string.IsNullOrEmpty(mappedPath))
                                {
                                    originalFilePath = mappedPath;
                                }
                                else
                                {
                                    continue; // Skip this file
                                }
                            }
                            else if (!ShellIntegration.IsInWatchedDirectory(path))
                            {
                                // Skip files not in watched directories
                                continue;
                            }
                            
                            processedPaths.Add(originalFilePath);
                        }
                        
                        if (processedPaths.Count == 0)
                        {
                            MessageBox.Show("Selected files are not in a watched directory. Please add the directory to File Tagger settings first.", 
                                "File Tagger", MessageBoxButton.OK, MessageBoxImage.Information);
                            return true;
                        }
                        
                        // Open tag management window with all files
                        var tagWindow = new TagManagementWindow(processedPaths);
                        tagWindow.ShowDialog();
                        return true;
                    }
                    else
                    {
                        // Not the first instance - just add files and exit
                        // The first instance will handle opening the window
                        return true;
                    }
                }
                finally
                {
                    if (isFirstInstance && collectionMutex != null)
                    {
                        collectionMutex.ReleaseMutex();
                    }
                    collectionMutex?.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error managing tags: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", 
                    "File Tagger Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }

        private static void WriteFilePaths(string tempFilePath, List<string> filePaths)
        {
            lock (_fileLock)
            {
                try
                {
                    File.AppendAllLines(tempFilePath, filePaths);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error writing file paths: {ex.Message}");
                }
            }
        }

        private static List<string> ReadFilePaths(string tempFilePath, List<string> fallbackPaths)
        {
            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        var paths = File.ReadAllLines(tempFilePath)
                            .Where(f => File.Exists(f))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        
                        // Clean up temp file
                        try { File.Delete(tempFilePath); } catch { }
                        
                        return paths;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading file paths: {ex.Message}");
                }
                
                return fallbackPaths;
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
