using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FileTagger.Services;
using FileTagger.Windows;

namespace FileTagger
{
    public static class CommandLineHandler
    {
        private const int ManageTagsDebounceMs = 300;
        private static readonly TimeSpan ManageTagsMergeWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan CoordinatorLookupTimeout = TimeSpan.FromSeconds(5);
        private const int CollectorConnectTimeoutMs = 250;
        private const string CollectionMutexName = "FileTagger_ManageTags_Collection";

        public static bool HandleCommandLineArgs(string[] args)
        {
            if (args.Length < 2)
                return false;

            if (IsMainApplicationRunning() && CliIpc.TrySend(args))
                return true;

            var command = args[0];

            switch (command)
            {
                case "--manage-tags":
                {
                    var filePaths = ParseExistingFilePaths(args, startIndex: 1);
                    if (filePaths.Count == 0)
                    {
                        CliConsole.Show(
                            "File Tagger",
                            "Could not find the selected file.\n\n" +
                            "The file may have been moved or deleted, or the path could not be read.",
                            MessageBoxImage.Warning);
                        return true;
                    }

                    return HandleManageTags(filePaths);
                }

                case "--view-tags":
                    DatabaseManager.Instance.Initialize();
                    return HandleViewTags(NormalizePath(args[1]));

                case "--filter-directory":
                    DatabaseManager.Instance.Initialize();
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

        private static bool IsMainApplicationRunning()
        {
            try
            {
                using var mutex = Mutex.OpenExisting("FileTagger_MainApplication");
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
        }

        private static bool HandleManageTags(List<string> filePaths)
        {
            try
            {
                // Explorer launches one process per selected file. The first process becomes the
                // coordinator and hosts a pipe; every other process forwards its path to it.
                var payload = new[] { "--manage-tags" }.Concat(filePaths).ToArray();
                using var mutex = new Mutex(false, CollectionMutexName);
                var deadline = DateTime.UtcNow + CoordinatorLookupTimeout;

                while (DateTime.UtcNow < deadline)
                {
                    if (TryAcquireCoordinatorMutex(mutex))
                    {
                        try
                        {
                            RunManageTagsCoordinator(filePaths);
                        }
                        finally
                        {
                            ReleaseCoordinatorMutex(mutex);
                        }
                        return true;
                    }

                    if (CliIpc.TrySend(CliIpc.ManageTagsCollectorPipeName, payload, CollectorConnectTimeoutMs))
                        return true;
                }

                DatabaseManager.Instance.Initialize();
                OpenManageTagsWindow(filePaths);
                return true;
            }
            catch (Exception ex)
            {
                CliConsole.ShowError("File Tagger Error", "Error managing tags.", ex);
                return true;
            }
        }

        private static void RunManageTagsCoordinator(List<string> filePaths)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            CliIpc.StartServer(CliIpc.ManageTagsCollectorPipeName, args =>
                dispatcher.BeginInvoke(new Action(() => HandleCommandLineArgsFromRunningApp(args))));

            var frame = new DispatcherFrame();
            _manageTagsSessionEnded = () => frame.Continue = false;

            QueueManageTagsFromRunningApp(filePaths);
            DatabaseManager.Instance.Initialize();

            Dispatcher.PushFrame(frame);
            _manageTagsSessionEnded = null;
        }

        private static bool TryAcquireCoordinatorMutex(Mutex mutex)
        {
            try
            {
                return mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died; ownership has been transferred to us.
                return true;
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

        public static void HandleCommandLineArgsFromRunningApp(string[] args)
        {
            if (args.Length < 2)
                return;

            switch (args[0])
            {
                case "--manage-tags":
                {
                    var filePaths = ParseExistingFilePaths(args, startIndex: 1);
                    if (filePaths.Count > 0)
                        QueueManageTagsFromRunningApp(filePaths);
                    break;
                }
                case "--view-tags":
                    HandleViewTags(NormalizePath(args[1]));
                    break;
                case "--filter-directory":
                    HandleFilterDirectory(NormalizePath(args[1]));
                    break;
            }
        }

        private static readonly List<string> PendingManageTagPaths = new List<string>();
        private static DispatcherTimer _manageTagsDebounceTimer;
        private static TagManagementWindow _openManageTagsWindow;
        private static DateTime _lastManageTagsRequestUtc = DateTime.MinValue;
        private static Action _manageTagsSessionEnded;

        /// <summary>
        /// Must be called on the UI thread. Requests that arrive in quick succession (one per
        /// Explorer-launched process) are merged into a single Manage Tags window.
        /// </summary>
        public static void QueueManageTagsFromRunningApp(IEnumerable<string> filePaths)
        {
            var now = DateTime.UtcNow;
            var isSameSelection = now - _lastManageTagsRequestUtc < ManageTagsMergeWindow;
            _lastManageTagsRequestUtc = now;

            if (isSameSelection && _openManageTagsWindow != null)
            {
                var resolved = ResolveManageTagPaths(filePaths);
                if (resolved.Count > 0)
                    _openManageTagsWindow.AddFiles(resolved);
                return;
            }

            lock (PendingManageTagPaths)
            {
                PendingManageTagPaths.AddRange(filePaths);
            }

            EnsureManageTagsDebounceTimer();
            _manageTagsDebounceTimer.Stop();
            _manageTagsDebounceTimer.Start();
        }

        private static void EnsureManageTagsDebounceTimer()
        {
            if (_manageTagsDebounceTimer != null)
                return;

            _manageTagsDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ManageTagsDebounceMs)
            };
            _manageTagsDebounceTimer.Tick += (_, __) =>
            {
                _manageTagsDebounceTimer.Stop();

                List<string> paths;
                lock (PendingManageTagPaths)
                {
                    paths = PendingManageTagPaths
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    PendingManageTagPaths.Clear();
                }

                OpenManageTagsWindow(paths);
                _manageTagsSessionEnded?.Invoke();
            };
        }

        private static void OpenManageTagsWindow(IEnumerable<string> filePaths)
        {
            var processedPaths = ResolveManageTagPaths(filePaths);
            if (processedPaths.Count == 0)
            {
                CliConsole.Show(
                    "File Tagger",
                    "Selected files are not in a watched directory.\n\n" +
                    "Add the folder to File Tagger (Settings tab) and try again.");
                return;
            }

            var snapshot = DatabaseManager.Instance.GetManageTagsSnapshot(processedPaths);
            var tagWindow = new TagManagementWindow(processedPaths, snapshot);

            var previousWindow = _openManageTagsWindow;
            _openManageTagsWindow = tagWindow;
            try
            {
                tagWindow.ShowDialog();
            }
            finally
            {
                _openManageTagsWindow = previousWindow;
            }
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
                        CliConsole.Show(
                            "File Tagger Error",
                            "Cannot find original file location for this temporary file.",
                            MessageBoxImage.Warning);
                        return true;
                    }
                }

                var tags = DatabaseManager.Instance.GetTagsForFile(originalFilePath);

                if (!tags.Any())
                {
                    CliConsole.Show(
                        "File Tags",
                        $"No tags found for:\n{Path.GetFileName(originalFilePath)}{displayNote}");
                }
                else
                {
                    var tagsText = string.Join("\n• ", tags);
                    CliConsole.Show(
                        "File Tags",
                        $"Tags for {Path.GetFileName(originalFilePath)}:\n\n• {tagsText}{displayNote}");
                }

                return true;
            }
            catch (Exception ex)
            {
                CliConsole.ShowError("File Tagger Error", "Error viewing tags.", ex);
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
                CliConsole.ShowError("File Tagger Error", "Error filtering directory.", ex);
                return true;
            }
        }
    }
}
