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
        private static readonly string CollectionDirectory = Path.Combine(
            Path.GetTempPath(), "FileTagger_Selection");
        private static readonly int CollectionWindowMs = 400;
        private const string CollectionMutexName = "FileTagger_ManageTags_Collection";

        public static bool HandleCommandLineArgs(string[] args)
        {
            if (args.Length < 2)
                return false;

            var command = args[0];

            switch (command)
            {
                case "--manage-tags":
                {
                    var filePaths = ParseExistingFilePaths(args, startIndex: 1);
                    if (filePaths.Count == 0)
                    {
                        MessageBox.Show(
                            "Could not find the selected file.\n\n" +
                            "The file may have been moved or deleted, or the path could not be read.",
                            "File Tagger", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return true;
                    }

                    return HandleManageTags(filePaths);
                }

                case "--view-tags":
                    return HandleViewTags(NormalizePath(args[1]));

                case "--filter-directory":
                    return HandleFilterDirectory(NormalizePath(args[1]));

                default:
                    return false;
            }
        }

        private static List<string> ParseExistingFilePaths(string[] args, int startIndex)
        {
            var filePaths = new List<string>();
            for (int i = startIndex; i < args.Length; i++)
            {
                var path = NormalizePath(args[i]);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    filePaths.Add(path);
            }

            return filePaths;
        }

        private static string NormalizePath(string path) =>
            string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('"');

        private static bool HandleManageTags(List<string> filePaths)
        {
            try
            {
                if (filePaths == null || filePaths.Count == 0)
                    return false;

                Directory.CreateDirectory(CollectionDirectory);
                var collectionFilePath = Path.Combine(CollectionDirectory, "manage_tags.txt");
                AppendCollectionPaths(collectionFilePath, filePaths);

                // Explorer launches one process per selected file. The first process to acquire
                // the mutex waits briefly so siblings can append their paths, then opens one window.
                using var mutex = new Mutex(false, CollectionMutexName);
                var isCoordinator = TryAcquireCoordinatorMutex(mutex);

                if (isCoordinator)
                {
                    try
                    {
                        Thread.Sleep(CollectionWindowMs);
                        var allPaths = ReadCollectionPaths(collectionFilePath, filePaths);
                        OpenManageTagsWindow(allPaths);
                    }
                    finally
                    {
                        ReleaseCoordinatorMutex(mutex);
                    }
                }
                else
                {
                    // Sibling process — coordinator opens the combined window.
                    WaitForCoordinator(mutex);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error managing tags: {ex.Message}",
                    "File Tagger Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }

        private static bool TryAcquireCoordinatorMutex(Mutex mutex)
        {
            try
            {
                return mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                return mutex.WaitOne(0);
            }
        }

        private static void ReleaseCoordinatorMutex(Mutex mutex)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore — mutex may already have been released.
            }
        }

        private static void WaitForCoordinator(Mutex mutex)
        {
            try
            {
                mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                // Coordinator crashed — nothing to wait for.
            }
            catch
            {
                // Ignore wait failures for sibling processes.
            }
        }

        private static void OpenManageTagsWindow(IEnumerable<string> filePaths)
        {
            var processedPaths = ResolveManageTagPaths(filePaths);
            if (processedPaths.Count == 0)
            {
                MessageBox.Show(
                    "Selected files are not in a watched directory.\n\n" +
                    "Add the folder to File Tagger (Settings tab) and try again.",
                    "File Tagger", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var tagWindow = new TagManagementWindow(processedPaths);
            tagWindow.ShowDialog();
        }

        private static List<string> ResolveManageTagPaths(IEnumerable<string> filePaths)
        {
            var result = new List<string>();

            foreach (var path in filePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                if (TempResultsManager.Instance.IsInTempDirectory(path))
                {
                    var mappedPath = TempResultsManager.Instance.GetOriginalFilePath(path);
                    if (!string.IsNullOrEmpty(mappedPath))
                        result.Add(mappedPath);
                    continue;
                }

                if (!string.IsNullOrEmpty(DatabaseManager.Instance.GetWatchedDirectoryForFile(path)))
                    result.Add(path);
            }

            return result;
        }

        private static void AppendCollectionPaths(string collectionFilePath, IEnumerable<string> filePaths)
        {
            lock (CollectionDirectory)
            {
                try
                {
                    File.AppendAllLines(collectionFilePath, filePaths);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error writing manage-tags collection file: {ex.Message}");
                }
            }
        }

        private static List<string> ReadCollectionPaths(string collectionFilePath, List<string> fallbackPaths)
        {
            lock (CollectionDirectory)
            {
                try
                {
                    if (File.Exists(collectionFilePath))
                    {
                        var paths = File.ReadAllLines(collectionFilePath)
                            .Select(NormalizePath)
                            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        try { File.Delete(collectionFilePath); } catch { }

                        if (paths.Count > 0)
                            return paths;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading manage-tags collection file: {ex.Message}");
                }

                return fallbackPaths;
            }
        }

        private static bool HandleViewTags(string filePath)
        {
            try
            {
                filePath = NormalizePath(filePath);
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return false;

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

                var tags = DatabaseManager.Instance.GetTagsForFile(originalFilePath);

                if (!tags.Any())
                {
                    MessageBox.Show($"No tags found for:\n{Path.GetFileName(originalFilePath)}{displayNote}",
                        "File Tags", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var tagsText = string.Join("\n• ", tags);
                    MessageBox.Show($"Tags for {Path.GetFileName(originalFilePath)}:\n\n• {tagsText}{displayNote}",
                        "File Tags", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error viewing tags: {ex.Message}", "File Tagger Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }

        private static bool HandleFilterDirectory(string directoryPath)
        {
            try
            {
                directoryPath = NormalizePath(directoryPath);
                if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                    return false;

                var filterWindow = new DirectoryFilterWindow(directoryPath);
                filterWindow.ShowDialog();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error filtering directory: {ex.Message}", "File Tagger Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }
    }
}
