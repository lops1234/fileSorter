using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileTagger.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FileTagger.Services
{
    /// <summary>
    /// Manages the central database for all file tagging operations.
    /// Uses a single local database as the authoritative source of truth.
    /// Provides Pull/Push/Cleanup operations for syncing with folder databases.
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
        /// Get the path to the central database
        /// </summary>
        public string GetCentralDatabasePath() => CentralDbContext.GetDatabasePath();

        /// <summary>
        /// Initialize the central database and perform any necessary migrations
        /// </summary>
        public void Initialize()
        {
            // Ensure central database exists
            using (var centralDb = new CentralDbContext())
            {
                centralDb.Database.EnsureCreated();
            }

            // Check if this is first run with old data - migrate if needed
            MigrateFromOldDatabaseStructure();
        }

        /// <summary>
        /// Migrate data from old database structure (main.db + per-folder DBs) to new central database
        /// </summary>
        private void MigrateFromOldDatabaseStructure()
        {
            var oldMainDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileTagger", "main.db");

            if (!File.Exists(oldMainDbPath))
                return; // No old database to migrate

            try
            {
                using var centralDb = new CentralDbContext();
                
                // Check if we already have data in central DB
                if (centralDb.Directories.Any())
                    return; // Already migrated

                // Migrate from old structure
                using var oldMainDb = new MainDbContext();
                var oldDirectories = oldMainDb.WatchedDirectories.Where(d => d.IsActive).ToList();

                foreach (var oldDir in oldDirectories)
                {
                    // Add directory to central DB
                    var centralDir = new CentralDirectory
                    {
                        DirectoryPath = oldDir.DirectoryPath,
                        IsActive = oldDir.IsActive,
                        CreatedAt = oldDir.CreatedAt,
                        LastSyncAt = oldDir.LastSyncAt
                    };
                    centralDb.Directories.Add(centralDir);
                    centralDb.SaveChanges();

                    // Pull data from folder database if it exists
                    PullFromFolderInternal(centralDb, centralDir);
                        }
                    }
                    catch
                    {
                // If migration fails, continue with empty central database
            }
        }

        #region Directory Management

        /// <summary>
        /// Add a new watched directory
        /// </summary>
        public void AddWatchedDirectory(string directoryPath)
        {
            using var centralDb = new CentralDbContext();

            var existingDir = centralDb.Directories.FirstOrDefault(d => 
                d.DirectoryPath.ToLower() == directoryPath.ToLower());

            if (existingDir != null)
            {
                existingDir.IsActive = true;
                existingDir.LastSyncAt = DateTime.UtcNow;
            }
            else
            {
                var newDir = new CentralDirectory
                        {
                            DirectoryPath = directoryPath,
                            IsActive = true,
                            LastSyncAt = DateTime.UtcNow
                        };
                centralDb.Directories.Add(newDir);
                centralDb.SaveChanges();

                // Pull any existing data from folder database
                var dir = centralDb.Directories.First(d => d.DirectoryPath.ToLower() == directoryPath.ToLower());
                PullFromFolderInternal(centralDb, dir);
            }

            centralDb.SaveChanges();
        }

        /// <summary>
        /// Remove a watched directory (deactivates it)
        /// </summary>
        public void RemoveWatchedDirectory(string directoryPath)
        {
            using var centralDb = new CentralDbContext();

            var directory = centralDb.Directories.FirstOrDefault(d => 
                d.DirectoryPath.ToLower() == directoryPath.ToLower());

            if (directory != null)
            {
                directory.IsActive = false;
                centralDb.SaveChanges();
            }
        }

        /// <summary>
        /// Get all active watched directories
        /// </summary>
        public List<string> GetAllActiveDirectories()
        {
            using var centralDb = new CentralDbContext();
            return centralDb.Directories
                .Where(d => d.IsActive)
                .Select(d => d.DirectoryPath)
                .ToList();
        }

        /// <summary>
        /// Get the watched directory that contains a file
        /// Returns the most specific (deepest) watched directory
        /// </summary>
        public string GetWatchedDirectoryForFile(string filePath)
        {
            var watchedDirs = GetAllActiveDirectories();
            var fileDir = Path.GetDirectoryName(filePath) ?? "";

            return watchedDirs
                .Where(wd => fileDir.StartsWith(wd, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(wd => wd.Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Get all applicable directories for a file (from most specific to least)
        /// </summary>
        public List<string> GetApplicableDirectoriesForFile(string filePath)
        {
            var watchedDirs = GetAllActiveDirectories();
            var fileDir = Path.GetDirectoryName(filePath) ?? "";

            return watchedDirs
                .Where(wd => fileDir.StartsWith(wd, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(wd => wd.Length)
                .ToList();
        }

        #endregion

        #region Pull/Push/Cleanup Operations

        /// <summary>
        /// Pull all data from folder databases into the central database
        /// </summary>
        public PullResult PullFromFolder(string directoryPath)
        {
            var result = new PullResult { DirectoryPath = directoryPath };

            try
            {
                using var centralDb = new CentralDbContext();

                var centralDir = centralDb.Directories.FirstOrDefault(d =>
                    d.DirectoryPath.ToLower() == directoryPath.ToLower());

                if (centralDir == null)
                {
                    result.Errors.Add($"Directory not found in watched directories: {directoryPath}");
                    return result;
                }

                // Find all .filetagger directories (including duplicates)
                var fileTaggerDirs = FindAllFileTaggerDirectories(directoryPath);
                result.DatabasesFound = fileTaggerDirs.Count;

                foreach (var ftDir in fileTaggerDirs)
                {
                    try
                    {
                        var dbPath = Path.Combine(ftDir, "tags.db");
                        if (File.Exists(dbPath))
                        {
                            var pullStats = ImportFromFolderDatabase(centralDb, centralDir, dbPath);
                            result.TagsImported += pullStats.TagsImported;
                            result.FilesImported += pullStats.FilesImported;
                            result.AssociationsImported += pullStats.AssociationsImported;
                            result.DatabasesPulled++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Error pulling from {ftDir}: {ex.Message}");
                    }
                }

                centralDir.LastSyncAt = DateTime.UtcNow;
                centralDb.SaveChanges();
                result.Success = result.Errors.Count == 0;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Pull failed: {ex.Message}");
            }
            finally
            {
                // Release all connections after pull to allow subsequent delete operations
                ReleaseAllDatabaseConnections();
            }

            return result;
        }

        /// <summary>
        /// Push data from central database to a folder database
        /// </summary>
        public PushResult PushToFolder(string directoryPath)
        {
            var result = new PushResult { DirectoryPath = directoryPath };

            try
            {
                using var centralDb = new CentralDbContext();

                var centralDir = centralDb.Directories
                    .Include(d => d.Tags)
                    .Include(d => d.FileRecords)
                        .ThenInclude(f => f.FileTags)
                    .FirstOrDefault(d => d.DirectoryPath.ToLower() == directoryPath.ToLower());

                if (centralDir == null)
                {
                    result.Errors.Add($"Directory not found in watched directories: {directoryPath}");
                    return result;
                }

                // Create .filetagger directory if needed
                var fileTaggerDir = Path.Combine(directoryPath, ".filetagger");
                if (!Directory.Exists(fileTaggerDir))
                {
                    Directory.CreateDirectory(fileTaggerDir);
                }

                // Create/update the folder database
                using var folderDb = new DirectoryDbContext(directoryPath);
                folderDb.Database.EnsureCreated();

                // Export tags
                var existingTags = folderDb.LocalTags.ToList();
                var tagMapping = new Dictionary<int, int>(); // central ID -> local ID

                foreach (var centralTag in centralDir.Tags)
                {
                    var localTag = existingTags.FirstOrDefault(t =>
                        string.Equals(t.Name, centralTag.Name, StringComparison.OrdinalIgnoreCase));

                    if (localTag == null)
                    {
                        localTag = new LocalTag
                        {
                            Name = centralTag.Name,
                            Description = centralTag.Description,
                            CreatedAt = centralTag.CreatedAt,
                            LastUsedAt = centralTag.LastUsedAt
                        };
                        folderDb.LocalTags.Add(localTag);
                        folderDb.SaveChanges();
                        existingTags.Add(localTag);
                        result.TagsExported++;
                    }
                    else
                    {
                        // Update if central is newer
                        if (centralTag.LastUsedAt > localTag.LastUsedAt)
                        {
                            localTag.LastUsedAt = centralTag.LastUsedAt;
                            localTag.Description = centralTag.Description;
                        }
                    }

                    tagMapping[centralTag.Id] = localTag.Id;
                }

                // Export file records
                var existingFiles = folderDb.LocalFileRecords.ToList();
                var fileMapping = new Dictionary<int, int>(); // central ID -> local ID

                foreach (var centralFile in centralDir.FileRecords)
                {
                    var localFile = existingFiles.FirstOrDefault(f =>
                        string.Equals(f.RelativePath, centralFile.RelativePath, StringComparison.OrdinalIgnoreCase));

                    if (localFile == null)
                    {
                        localFile = new LocalFileRecord
                        {
                            FileName = centralFile.FileName,
                            RelativePath = centralFile.RelativePath,
                            LastModified = centralFile.LastModified,
                            FileSize = centralFile.FileSize,
                            CreatedAt = centralFile.CreatedAt
                        };
                        folderDb.LocalFileRecords.Add(localFile);
                        folderDb.SaveChanges();
                        existingFiles.Add(localFile);
                        result.FilesExported++;
            }
            else
            {
                        // Update if central is newer
                        if (centralFile.LastModified > localFile.LastModified)
                        {
                            localFile.LastModified = centralFile.LastModified;
                            localFile.FileSize = centralFile.FileSize;
                        }
                    }

                    fileMapping[centralFile.Id] = localFile.Id;
                }

                // Export file-tag associations
                var existingAssociations = folderDb.LocalFileTags.ToList()
                    .Select(ft => (ft.LocalFileRecordId, ft.LocalTagId))
                    .ToHashSet();

                var centralFileTags = centralDb.FileTags
                    .Where(ft => ft.FileRecord.DirectoryId == centralDir.Id)
                    .ToList();

                foreach (var centralFileTag in centralFileTags)
                {
                    if (!fileMapping.ContainsKey(centralFileTag.FileRecordId) ||
                        !tagMapping.ContainsKey(centralFileTag.TagId))
                        continue;

                    var localFileId = fileMapping[centralFileTag.FileRecordId];
                    var localTagId = tagMapping[centralFileTag.TagId];

                    if (!existingAssociations.Contains((localFileId, localTagId)))
                    {
                        folderDb.LocalFileTags.Add(new LocalFileTag
                        {
                            LocalFileRecordId = localFileId,
                            LocalTagId = localTagId
                        });
                        result.AssociationsExported++;
                    }
                }

                folderDb.SaveChanges();
                centralDir.LastSyncAt = DateTime.UtcNow;
                centralDb.SaveChanges();

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Push failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Clean up a folder: Pull all data, delete all .filetagger directories, push clean copy
        /// </summary>
        public CleanupResult CleanupFolder(string directoryPath)
        {
            var result = new CleanupResult { DirectoryPath = directoryPath };

            try
            {
                // Step 1: Pull all data from folder databases into central database
                var pullResult = PullFromFolder(directoryPath);
                result.PullResult = pullResult;

                if (!pullResult.Success && pullResult.Errors.Any())
                {
                    result.Errors.AddRange(pullResult.Errors);
                    // Continue anyway to clean up
                }

                // Step 2: Release all database connections before deleting
                ReleaseAllDatabaseConnections();

                // Step 3: Delete ALL .filetagger directories (including base and all duplicates)
                var fileTaggerDirs = FindAllFileTaggerDirectories(directoryPath);
                foreach (var ftDir in fileTaggerDirs)
                {
                    if (SafeDeleteDirectory(ftDir, result.Errors))
                    {
                        result.DirectoriesDeleted++;
                    }
                }

                // Step 4: Push clean copy back to folder (creates fresh .filetagger)
                var pushResult = PushToFolder(directoryPath);
                result.PushResult = pushResult;

                if (!pushResult.Success)
                {
                    result.Errors.AddRange(pushResult.Errors);
                }

                result.Success = result.DirectoriesDeleted > 0 || pullResult.DatabasesPulled > 0;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Cleanup failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Pull data from all watched directories
        /// </summary>
        public List<PullResult> PullFromAllFolders()
        {
            var results = new List<PullResult>();
            var directories = GetAllActiveDirectories();

            foreach (var dir in directories)
            {
                results.Add(PullFromFolder(dir));
            }

            return results;
        }

        /// <summary>
        /// Push data to all watched directories
        /// </summary>
        public List<PushResult> PushToAllFolders()
        {
            var results = new List<PushResult>();
            var directories = GetAllActiveDirectories();
            
            foreach (var dir in directories)
            {
                results.Add(PushToFolder(dir));
            }

            return results;
        }

        #endregion

        #region Tag Management

        /// <summary>
        /// Add a tag to a file
        /// </summary>
        public void AddTagToFile(string filePath, string tagName, string tagDescription = "")
        {
            var watchedDir = GetWatchedDirectoryForFile(filePath);
            if (string.IsNullOrEmpty(watchedDir))
                return;

            using var centralDb = new CentralDbContext();

            var centralDir = centralDb.Directories.FirstOrDefault(d =>
                d.DirectoryPath.ToLower() == watchedDir.ToLower());

            if (centralDir == null)
                return;

            var relativePath = Path.GetRelativePath(watchedDir, filePath);
            var fileName = Path.GetFileName(filePath);

            // Get or create file record
            var fileRecord = centralDb.FileRecords.FirstOrDefault(f =>
                f.DirectoryId == centralDir.Id &&
                f.RelativePath.ToLower() == relativePath.ToLower());

            if (fileRecord == null)
            {
                var fileInfo = new FileInfo(filePath);
                fileRecord = new CentralFileRecord
                {
                    FileName = fileName,
                    RelativePath = relativePath,
                    DirectoryId = centralDir.Id,
                    LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.UtcNow,
                    FileSize = fileInfo.Exists ? fileInfo.Length : 0
                };
                centralDb.FileRecords.Add(fileRecord);
                centralDb.SaveChanges();
            }

            // Get or create tag
            var tag = centralDb.Tags.FirstOrDefault(t =>
                t.DirectoryId == centralDir.Id &&
                t.Name.ToLower() == tagName.ToLower());

            if (tag == null)
            {
                tag = new CentralTag
                {
                    Name = tagName,
                    Description = tagDescription,
                    DirectoryId = centralDir.Id,
                    LastUsedAt = DateTime.UtcNow
                };
                centralDb.Tags.Add(tag);
                centralDb.SaveChanges();
                }
                else
                {
                tag.LastUsedAt = DateTime.UtcNow;
            }

            // Create file-tag association if it doesn't exist
            var existingAssociation = centralDb.FileTags.FirstOrDefault(ft =>
                ft.FileRecordId == fileRecord.Id && ft.TagId == tag.Id);

            if (existingAssociation == null)
            {
                centralDb.FileTags.Add(new CentralFileTag
                {
                    FileRecordId = fileRecord.Id,
                    TagId = tag.Id
                });
            }

            centralDb.SaveChanges();
        }

        /// <summary>
        /// Remove a tag from a file
        /// </summary>
        public void RemoveTagFromFile(string filePath, string tagName)
        {
            var watchedDir = GetWatchedDirectoryForFile(filePath);
            if (string.IsNullOrEmpty(watchedDir))
                return;

            using var centralDb = new CentralDbContext();

            var centralDir = centralDb.Directories.FirstOrDefault(d =>
                d.DirectoryPath.ToLower() == watchedDir.ToLower());

            if (centralDir == null)
                return;

            var relativePath = Path.GetRelativePath(watchedDir, filePath);

            var fileRecord = centralDb.FileRecords.FirstOrDefault(f =>
                f.DirectoryId == centralDir.Id &&
                f.RelativePath.ToLower() == relativePath.ToLower());

            if (fileRecord == null)
                return;

            var tag = centralDb.Tags.FirstOrDefault(t =>
                t.DirectoryId == centralDir.Id &&
                t.Name.ToLower() == tagName.ToLower());

            if (tag == null)
                return;

            var association = centralDb.FileTags.FirstOrDefault(ft =>
                ft.FileRecordId == fileRecord.Id && ft.TagId == tag.Id);

            if (association != null)
            {
                centralDb.FileTags.Remove(association);
                centralDb.SaveChanges();
            }
        }

        /// <summary>
        /// Get all available tags from all active directories
        /// </summary>
        public List<TagInfo> GetAllAvailableTags()
        {
            using var centralDb = new CentralDbContext();

            var tags = centralDb.Tags
                .Include(t => t.Directory)
                .Include(t => t.FileTags)
                .Where(t => t.Directory.IsActive)
                .ToList();

            return tags
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TagInfo
                {
                    Name = g.Key,
                    Description = g.First().Description,
                    TotalUsageCount = g.Sum(t => t.FileTags.Count),
                    SourceDirectories = g.Select(t => t.Directory.DirectoryPath).Distinct().ToList()
                })
                .Where(t => t.TotalUsageCount > 0) // Only show tags with files
                .OrderBy(t => t.Name)
                .ToList();
        }

        /// <summary>
        /// Create a standalone tag in a specific directory
        /// </summary>
        public void CreateStandaloneTag(string directoryPath, string tagName, string tagDescription = "")
        {
            if (string.IsNullOrEmpty(directoryPath) || string.IsNullOrEmpty(tagName))
                return;

            using var centralDb = new CentralDbContext();

            var centralDir = centralDb.Directories.FirstOrDefault(d =>
                d.DirectoryPath.ToLower() == directoryPath.ToLower());

            if (centralDir == null)
                return;

            var existingTag = centralDb.Tags.FirstOrDefault(t =>
                t.DirectoryId == centralDir.Id &&
                t.Name.ToLower() == tagName.ToLower());

            if (existingTag != null)
            {
                if (!string.IsNullOrEmpty(tagDescription))
                {
                    existingTag.Description = tagDescription;
                    existingTag.LastUsedAt = DateTime.UtcNow;
                    centralDb.SaveChanges();
                }
                return;
            }

            var tag = new CentralTag
            {
                Name = tagName,
                Description = tagDescription,
                DirectoryId = centralDir.Id,
                LastUsedAt = DateTime.UtcNow
            };
            centralDb.Tags.Add(tag);
            centralDb.SaveChanges();
        }

        /// <summary>
        /// Create a standalone tag in all active directories
        /// </summary>
        public void CreateStandaloneTagInAllDirectories(string tagName, string tagDescription = "")
        {
            var directories = GetAllActiveDirectories();
            foreach (var dir in directories)
            {
                CreateStandaloneTag(dir, tagName, tagDescription);
            }
        }

        /// <summary>
        /// Update a tag name across all directories
        /// </summary>
        public void UpdateTagName(string oldName, string newName)
        {
            using var centralDb = new CentralDbContext();

            var tagsToUpdate = centralDb.Tags
                .Where(t => t.Name.ToLower() == oldName.ToLower())
                .ToList();

            foreach (var tag in tagsToUpdate)
            {
                tag.Name = newName;
            }

            centralDb.SaveChanges();
        }

        /// <summary>
        /// Delete a tag from all directories
        /// </summary>
        public void DeleteTag(string tagName)
        {
            using var centralDb = new CentralDbContext();

            var tagsToDelete = centralDb.Tags
                .Include(t => t.FileTags)
                .Where(t => t.Name.ToLower() == tagName.ToLower())
                .ToList();

            foreach (var tag in tagsToDelete)
            {
                centralDb.Tags.Remove(tag);
            }

            centralDb.SaveChanges();
        }

        #endregion

        #region File Management

        /// <summary>
        /// Get all files with tags across all active directories
        /// </summary>
        public List<FileWithTags> GetAllFilesWithTags()
        {
            using var centralDb = new CentralDbContext();

            var files = centralDb.FileRecords
                .Include(f => f.Directory)
                .Include(f => f.FileTags)
                    .ThenInclude(ft => ft.Tag)
                .Where(f => f.Directory.IsActive && f.FileTags.Any())
                .ToList();

            return files.Select(f => new FileWithTags
            {
                FileName = f.FileName,
                FullPath = Path.Combine(f.Directory.DirectoryPath, f.RelativePath),
                DirectoryPath = f.Directory.DirectoryPath,
                LastModified = f.LastModified,
                FileSize = f.FileSize,
                Tags = f.FileTags.Select(ft => ft.Tag.Name).ToList()
            })
            .OrderBy(f => f.FileName)
            .ToList();
        }

        /// <summary>
        /// Get untagged files from watched directories
        /// </summary>
        public List<FileWithTags> GetUntaggedFiles()
        {
            var result = new List<FileWithTags>();
            var directories = GetAllActiveDirectories();

            using var centralDb = new CentralDbContext();

            // Get all tagged file paths
            var taggedFilePaths = centralDb.FileRecords
                .Include(f => f.Directory)
                .Include(f => f.FileTags)
                .Where(f => f.Directory.IsActive && f.FileTags.Any())
                .Select(f => Path.Combine(f.Directory.DirectoryPath, f.RelativePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in directories)
            {
                try
                {
                    var allFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);

                    foreach (var filePath in allFiles)
                    {
                        if (taggedFilePaths.Contains(filePath))
                            continue;

                        try
                {
                    var fileInfo = new FileInfo(filePath);
                            result.Add(new FileWithTags
                    {
                                FileName = fileInfo.Name,
                                FullPath = filePath,
                                DirectoryPath = dir,
                        LastModified = fileInfo.LastWriteTime,
                                FileSize = fileInfo.Length,
                                Tags = new List<string>()
                            });
                        }
                        catch
                        {
                            // Skip files that can't be accessed
                        }
                    }
                }
                catch
                {
                    // Skip directories with issues
                }
            }

            return result.OrderBy(f => f.FileName).ToList();
        }

        /// <summary>
        /// Get all files in watched directories (both tagged and untagged)
        /// </summary>
        public List<FileWithTags> GetAllFilesInWatchedDirectories()
        {
            var result = new Dictionary<string, FileWithTags>(StringComparer.OrdinalIgnoreCase);
            var directories = GetAllActiveDirectories();

            using var centralDb = new CentralDbContext();

            // Get all tagged files
            var taggedFiles = centralDb.FileRecords
                .Include(f => f.Directory)
                .Include(f => f.FileTags)
                    .ThenInclude(ft => ft.Tag)
                .Where(f => f.Directory.IsActive)
                .ToList();

            foreach (var file in taggedFiles)
            {
                var fullPath = Path.Combine(file.Directory.DirectoryPath, file.RelativePath);
                result[fullPath] = new FileWithTags
                {
                    FileName = file.FileName,
                    FullPath = fullPath,
                    DirectoryPath = file.Directory.DirectoryPath,
                    LastModified = file.LastModified,
                    FileSize = file.FileSize,
                    Tags = file.FileTags.Select(ft => ft.Tag.Name).ToList()
                };
            }

            // Get all files from filesystem
            foreach (var dir in directories)
            {
                try
                {
                    var allFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);

                    foreach (var filePath in allFiles)
                    {
                        if (result.ContainsKey(filePath))
                            continue;

                        try
                        {
                            var fileInfo = new FileInfo(filePath);
                            result[filePath] = new FileWithTags
                            {
                                FileName = fileInfo.Name,
                                FullPath = filePath,
                                DirectoryPath = dir,
                                LastModified = fileInfo.LastWriteTime,
                                FileSize = fileInfo.Length,
                                Tags = new List<string>()
                            };
                        }
                        catch
                        {
                            // Skip files that can't be accessed
                        }
                    }
                }
                catch
                {
                    // Skip directories with issues
                }
            }

            return result.Values.OrderBy(f => f.FileName).ToList();
        }

        /// <summary>
        /// Get or create a directory database context (legacy compatibility)
        /// WARNING: In the new architecture, prefer using central database methods
        /// </summary>
        public DirectoryDbContext GetDirectoryDb(string directoryPath)
        {
            var dirDb = new DirectoryDbContext(directoryPath);
            dirDb.Database.EnsureCreated();
            return dirDb;
        }

        /// <summary>
        /// Get tags for a specific file
        /// </summary>
        public List<string> GetTagsForFile(string filePath)
        {
            var watchedDir = GetWatchedDirectoryForFile(filePath);
            if (string.IsNullOrEmpty(watchedDir))
                return new List<string>();

            using var centralDb = new CentralDbContext();

            var centralDir = centralDb.Directories.FirstOrDefault(d =>
                d.DirectoryPath.ToLower() == watchedDir.ToLower());

            if (centralDir == null)
                return new List<string>();

            var relativePath = Path.GetRelativePath(watchedDir, filePath);

            var fileRecord = centralDb.FileRecords
                .Include(f => f.FileTags)
                    .ThenInclude(ft => ft.Tag)
                .FirstOrDefault(f =>
                    f.DirectoryId == centralDir.Id &&
                    f.RelativePath.ToLower() == relativePath.ToLower());

            if (fileRecord == null)
                return new List<string>();

            return fileRecord.FileTags.Select(ft => ft.Tag.Name).ToList();
        }

        /// <summary>
        /// Verify that all tagged files exist and clean up missing files
        /// </summary>
        public FileVerificationResult VerifyAndCleanupTaggedFiles()
        {
            var result = new FileVerificationResult();
            var affectedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var centralDb = new CentralDbContext();

            var files = centralDb.FileRecords
                .Include(f => f.Directory)
                .Include(f => f.FileTags)
                    .ThenInclude(ft => ft.Tag)
                .Where(f => f.Directory.IsActive)
                .ToList();

            var fileTagsToRemove = new List<CentralFileTag>();
            var fileRecordsToRemove = new List<CentralFileRecord>();

            foreach (var file in files)
            {
                var fullPath = Path.Combine(file.Directory.DirectoryPath, file.RelativePath);
                result.TotalFilesChecked++;

                if (!File.Exists(fullPath))
                {
                    result.MissingFiles.Add(fullPath);
                    result.MissingFilesCount++;

                    // Track affected tags and collect file-tag associations to remove
                    foreach (var fileTag in file.FileTags)
                    {
                        affectedTags.Add(fileTag.Tag.Name);
                        fileTagsToRemove.Add(fileTag);
                    }

                    fileRecordsToRemove.Add(file);
                }
                else
                {
                    result.ExistingFilesCount++;
                }
            }

            // Explicitly remove file-tag associations first (this updates tag counts)
            centralDb.FileTags.RemoveRange(fileTagsToRemove);
            centralDb.SaveChanges();

            // Then remove file records
            centralDb.FileRecords.RemoveRange(fileRecordsToRemove);
            centralDb.SaveChanges();

            result.AffectedTags = affectedTags.ToList();
            result.Success = result.Errors.Count == 0;
            return result;
        }

        #endregion

        #region Synchronization (Legacy Support)

        /// <summary>
        /// Synchronize all tags (refreshes from central database)
        /// </summary>
        public void SynchronizeAllTags()
        {
            // In the new architecture, all data is in central DB
            // This method now just verifies and cleans up
            VerifyAndCleanupTaggedFiles();
        }

        /// <summary>
        /// Synchronize tags for a specific directory (legacy support)
        /// </summary>
        public void SynchronizeDirectoryTags(string directoryPath)
        {
            // In new architecture, this is a no-op as all data is already centralized
            // Kept for API compatibility
        }

        #endregion

        #region Database Merge (Legacy Support)

        /// <summary>
        /// Find and merge all duplicate .filetagger databases
        /// In the new architecture, this pulls from all duplicates then cleans up
        /// </summary>
        public DatabaseMergeResult MergeAllDuplicateFileTaggerDatabases()
        {
            var result = new DatabaseMergeResult();
            var directories = GetAllActiveDirectories();

            foreach (var dir in directories)
            {
                var duplicateDirs = FindDuplicateFileTaggerDirectories(dir);
                if (duplicateDirs.Any())
                {
                    result.DirectoriesWithDuplicates++;
                    result.DuplicateDatabasesFound += duplicateDirs.Count;

                    // Pull from all (including duplicates) into central database
                    var pullResult = PullFromFolder(dir);

                    // Release all database connections before deleting
                    ReleaseAllDatabaseConnections();

                    // Delete duplicates only (keep the original .filetagger)
                    foreach (var dupDir in duplicateDirs)
                    {
                        if (SafeDeleteDirectory(dupDir, result.Errors))
                        {
                            result.DuplicateDatabasesDeleted++;
                        }
                    }

                    result.TagsMerged += pullResult.TagsImported;
                    result.FilesMerged += pullResult.FilesImported;
                    result.AssociationsMerged += pullResult.AssociationsImported;
                }
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        #endregion

        #region Discovery

        /// <summary>
        /// Discover and import existing .filetagger directories
        /// </summary>
        public List<string> DiscoverAndImportExistingDatabases(string rootPath = null)
        {
            var discoveredDirectories = new List<string>();
            var searchRoots = string.IsNullOrEmpty(rootPath) ? GetSearchRoots() : new List<string> { rootPath };

            using var centralDb = new CentralDbContext();
            var existingDirs = centralDb.Directories.Select(d => d.DirectoryPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var searchRoot in searchRoots)
            {
                try
                {
                    if (!Directory.Exists(searchRoot))
                        continue;

                    var fileTaggerDirs = Directory.GetDirectories(searchRoot, ".filetagger", SearchOption.AllDirectories);

                    foreach (var ftDir in fileTaggerDirs)
                    {
                        var parentDir = Path.GetDirectoryName(ftDir);
                        if (string.IsNullOrEmpty(parentDir) || existingDirs.Contains(parentDir))
                            continue;
                        
                        var dbFile = Path.Combine(ftDir, "tags.db");
                        if (File.Exists(dbFile))
                        {
                            AddWatchedDirectory(parentDir);
                            discoveredDirectories.Add(parentDir);
                            existingDirs.Add(parentDir);
                        }
                    }
                }
                catch
                {
                            continue;
                }
            }

            return discoveredDirectories;
        }

        private List<string> GetSearchRoots()
        {
            var searchRoots = new List<string>();

            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    searchRoots.Add(userProfile);

                    var commonDirs = new[] { "Documents", "Downloads", "Desktop", "Pictures", "Videos" };
                    foreach (var dir in commonDirs)
                    {
                        var fullPath = Path.Combine(userProfile, dir);
                        if (Directory.Exists(fullPath))
                            searchRoots.Add(fullPath);
                    }
                }

                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                    .Select(d => d.RootDirectory.FullName);

                searchRoots.AddRange(drives);
                        }
                        catch
                        {
                searchRoots.Add(Environment.CurrentDirectory);
            }

            return searchRoots;
        }

        #endregion

        #region Helper Methods

        private List<string> FindAllFileTaggerDirectories(string directoryPath)
        {
            var result = new List<string>();
            var baseDir = Path.Combine(directoryPath, ".filetagger");

            if (Directory.Exists(baseDir))
                result.Add(baseDir);

            // Find duplicates
            result.AddRange(FindDuplicateFileTaggerDirectories(directoryPath));

            return result;
        }

        private List<string> FindDuplicateFileTaggerDirectories(string directoryPath)
        {
            var duplicates = new List<string>();

            for (int i = 1; i <= 20; i++)
            {
                var duplicateDir = Path.Combine(directoryPath, $".filetagger ({i})");
                if (Directory.Exists(duplicateDir))
                {
                    var dbPath = Path.Combine(duplicateDir, "tags.db");
                    if (File.Exists(dbPath))
                        duplicates.Add(duplicateDir);
                }
            }

            return duplicates;
        }

        /// <summary>
        /// Force release all SQLite connections to allow file deletion
        /// </summary>
        private void ReleaseAllDatabaseConnections()
        {
            // Clear SQLite connection pool to release all file handles
            SqliteConnection.ClearAllPools();
            
            // Force garbage collection to release any remaining references
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // Give the OS time to release file handles
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Safely delete a directory containing database files
        /// </summary>
        private bool SafeDeleteDirectory(string directoryPath, List<string> errors)
            {
                try
                {
                // Release all connections before attempting delete
                ReleaseAllDatabaseConnections();
                
                // Try to delete
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, true);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to delete {directoryPath}: {ex.Message}");
                return false;
            }
        }

        private void PullFromFolderInternal(CentralDbContext centralDb, CentralDirectory centralDir)
        {
            var fileTaggerDirs = FindAllFileTaggerDirectories(centralDir.DirectoryPath);

            foreach (var ftDir in fileTaggerDirs)
            {
                var dbPath = Path.Combine(ftDir, "tags.db");
                if (File.Exists(dbPath))
                {
                    try
                    {
                        ImportFromFolderDatabase(centralDb, centralDir, dbPath);
                    }
                    catch
                    {
                        // Skip databases that can't be imported
                    }
                }
            }
        }

        private (int TagsImported, int FilesImported, int AssociationsImported) ImportFromFolderDatabase(
            CentralDbContext centralDb, CentralDirectory centralDir, string dbPath)
        {
            int tagsImported = 0, filesImported = 0, associationsImported = 0;

            List<LocalTag> localTags;
            List<LocalFileRecord> localFiles;

            // Read data using a direct SQLite connection to avoid connection pooling issues
            var connectionString = $"Data Source={dbPath};Mode=ReadOnly;Pooling=False";

            // Read data in separate scope with explicit connection management
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                
                var options = new DbContextOptionsBuilder<DirectoryDbContext>()
                    .UseSqlite(connection)
                    .Options;

                using (var folderDb = new DirectoryDbContext(centralDir.DirectoryPath))
                {
                    folderDb.Database.SetConnectionString(connectionString);

                    localTags = folderDb.LocalTags.ToList();
                    localFiles = folderDb.LocalFileRecords
                        .Include(f => f.LocalFileTags)
                        .ToList();

                    folderDb.Database.CloseConnection();
                }
                
                connection.Close();
            }
            
            // Clear the connection from pool immediately
            SqliteConnection.ClearPool(new SqliteConnection(connectionString));

            // Import tags
            var existingTags = centralDb.Tags
                .Where(t => t.DirectoryId == centralDir.Id)
                .ToList();
            var tagMapping = new Dictionary<int, int>();

            foreach (var localTag in localTags)
            {
                var existingTag = existingTags.FirstOrDefault(t =>
                    string.Equals(t.Name, localTag.Name, StringComparison.OrdinalIgnoreCase));

                if (existingTag == null)
                {
                    var newTag = new CentralTag
                    {
                        Name = localTag.Name,
                        Description = localTag.Description,
                        DirectoryId = centralDir.Id,
                        CreatedAt = localTag.CreatedAt,
                        LastUsedAt = localTag.LastUsedAt
                    };
                    centralDb.Tags.Add(newTag);
                    centralDb.SaveChanges();
                    existingTags.Add(newTag);
                    tagMapping[localTag.Id] = newTag.Id;
                    tagsImported++;
                }
                else
                {
                    if (localTag.LastUsedAt > existingTag.LastUsedAt)
                    {
                        existingTag.LastUsedAt = localTag.LastUsedAt;
                    }
                    tagMapping[localTag.Id] = existingTag.Id;
                }
            }

            // Import files
            var existingFiles = centralDb.FileRecords
                .Where(f => f.DirectoryId == centralDir.Id)
                .ToList();
            var fileMapping = new Dictionary<int, int>();

            foreach (var localFile in localFiles)
            {
                var existingFile = existingFiles.FirstOrDefault(f =>
                    string.Equals(f.RelativePath, localFile.RelativePath, StringComparison.OrdinalIgnoreCase));

                if (existingFile == null)
                {
                    var newFile = new CentralFileRecord
                    {
                        FileName = localFile.FileName,
                        RelativePath = localFile.RelativePath,
                        DirectoryId = centralDir.Id,
                        CreatedAt = localFile.CreatedAt,
                        LastModified = localFile.LastModified,
                        FileSize = localFile.FileSize
                    };
                    centralDb.FileRecords.Add(newFile);
                    centralDb.SaveChanges();
                    existingFiles.Add(newFile);
                    fileMapping[localFile.Id] = newFile.Id;
                    filesImported++;
                }
                else
                {
                    if (localFile.LastModified > existingFile.LastModified)
                    {
                        existingFile.LastModified = localFile.LastModified;
                        existingFile.FileSize = localFile.FileSize;
                    }
                    fileMapping[localFile.Id] = existingFile.Id;
                }
            }

            // Import associations
            var existingAssociations = centralDb.FileTags
                .Where(ft => ft.FileRecord.DirectoryId == centralDir.Id)
                .Select(ft => new { ft.FileRecordId, ft.TagId })
                .ToHashSet();

            foreach (var localFile in localFiles)
            {
                if (!fileMapping.ContainsKey(localFile.Id))
                    continue;

                var newFileId = fileMapping[localFile.Id];

                foreach (var localFileTag in localFile.LocalFileTags)
                {
                    if (!tagMapping.ContainsKey(localFileTag.LocalTagId))
                        continue;

                    var newTagId = tagMapping[localFileTag.LocalTagId];

                    var assocKey = new { FileRecordId = newFileId, TagId = newTagId };
                    if (!existingAssociations.Any(a => a.FileRecordId == newFileId && a.TagId == newTagId))
                    {
                        centralDb.FileTags.Add(new CentralFileTag
                        {
                            FileRecordId = newFileId,
                            TagId = newTagId
                        });
                        associationsImported++;
                    }
                }
            }

            centralDb.SaveChanges();

            return (tagsImported, filesImported, associationsImported);
        }

        /// <summary>
        /// Get database path for a directory (for UI display)
        /// </summary>
        public string GetDatabasePathForDirectory(string directoryPath)
        {
            // In new architecture, always return central database path
            return GetCentralDatabasePath();
        }

        /// <summary>
        /// Check if a folder database exists for a directory
        /// </summary>
        public bool FolderDatabaseExists(string directoryPath)
        {
            var dbPath = Path.Combine(directoryPath, ".filetagger", "tags.db");
            return File.Exists(dbPath);
        }

        /// <summary>
        /// Check if duplicate folder databases exist
        /// </summary>
        public bool HasDuplicateDatabases(string directoryPath)
        {
            return FindDuplicateFileTaggerDirectories(directoryPath).Any();
        }

        #endregion
    }

    #region Result Classes

    /// <summary>
    /// Result of a Pull operation
    /// </summary>
    public class PullResult
    {
        public string DirectoryPath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int DatabasesFound { get; set; }
        public int DatabasesPulled { get; set; }
        public int TagsImported { get; set; }
        public int FilesImported { get; set; }
        public int AssociationsImported { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of a Push operation
    /// </summary>
    public class PushResult
    {
        public string DirectoryPath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int TagsExported { get; set; }
        public int FilesExported { get; set; }
        public int AssociationsExported { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of a Cleanup operation
    /// </summary>
    public class CleanupResult
    {
        public string DirectoryPath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int DirectoriesDeleted { get; set; }
        public PullResult PullResult { get; set; }
        public PushResult PushResult { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of database merge operation (legacy)
    /// </summary>
    public class DatabaseMergeResult
    {
        public bool Success { get; set; }
        public int DirectoriesWithDuplicates { get; set; }
        public int DuplicateDatabasesFound { get; set; }
        public int DuplicateDatabasesDeleted { get; set; }
        public int TagsMerged { get; set; }
        public int FilesMerged { get; set; }
        public int AssociationsMerged { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of file verification and cleanup operation
    /// </summary>
    public class FileVerificationResult
    {
        public bool Success { get; set; }
        public int TotalFilesChecked { get; set; }
        public int ExistingFilesCount { get; set; }
        public int MissingFilesCount { get; set; }
        public int TagsRemoved { get; set; }
        public List<string> MissingFiles { get; set; } = new List<string>();
        public List<string> AffectedTags { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
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

    #endregion
}
