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
        private Dictionary<string, string> _tempToOriginalMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                
                // Only create directory if it doesn't exist, don't clean up automatically
                // Cleanup will be done explicitly when PrepareTempDirectory is called
                if (!Directory.Exists(_tempDirectoryPath))
                {
                    Directory.CreateDirectory(_tempDirectoryPath);
                }
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
                    
                    // Clear the mapping when preparing new temp directory
                    _tempToOriginalMapping.Clear();
                    
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

                // If mapping is empty but we have temp files, rebuild the mapping
                if (_tempToOriginalMapping.Count == 0 && tempFiles.Count > 0 && filePaths.Count > 0)
                {
                    RebuildMapping(tempFiles, filePaths);
                }

                return false;
            }
        }

        /// <summary>
        /// Rebuild the temp-to-original file mapping based on existing temp files and original file paths
        /// </summary>
        private void RebuildMapping(List<string> tempFiles, List<string> originalFilePaths)
        {
            _tempToOriginalMapping.Clear();
            
            // Match temp files to original files based on filename
            foreach (var tempFile in tempFiles)
            {
                var tempFileName = Path.GetFileName(tempFile);
                
                // Find the original file with the same filename
                var matchingOriginal = originalFilePaths.FirstOrDefault(orig => 
                    Path.GetFileName(orig).Equals(tempFileName, StringComparison.OrdinalIgnoreCase));
                
                if (matchingOriginal != null)
                {
                    _tempToOriginalMapping[tempFileName] = matchingOriginal;
                }
                else
                {
                    // Try to find by removing the _1, _2, etc. suffix that gets added for duplicates
                    var extension = Path.GetExtension(tempFileName);
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(tempFileName);
                    
                    // Remove _1, _2, etc. pattern from end
                    var underscoreIndex = nameWithoutExt.LastIndexOf('_');
                    if (underscoreIndex > 0 && int.TryParse(nameWithoutExt.Substring(underscoreIndex + 1), out _))
                    {
                        var baseFileName = nameWithoutExt.Substring(0, underscoreIndex) + extension;
                        
                        var baseMatchingOriginal = originalFilePaths.FirstOrDefault(orig => 
                            Path.GetFileName(orig).Equals(baseFileName, StringComparison.OrdinalIgnoreCase));
                        
                        if (baseMatchingOriginal != null)
                        {
                            _tempToOriginalMapping[tempFileName] = baseMatchingOriginal;
                        }
                    }
                }
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
        /// Get the original file path for a temp file
        /// </summary>
        public string GetOriginalFilePath(string tempFilePath)
        {
            lock (_lock)
            {
                var tempFileName = Path.GetFileName(tempFilePath);
                var result = _tempToOriginalMapping.TryGetValue(tempFileName, out var originalPath) ? originalPath : null;
                
                // If no mapping found, try to rebuild mapping
                if (result == null)
                {
                    var tempFiles = Directory.GetFiles(_tempDirectoryPath, "*", SearchOption.TopDirectoryOnly)
                        .Where(f => !Path.GetFileName(f).Equals("README.txt", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    if (tempFiles.Count > 0)
                    {
                        List<string> originalFiles = null;
                        
                        // Try to use cached file paths first
                        if (_lastFilePaths.Count > 0)
                        {
                            originalFiles = _lastFilePaths;
                        }
                        else
                        {
                            // Fallback: get all files from all watched directories (including untagged files)
                            try
                            {
                                originalFiles = DatabaseManager.Instance.GetAllFilesInWatchedDirectories()
                                    .Select(f => f.FullPath).ToList();
                            }
                            catch (Exception)
                            {
                                originalFiles = new List<string>();
                            }
                        }
                        
                        if (originalFiles.Count > 0)
                        {
                            RebuildMapping(tempFiles, originalFiles);
                            
                            // Try lookup again after rebuild
                            result = _tempToOriginalMapping.TryGetValue(tempFileName, out originalPath) ? originalPath : null;
                        }
                    }
                }
                
                return result;
            }
        }

        /// <summary>
        /// Check if a file path is in the temp directory
        /// </summary>
        public bool IsInTempDirectory(string filePath)
        {
            try
            {
                var fileDir = Path.GetDirectoryName(filePath);
                return string.Equals(fileDir, _tempDirectoryPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
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
                            
                            // Record the mapping from temp filename to original path
                            var tempFileName = Path.GetFileName(destPath);
                            _tempToOriginalMapping[tempFileName] = sourceFile;
                            
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
                             $"This temporary directory will be cleaned up when File Tagger closes.\n\n" +
                             $"⭐ TAG MANAGEMENT FEATURE ⭐\n" +
                             $"You can right-click on any file here to add or manage tags!\n" +
                             $"Tags will be applied to the original files in their actual locations.\n" +
                             $"Use 'Manage Tags' or 'View File Tags' from the context menu.";

                File.WriteAllText(readmePath, content);
            }
            catch (Exception)
            {
                // Ignore README creation errors - not critical
            }
        }
    }
}
