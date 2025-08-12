using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FileTagger.Services
{
    /// <summary>
    /// Manages temporary directory for search results
    /// </summary>
    public class TempResultsManager
    {
        private static TempResultsManager _instance;
        public static TempResultsManager Instance => _instance ??= new TempResultsManager();

        private string _tempDirectoryPath;
        private readonly object _lock = new object();
        private CancellationTokenSource _currentCopyOperation;
        private string _lastSearchQuery = "";
        private List<string> _lastFilePaths = new List<string>();
        private DateTime _lastCopyTime = DateTime.MinValue;

        private TempResultsManager()
        {
            InitializeTempDirectory();
        }

        private void InitializeTempDirectory()
        {
            try
            {
                // Create temp directory in user's temp folder
                var baseTempPath = Path.GetTempPath();
                _tempDirectoryPath = Path.Combine(baseTempPath, "FileTagger_SearchResults");
                
                // Clean up any existing temp directory
                CleanupTempDirectory();
                
                // Create fresh temp directory
                Directory.CreateDirectory(_tempDirectoryPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize temporary directory: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Prepare temp directory and return path (for immediate Explorer opening)
        /// </summary>
        public string PrepareTempDirectory()
        {
            lock (_lock)
            {
                try
                {
                    // Clean up previous results
                    CleanupTempDirectory();
                    Directory.CreateDirectory(_tempDirectoryPath);
                    return _tempDirectoryPath;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error preparing temporary directory: {ex.Message}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return _tempDirectoryPath;
                }
            }
        }

        /// <summary>
        /// Cancel any ongoing copy operation
        /// </summary>
        public void CancelCurrentCopyOperation()
        {
            lock (_lock)
            {
                _currentCopyOperation?.Cancel();
            }
        }

        /// <summary>
        /// Check if the search parameters have changed since last copy operation
        /// </summary>
        public bool HasSearchParametersChanged(string searchQuery, List<string> filePaths)
        {
            lock (_lock)
            {
                // Check if query changed
                if (_lastSearchQuery != (searchQuery ?? ""))
                    return true;

                // Check if file list changed (compare count and content)
                if (_lastFilePaths.Count != filePaths.Count)
                    return true;

                // Compare file paths (order-sensitive)
                for (int i = 0; i < filePaths.Count; i++)
                {
                    if (i >= _lastFilePaths.Count || _lastFilePaths[i] != filePaths[i])
                        return true;
                }

                // Check if temp directory exists and has files
                if (!Directory.Exists(_tempDirectoryPath))
                    return true;

                var tempFiles = Directory.GetFiles(_tempDirectoryPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).Equals("README.txt", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // If no files in temp directory (besides README), we need to copy
                if (tempFiles.Count == 0 && filePaths.Count > 0)
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Update the last search parameters
        /// </summary>
        private void UpdateLastSearchParameters(string searchQuery, List<string> filePaths)
        {
            lock (_lock)
            {
                _lastSearchQuery = searchQuery ?? "";
                _lastFilePaths = new List<string>(filePaths);
                _lastCopyTime = DateTime.Now;
            }
        }

        /// <summary>
        /// Copy search result files to temp directory asynchronously with cancellation support
        /// </summary>
        public async Task<(int copiedCount, List<string> errors, bool wasCancelled)> CopySearchResultsAsync(List<string> filePaths, string searchQuery = "", Action<int, int> progressCallback = null)
        {
            return await Task.Run(() =>
            {
                CancellationTokenSource cts;
                lock (_lock)
                {
                    // Cancel any existing operation
                    _currentCopyOperation?.Cancel();
                    
                    // Create new cancellation token for this operation
                    _currentCopyOperation = new CancellationTokenSource();
                    cts = _currentCopyOperation;
                }

                var copiedCount = 0;
                var errors = new List<string>();
                var wasCancelled = false;

                if (filePaths == null || !filePaths.Any())
                {
                    return (copiedCount, errors, wasCancelled);
                }

                try
                {
                    for (int i = 0; i < filePaths.Count; i++)
                    {
                        // Check for cancellation before each file
                        if (cts.Token.IsCancellationRequested)
                        {
                            wasCancelled = true;
                            break;
                        }

                        var sourceFile = filePaths[i];
                        try
                        {
                            if (!File.Exists(sourceFile))
                            {
                                errors.Add($"File not found: {sourceFile}");
                                continue;
                            }

                            var fileName = Path.GetFileName(sourceFile);
                            var destPath = Path.Combine(_tempDirectoryPath, fileName);

                            // Handle duplicate filenames by appending a number
                            var counter = 1;
                            var originalDestPath = destPath;
                            while (File.Exists(destPath))
                            {
                                var nameWithoutExt = Path.GetFileNameWithoutExtension(originalDestPath);
                                var extension = Path.GetExtension(originalDestPath);
                                destPath = Path.Combine(_tempDirectoryPath, $"{nameWithoutExt}_{counter}{extension}");
                                counter++;
                            }

                            // Check for cancellation before copying
                            if (cts.Token.IsCancellationRequested)
                            {
                                wasCancelled = true;
                                break;
                            }

                            File.Copy(sourceFile, destPath, true);
                            copiedCount++;
                            
                            // Report progress
                            progressCallback?.Invoke(copiedCount, filePaths.Count);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Failed to copy {Path.GetFileName(sourceFile)}: {ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    wasCancelled = true;
                }
                finally
                {
                    lock (_lock)
                    {
                        // Clear the current operation if it's still this one
                        if (_currentCopyOperation == cts)
                        {
                            _currentCopyOperation = null;
                        }
                    }
                }

                // Update last search parameters if copy completed successfully (not cancelled)
                if (!wasCancelled)
                {
                    UpdateLastSearchParameters(searchQuery, filePaths);
                }

                return (copiedCount, errors, wasCancelled);
            });
        }

        /// <summary>
        /// Get the current temp directory path
        /// </summary>
        public string GetTempDirectoryPath()
        {
            return _tempDirectoryPath;
        }

        /// <summary>
        /// Clean up the temporary directory
        /// </summary>
        public void CleanupTempDirectory()
        {
            lock (_lock)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_tempDirectoryPath) && Directory.Exists(_tempDirectoryPath))
                    {
                        Directory.Delete(_tempDirectoryPath, true);
                    }
                }
                catch (Exception)
                {
                    // Ignore cleanup errors - they're not critical
                    // Files might be locked or in use
                }
            }
        }

        /// <summary>
        /// Create a README file in the temp directory explaining what it contains
        /// </summary>
        public void CreateReadmeFile(string searchQuery, int fileCount)
        {
            try
            {
                var readmePath = Path.Combine(_tempDirectoryPath, "README.txt");
                var content = $"File Tagger - Search Results\n" +
                             $"===========================\n\n" +
                             $"Search Query: {searchQuery}\n" +
                             $"Files Found: {fileCount}\n" +
                             $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                             $"These are copies of files that matched your search criteria.\n" +
                             $"Original files remain in their original locations.\n" +
                             $"This temporary directory will be cleaned up when File Tagger closes.";

                File.WriteAllText(readmePath, content);
            }
            catch (Exception)
            {
                // Ignore README creation errors - not critical
            }
        }
    }
}
