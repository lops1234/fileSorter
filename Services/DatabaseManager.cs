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

            // Sync tags from all directory databases
            SynchronizeAllTags();
        }

        /// <summary>
        /// Get or create a directory database context for the specified path
        /// </summary>
        public DirectoryDbContext GetDirectoryDb(string directoryPath)
        {
            var dirDb = new DirectoryDbContext(directoryPath);
            dirDb.Database.EnsureCreated();
            return dirDb;
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
        /// Add a tag to a file in a specific directory
        /// </summary>
        public void AddTagToFile(string filePath, string tagName, string tagDescription = "")
        {
            var directoryPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directoryPath)) return;

            var relativePath = Path.GetRelativePath(directoryPath, filePath);
            var fileName = Path.GetFileName(filePath);

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

        /// <summary>
        /// Get all files with tags across all directories
        /// </summary>
        public List<FileWithTags> GetAllFilesWithTags()
        {
            var result = new List<FileWithTags>();
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
                        result.Add(new FileWithTags
                        {
                            FileName = file.FileName,
                            FullPath = fullPath,
                            DirectoryPath = directoryPath,
                            LastModified = file.LastModified,
                            FileSize = file.FileSize,
                            Tags = file.LocalFileTags.Select(lft => lft.LocalTag.Name).ToList()
                        });
                    }
                }
                catch
                {
                    // Skip directories with database issues
                    continue;
                }
            }

            return result.OrderBy(f => f.FileName).ToList();
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
