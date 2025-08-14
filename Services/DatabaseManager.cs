using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileTagger.Data;
using Microsoft.EntityFrameworkCore;

namespace FileTagger.Services
{
    /// <summary>
    /// Manages distributed database operations across main DB and directory-specific DBs
    /// </summary>
    public class DatabaseManager
    {
        private static DatabaseManager _instance;
        private static readonly object _lock = new object();

        public static DatabaseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new DatabaseManager();
                    }
                }
                return _instance;
            }
        }

        private DatabaseManager() { }

        /// <summary>
        /// Initialize all databases and perform initial synchronization
        /// </summary>
        public void Initialize()
        {
            // Ensure main database exists
            using (var mainDb = new MainDbContext())
            {
                mainDb.Database.EnsureCreated();
            }

            // Discover and add existing .filetagger directories
            DiscoverExistingFileTaggerDirectories();

            // Sync tags from all directory databases
            SynchronizeAllTags();
        }

        /// <summary>
        /// Discover existing .filetagger directories and add them to watched directories if not already present
        /// </summary>
        private void DiscoverExistingFileTaggerDirectories()
        {
            try
            {
                using var mainDb = new MainDbContext();
                var existingWatchedDirs = mainDb.WatchedDirectories
                    .Select(d => d.DirectoryPath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Search common drive roots for .filetagger directories
                var searchRoots = GetSearchRoots();
                var discoveredDirectories = new List<string>();

                foreach (var searchRoot in searchRoots)
                {
                    try
                    {
                        if (!Directory.Exists(searchRoot))
                            continue;

                        // Search for .filetagger directories recursively
                        var fileTaggerDirs = Directory.GetDirectories(searchRoot, ".filetagger", SearchOption.AllDirectories);
                        
                        foreach (var fileTaggerDir in fileTaggerDirs)
                        {
                            // Get the parent directory (the actual directory we want to watch)
                            var parentDir = Path.GetDirectoryName(fileTaggerDir);
                            if (string.IsNullOrEmpty(parentDir) || existingWatchedDirs.Contains(parentDir))
                                continue;

                            // Check if there's a valid database file
                            var dbFile = Path.Combine(fileTaggerDir, "tags.db");
                            if (File.Exists(dbFile))
                            {
                                discoveredDirectories.Add(parentDir);
                            }
                        }
                    }
                    catch
                    {
                        // Skip search roots that can't be accessed
                        continue;
                    }
                }

                // Add discovered directories to watched list
                foreach (var directoryPath in discoveredDirectories)
                {
                    try
                    {
                        var watchedDir = new WatchedDirectory
                        {
                            DirectoryPath = directoryPath,
                            IsActive = true,
                            LastSyncAt = DateTime.UtcNow
                        };
                        mainDb.WatchedDirectories.Add(watchedDir);
                    }
                    catch
                    {
                        // Skip directories that can't be added
                        continue;
                    }
                }

                if (discoveredDirectories.Any())
                {
                    mainDb.SaveChanges();
                }
            }
            catch
            {
                // If discovery fails, continue with normal initialization
            }
        }

        /// <summary>
        /// Get search roots for discovering existing .filetagger directories
        /// </summary>
        private List<string> GetSearchRoots()
        {
            var searchRoots = new List<string>();

            try
            {
                // Add user profile directories
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    searchRoots.Add(userProfile);
                    
                    // Add common subdirectories
                    var commonDirs = new[] { "Documents", "Downloads", "Desktop", "Pictures", "Videos" };
                    foreach (var dir in commonDirs)
                    {
                        var fullPath = Path.Combine(userProfile, dir);
                        if (Directory.Exists(fullPath))
                            searchRoots.Add(fullPath);
                    }
                }

                // Add all logical drives (but limit search depth for performance)
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                    .Select(d => d.RootDirectory.FullName);
                
                searchRoots.AddRange(drives);
            }
            catch
            {
                // If we can't get search roots, just use current directory
                searchRoots.Add(Environment.CurrentDirectory);
            }

            return searchRoots;
        }

        /// <summary>
        /// Get or create a directory database context for the specified path
        /// WARNING: This method should only be used with explicitly watched directories
        /// For files in subdirectories, use GetDirectoryDbForFile instead
        /// </summary>
        public DirectoryDbContext GetDirectoryDb(string directoryPath)
        {
            // Optional safety check - uncomment to enforce watched directory requirement
            // var watchedDirs = GetAllActiveDirectories();
            // if (!watchedDirs.Contains(directoryPath, StringComparer.OrdinalIgnoreCase))
            // {
            //     throw new InvalidOperationException($"Directory '{directoryPath}' is not a watched directory. Use GetDirectoryDbForFile for files in subdirectories.");
            // }
            
            var dirDb = new DirectoryDbContext(directoryPath);
            dirDb.Database.EnsureCreated();
            return dirDb;
        }

        /// <summary>
        /// Get the appropriate watched directory for a given file path
        /// Returns the most specific (deepest) watched directory that contains the file
        /// </summary>
        public string GetWatchedDirectoryForFile(string filePath)
        {
            var applicableDirectories = GetApplicableDirectoriesForFile(filePath);
            return applicableDirectories.FirstOrDefault(); // Most specific first due to OrderByDescending
        }

        /// <summary>
        /// Get the directory database context for a file, using the appropriate watched directory
        /// This ensures subdirectory files use their parent watched directory's database
        /// </summary>
        public DirectoryDbContext GetDirectoryDbForFile(string filePath)
        {
            var watchedDirectory = GetWatchedDirectoryForFile(filePath);
            if (string.IsNullOrEmpty(watchedDirectory))
            {
                throw new InvalidOperationException($"File '{filePath}' is not in any watched directory. Please add the directory to File Tagger settings first.");
            }
            
            return GetDirectoryDb(watchedDirectory);
        }

        /// <summary>
        /// Manually discover and import existing .filetagger directories from a specific root path
        /// </summary>
        public List<string> DiscoverAndImportExistingDatabases(string rootPath = null)
        {
            var discoveredDirectories = new List<string>();
            
            try
            {
                using var mainDb = new MainDbContext();
                var existingWatchedDirs = mainDb.WatchedDirectories
                    .Select(d => d.DirectoryPath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var searchRoots = string.IsNullOrEmpty(rootPath) ? GetSearchRoots() : new List<string> { rootPath };

                foreach (var searchRoot in searchRoots)
                {
                    try
                    {
                        if (!Directory.Exists(searchRoot))
                            continue;

                        // Search for .filetagger directories recursively
                        var fileTaggerDirs = Directory.GetDirectories(searchRoot, ".filetagger", SearchOption.AllDirectories);
                        
                        foreach (var fileTaggerDir in fileTaggerDirs)
                        {
                            // Get the parent directory (the actual directory we want to watch)
                            var parentDir = Path.GetDirectoryName(fileTaggerDir);
                            if (string.IsNullOrEmpty(parentDir) || existingWatchedDirs.Contains(parentDir))
                                continue;

                            // Check if there's a valid database file
                            var dbFile = Path.Combine(fileTaggerDir, "tags.db");
                            if (File.Exists(dbFile))
                            {
                                // Try to add the directory
                                try
                                {
                                    var watchedDir = new WatchedDirectory
                                    {
                                        DirectoryPath = parentDir,
                                        IsActive = true,
                                        LastSyncAt = DateTime.UtcNow
                                    };
                                    mainDb.WatchedDirectories.Add(watchedDir);
                                    discoveredDirectories.Add(parentDir);
                                    existingWatchedDirs.Add(parentDir); // Update our cache to avoid duplicates
                                }
                                catch
                                {
                                    // Skip directories that can't be added (e.g., database constraint violations)
                                    continue;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip search roots that can't be accessed
                        continue;
                    }
                }

                if (discoveredDirectories.Any())
                {
                    mainDb.SaveChanges();
                    // Sync tags from newly discovered directories
                    foreach (var dir in discoveredDirectories)
                    {
                        try
                        {
                            SynchronizeDirectoryTags(dir);
                        }
                        catch
                        {
                            // Continue with other directories if one fails
                        }
                    }
                }
            }
            catch
            {
                // If discovery fails, return what we found so far
            }

            return discoveredDirectories;
        }

        /// <summary>
        /// Add a new watched directory and create its database
        /// </summary>
        public void AddWatchedDirectory(string directoryPath)
        {
            using var mainDb = new MainDbContext();
            
            var existingDir = mainDb.WatchedDirectories.FirstOrDefault(d => d.DirectoryPath == directoryPath);
            if (existingDir != null)
            {
                existingDir.IsActive = true;
                existingDir.LastSyncAt = DateTime.UtcNow;
            }
            else
            {
                mainDb.WatchedDirectories.Add(new WatchedDirectory
                {
                    DirectoryPath = directoryPath,
                    IsActive = true
                });
            }
            
            mainDb.SaveChanges();

            // Create/ensure directory database exists
            using var dirDb = GetDirectoryDb(directoryPath);
            
            // Sync tags from this directory
            SynchronizeDirectoryTags(directoryPath);
        }

        /// <summary>
        /// Remove a watched directory and clean up its tags
        /// </summary>
        public void RemoveWatchedDirectory(string directoryPath)
        {
            using var mainDb = new MainDbContext();
            
            var directory = mainDb.WatchedDirectories.FirstOrDefault(d => d.DirectoryPath == directoryPath);
            if (directory != null)
            {
                directory.IsActive = false;
                
                // Remove aggregated tags that are only from this directory
                var tagsToRemove = mainDb.AggregatedTags
                    .Where(at => at.SourceDirectoryId == directory.Id)
                    .ToList();
                
                foreach (var tag in tagsToRemove)
                {
                    // Check if this tag exists in other active directories
                    var existsElsewhere = GetAllActiveDirectories()
                        .Where(d => d != directoryPath)
                        .Any(d => DirectoryHasTag(d, tag.Name));
                    
                    if (!existsElsewhere)
                    {
                        mainDb.AggregatedTags.Remove(tag);
                    }
                }
                
                mainDb.SaveChanges();
            }
        }

        /// <summary>
        /// Synchronize tags from all directory databases to main database
        /// </summary>
        public void SynchronizeAllTags()
        {
            var activeDirectories = GetAllActiveDirectories();
            
            foreach (var directoryPath in activeDirectories)
            {
                SynchronizeDirectoryTags(directoryPath);
            }
        }

        /// <summary>
        /// Synchronize tags from a specific directory database to main database
        /// </summary>
        public void SynchronizeDirectoryTags(string directoryPath)
        {
            using var mainDb = new MainDbContext();
            
            var watchedDir = mainDb.WatchedDirectories.FirstOrDefault(d => d.DirectoryPath == directoryPath && d.IsActive);
            if (watchedDir == null) return;

            using var dirDb = GetDirectoryDb(directoryPath);
            var localTags = dirDb.LocalTags.Include(t => t.LocalFileTags).ToList();

            foreach (var localTag in localTags)
            {
                var aggregatedTag = mainDb.AggregatedTags.FirstOrDefault(at => 
                    at.Name == localTag.Name && at.SourceDirectoryId == watchedDir.Id);

                if (aggregatedTag == null)
                {
                    // Create new aggregated tag
                    aggregatedTag = new AggregatedTag
                    {
                        Name = localTag.Name,
                        Description = localTag.Description,
                        SourceDirectoryId = watchedDir.Id,
                        CreatedAt = localTag.CreatedAt,
                        LastSeenAt = DateTime.UtcNow,
                        UsageCount = localTag.LocalFileTags.Count
                    };
                    mainDb.AggregatedTags.Add(aggregatedTag);
                }
                else
                {
                    // Update existing aggregated tag
                    aggregatedTag.Description = localTag.Description;
                    aggregatedTag.LastSeenAt = DateTime.UtcNow;
                    aggregatedTag.UsageCount = localTag.LocalFileTags.Count;
                }
            }

            watchedDir.LastSyncAt = DateTime.UtcNow;
            mainDb.SaveChanges();
        }

        /// <summary>
        /// Get all available tags from all active directories
        /// </summary>
        public List<TagInfo> GetAllAvailableTags()
        {
            using var mainDb = new MainDbContext();
            
            return mainDb.AggregatedTags
                .Include(at => at.SourceDirectory)
                .Where(at => at.SourceDirectory.IsActive)
                .GroupBy(at => at.Name)
                .Select(g => new TagInfo
                {
                    Name = g.Key,
                    Description = g.First().Description,
                    TotalUsageCount = g.Sum(at => at.UsageCount),
                    SourceDirectories = g.Select(at => at.SourceDirectory.DirectoryPath).ToList()
                })
                .OrderBy(t => t.Name)
                .ToList();
        }

        /// <summary>
        /// Get all active watched directories
        /// </summary>
        public List<string> GetAllActiveDirectories()
        {
            using var mainDb = new MainDbContext();
            return mainDb.WatchedDirectories
                .Where(d => d.IsActive)
                .Select(d => d.DirectoryPath)
                .ToList();
        }

        /// <summary>
        /// Get all watched directories that should contain tags for a given file path
        /// Returns directories in order from most specific (deepest) to least specific (shallowest)
        /// </summary>
        public List<string> GetApplicableDirectoriesForFile(string filePath)
        {
            var watchedDirs = GetAllActiveDirectories();
            var fileDir = Path.GetDirectoryName(filePath);
            
            if (string.IsNullOrEmpty(fileDir))
                return new List<string>();

            // Find all watched directories that contain this file
            var applicableDirs = watchedDirs
                .Where(wd => fileDir.StartsWith(wd, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(wd => wd.Length) // Most specific first (deepest directory)
                .ToList();

            return applicableDirs;
        }

        /// <summary>
        /// Check if a directory has a specific tag
        /// </summary>
        private bool DirectoryHasTag(string directoryPath, string tagName)
        {
            try
            {
                using var dirDb = GetDirectoryDb(directoryPath);
                return dirDb.LocalTags.Any(t => t.Name == tagName);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create a standalone tag in a specific directory (without attaching to a file)
        /// </summary>
        public void CreateStandaloneTag(string directoryPath, string tagName, string tagDescription = "")
        {
            if (string.IsNullOrEmpty(directoryPath) || string.IsNullOrEmpty(tagName))
                return;

            using var dirDb = GetDirectoryDb(directoryPath);

            // Check if tag already exists
            var existingTag = dirDb.LocalTags.FirstOrDefault(t => t.Name == tagName);
            if (existingTag != null)
            {
                // Update description if provided
                if (!string.IsNullOrEmpty(tagDescription))
                {
                    existingTag.Description = tagDescription;
                    existingTag.LastUsedAt = DateTime.UtcNow;
                    dirDb.SaveChanges();
                }
                return;
            }

            // Create new tag
            var tag = new LocalTag
            {
                Name = tagName,
                Description = tagDescription,
                LastUsedAt = DateTime.UtcNow
            };
            dirDb.LocalTags.Add(tag);
            dirDb.SaveChanges();

            // Sync this directory's tags to main database
            SynchronizeDirectoryTags(directoryPath);
        }

        /// <summary>
        /// Create a standalone tag in all active directories
        /// </summary>
        public void CreateStandaloneTagInAllDirectories(string tagName, string tagDescription = "")
        {
            var activeDirectories = GetAllActiveDirectories();
            
            foreach (var directoryPath in activeDirectories)
            {
                try
                {
                    CreateStandaloneTag(directoryPath, tagName, tagDescription);
                }
                catch
                {
                    // Skip directories with issues
                    continue;
                }
            }
        }

        /// <summary>
        /// Add a tag to a file in a specific directory
        /// </summary>
        public void AddTagToFile(string filePath, string tagName, string tagDescription = "")
        {
            // Get all directories that should contain this tag
            var applicableDirectories = GetApplicableDirectoriesForFile(filePath);
            
            if (!applicableDirectories.Any())
                return;

            var fileName = Path.GetFileName(filePath);

            // Add tag to each applicable directory's database
            foreach (var directoryPath in applicableDirectories)
            {
                var relativePath = Path.GetRelativePath(directoryPath, filePath);

                using var dirDb = GetDirectoryDb(directoryPath);

                // Get or create file record
                var fileRecord = dirDb.LocalFileRecords.FirstOrDefault(f => f.RelativePath == relativePath);
                if (fileRecord == null)
                {
                    var fileInfo = new FileInfo(filePath);
                    fileRecord = new LocalFileRecord
                    {
                        FileName = fileName,
                        RelativePath = relativePath,
                        LastModified = fileInfo.LastWriteTime,
                        FileSize = fileInfo.Length
                    };
                    dirDb.LocalFileRecords.Add(fileRecord);
                    dirDb.SaveChanges();
                }

                // Get or create tag
                var tag = dirDb.LocalTags.FirstOrDefault(t => t.Name == tagName);
                if (tag == null)
                {
                    tag = new LocalTag
                    {
                        Name = tagName,
                        Description = tagDescription,
                        LastUsedAt = DateTime.UtcNow
                    };
                    dirDb.LocalTags.Add(tag);
                    dirDb.SaveChanges();
                }
                else
                {
                    tag.LastUsedAt = DateTime.UtcNow;
                }

                // Create file-tag association if it doesn't exist
                var existingAssociation = dirDb.LocalFileTags
                    .FirstOrDefault(lft => lft.LocalFileRecordId == fileRecord.Id && lft.LocalTagId == tag.Id);

                if (existingAssociation == null)
                {
                    dirDb.LocalFileTags.Add(new LocalFileTag
                    {
                        LocalFileRecordId = fileRecord.Id,
                        LocalTagId = tag.Id
                    });
                    dirDb.SaveChanges();
                }

                // Sync this directory's tags to main database
                SynchronizeDirectoryTags(directoryPath);
            }
        }

        /// <summary>
        /// Get all files with tags across all directories
        /// Consolidates tags from multiple directories for the same file
        /// </summary>
        public List<FileWithTags> GetAllFilesWithTags()
        {
            var fileMap = new Dictionary<string, FileWithTags>(StringComparer.OrdinalIgnoreCase);
            var activeDirectories = GetAllActiveDirectories();

            foreach (var directoryPath in activeDirectories)
            {
                try
                {
                    using var dirDb = GetDirectoryDb(directoryPath);
                    var files = dirDb.LocalFileRecords
                        .Include(f => f.LocalFileTags)
                        .ThenInclude(lft => lft.LocalTag)
                        .ToList();

                    foreach (var file in files)
                    {
                        var fullPath = Path.Combine(directoryPath, file.RelativePath);
                        
                        // Check if file already exists in our map
                        if (fileMap.TryGetValue(fullPath, out var existingFile))
                        {
                            // Merge tags from this directory with existing tags
                            var newTags = file.LocalFileTags.Select(lft => lft.LocalTag.Name).ToList();
                            existingFile.Tags = existingFile.Tags.Union(newTags).Distinct().ToList();
                            
                            // Update last modified if this one is newer
                            if (file.LastModified > existingFile.LastModified)
                            {
                                existingFile.LastModified = file.LastModified;
                            }
                        }
                        else
                        {
                            // Add new file
                            fileMap[fullPath] = new FileWithTags
                            {
                                FileName = file.FileName,
                                FullPath = fullPath,
                                DirectoryPath = GetMostSpecificDirectory(fullPath, activeDirectories),
                                LastModified = file.LastModified,
                                FileSize = file.FileSize,
                                Tags = file.LocalFileTags.Select(lft => lft.LocalTag.Name).ToList()
                            };
                        }
                    }
                }
                catch
                {
                    // Skip directories with database issues
                    continue;
                }
            }

            return fileMap.Values.OrderBy(f => f.FileName).ToList();
        }

        /// <summary>
        /// Get the most specific (deepest) directory that contains a file
        /// </summary>
        private string GetMostSpecificDirectory(string filePath, List<string> directories)
        {
            var fileDir = Path.GetDirectoryName(filePath) ?? "";
            return directories
                .Where(d => fileDir.StartsWith(d, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.Length)
                .FirstOrDefault() ?? fileDir;
        }

        /// <summary>
        /// Get information about which directories contain tags for a specific file
        /// Useful for debugging and understanding the hierarchical system
        /// </summary>
        public List<string> GetDirectoriesContainingFile(string filePath)
        {
            var applicableDirectories = GetApplicableDirectoriesForFile(filePath);
            var directoriesWithFile = new List<string>();

            foreach (var directoryPath in applicableDirectories)
            {
                try
                {
                    var relativePath = Path.GetRelativePath(directoryPath, filePath);
                    using var dirDb = GetDirectoryDb(directoryPath);
                    
                    var fileExists = dirDb.LocalFileRecords
                        .Any(f => f.RelativePath == relativePath);
                    
                    if (fileExists)
                    {
                        directoriesWithFile.Add(directoryPath);
                    }
                }
                catch
                {
                    // Skip directories with issues
                    continue;
                }
            }

            return directoriesWithFile;
        }

        /// <summary>
        /// Get all files in watched directories that have no tags
        /// </summary>
        public List<FileWithTags> GetUntaggedFiles()
        {
            var fileMap = new Dictionary<string, FileWithTags>(StringComparer.OrdinalIgnoreCase);
            var activeDirectories = GetAllActiveDirectories();

            foreach (var directoryPath in activeDirectories)
            {
                try
                {
                    // Get all files from the file system in this directory
                    var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                    
                    using var dirDb = GetDirectoryDb(directoryPath);
                    var taggedFiles = dirDb.LocalFileRecords
                        .Include(f => f.LocalFileTags)
                        .Where(f => f.LocalFileTags.Any())
                        .Select(f => Path.Combine(directoryPath, f.RelativePath))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var filePath in allFiles)
                    {
                        // Skip if this file is already tagged in any database
                        if (taggedFiles.Contains(filePath))
                            continue;
                        
                        // Skip if we already processed this file from another directory
                        if (fileMap.ContainsKey(filePath))
                            continue;

                        try
                        {
                            var fileInfo = new FileInfo(filePath);
                            fileMap[filePath] = new FileWithTags
                            {
                                FileName = fileInfo.Name,
                                FullPath = filePath,
                                DirectoryPath = GetMostSpecificDirectory(filePath, activeDirectories),
                                LastModified = fileInfo.LastWriteTime,
                                FileSize = fileInfo.Length,
                                Tags = new List<string>() // No tags
                            };
                        }
                        catch
                        {
                            // Skip files that can't be accessed
                            continue;
                        }
                    }
                }
                catch
                {
                    // Skip directories with issues
                    continue;
                }
            }

            return fileMap.Values.OrderBy(f => f.FileName).ToList();
        }

        /// <summary>
        /// Get all files in watched directories (both tagged and untagged)
        /// </summary>
        public List<FileWithTags> GetAllFilesInWatchedDirectories()
        {
            var fileMap = new Dictionary<string, FileWithTags>(StringComparer.OrdinalIgnoreCase);
            var activeDirectories = GetAllActiveDirectories();

            foreach (var directoryPath in activeDirectories)
            {
                try
                {
                    // Get all files from the file system in this directory
                    var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                    
                    using var dirDb = GetDirectoryDb(directoryPath);
                    var taggedFilesMap = dirDb.LocalFileRecords
                        .Include(f => f.LocalFileTags)
                        .ThenInclude(lft => lft.LocalTag)
                        .ToDictionary(f => Path.Combine(directoryPath, f.RelativePath), 
                                    f => f, 
                                    StringComparer.OrdinalIgnoreCase);

                    foreach (var filePath in allFiles)
                    {
                        // Check if we already processed this file from another directory
                        if (fileMap.TryGetValue(filePath, out var existingFile))
                        {
                            // Merge tags if this file is also tagged in this directory
                            if (taggedFilesMap.TryGetValue(filePath, out var taggedFile))
                            {
                                var newTags = taggedFile.LocalFileTags.Select(lft => lft.LocalTag.Name).ToList();
                                existingFile.Tags = existingFile.Tags.Union(newTags).Distinct().ToList();
                            }
                            continue;
                        }

                        try
                        {
                            var fileInfo = new FileInfo(filePath);
                            var tags = new List<string>();
                            
                            // Get tags if file is in database
                            if (taggedFilesMap.TryGetValue(filePath, out var taggedFile))
                            {
                                tags = taggedFile.LocalFileTags.Select(lft => lft.LocalTag.Name).ToList();
                            }

                            fileMap[filePath] = new FileWithTags
                            {
                                FileName = fileInfo.Name,
                                FullPath = filePath,
                                DirectoryPath = GetMostSpecificDirectory(filePath, activeDirectories),
                                LastModified = fileInfo.LastWriteTime,
                                FileSize = fileInfo.Length,
                                Tags = tags
                            };
                        }
                        catch
                        {
                            // Skip files that can't be accessed
                            continue;
                        }
                    }
                }
                catch
                {
                    // Skip directories with issues
                    continue;
                }
            }

            return fileMap.Values.OrderBy(f => f.FileName).ToList();
        }
    }

    /// <summary>
    /// Information about a tag across all directories
    /// </summary>
    public class TagInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int TotalUsageCount { get; set; }
        public List<string> SourceDirectories { get; set; } = new List<string>();
        public string SourceDirectoriesString => string.Join("; ", SourceDirectories.Select(Path.GetFileName));
    }

    /// <summary>
    /// File information with associated tags
    /// </summary>
    public class FileWithTags
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        
        public string TagsString => string.Join(", ", Tags);
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
}
