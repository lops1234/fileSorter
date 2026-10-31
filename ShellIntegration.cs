using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace FileTagger
{
    public static class ShellIntegration
    {
        private const string SHELL_KEY = @"*\shell";
        private const string ADD_TAG_KEY = "FileTagger.AddTag";
        private const string VIEW_TAGS_KEY = "FileTagger.ViewTags";
        private const string FILTER_BY_TAGS_KEY = "FileTagger.FilterByTags";
        
        private const string DropTargetClassKey = @"Software\Classes\CLSID\" + ShellDropTarget.ManageTagsClsid;
        private const string DropTargetServerKey = DropTargetClassKey + @"\LocalServer32";

        private static string _lastRegisteredExecutablePath = string.Empty;

        public static void RegisterContextMenu()
        {
            try
            {
                var executablePath = GetExecutablePath();
                
                // Validate executable path exists
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Executable path not found or invalid: {executablePath}");
                    return;
                }
                
                // If path hasn't changed and menus point at this executable, skip
                if (_lastRegisteredExecutablePath == executablePath && AreContextMenusRegistered(executablePath))
                {
                    return;
                }

                // Unregister any existing entries first to ensure clean state
                UnregisterContextMenu();

                // Register "Add/Manage Tags" context menu
                using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}"))
                {
                    key?.SetValue("", "Manage Tags");
                    key?.SetValue("AppliesTo", "*"); // Ensure it applies to all files
                    key?.SetValue("MultiSelectModel", "Player");
                }

                using (var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}\command"))
                {
                    commandKey?.SetValue("", $"\"{executablePath}\" --manage-tags \"%1\"");
                }

                // DropTarget takes precedence over "command": the verb stays visible for any selection
                // size and Explorer hands every selected file to a single process.
                using (var serverKey = Registry.CurrentUser.CreateSubKey(DropTargetServerKey))
                {
                    serverKey?.SetValue("", $"\"{executablePath}\"");
                }

                using (var dropTargetKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}\DropTarget"))
                {
                    dropTargetKey?.SetValue("CLSID", ShellDropTarget.ManageTagsClsid);
                }

                // Register "View Tags" context menu
                using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{VIEW_TAGS_KEY}"))
                {
                    key?.SetValue("", "View File Tags");
                    key?.SetValue("AppliesTo", "*"); // Ensure it applies to all files
                }

                using (var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{VIEW_TAGS_KEY}\command"))
                {
                    commandKey?.SetValue("", $"\"{executablePath}\" --view-tags \"%1\"");
                }

                // Register for directories as well
                RegisterDirectoryContextMenu(executablePath);
                
                _lastRegisteredExecutablePath = executablePath;
                
                // Force Windows to refresh the shell
                RefreshShell();
            }
            catch (Exception ex)
            {
                // Log error but don't fail application startup
                System.Diagnostics.Debug.WriteLine($"Failed to register context menu: {ex.Message}");
            }
        }

        /// <summary>
        /// Force refresh the context menus - useful after adding/removing directories
        /// </summary>
        public static void RefreshContextMenus()
        {
            try
            {
                // Force re-registration
                _lastRegisteredExecutablePath = string.Empty;
                RegisterContextMenu();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to refresh context menus: {ex.Message}");
            }
        }

        private const string AppExeName = "FileTagger.exe";

        private static string GetExecutablePath()
        {
            // Environment.ProcessPath is FileTagger.exe for published/direct runs, but dotnet.exe when
            // launched via "dotnet run" or the VS debugger — never register the host as the shell target.
            var processPath = Environment.ProcessPath;
            if (IsFileTaggerExecutable(processPath))
                return Path.GetFullPath(processPath!);

            var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, AppExeName);
            if (File.Exists(baseDirCandidate))
                return Path.GetFullPath(baseDirCandidate);

            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(assemblyLocation))
            {
                var assemblyDir = Path.GetDirectoryName(assemblyLocation);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    var assemblyDirCandidate = Path.Combine(assemblyDir, AppExeName);
                    if (File.Exists(assemblyDirCandidate))
                        return Path.GetFullPath(assemblyDirCandidate);
                }
            }

            return File.Exists(baseDirCandidate)
                ? Path.GetFullPath(baseDirCandidate)
                : processPath ?? baseDirCandidate;
        }

        private static bool IsFileTaggerExecutable(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            return Path.GetFileName(path).Equals(AppExeName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AreContextMenusRegistered(string executablePath)
        {
            try
            {
                using var addTagKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}\command");
                using var viewTagKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{SHELL_KEY}\{VIEW_TAGS_KEY}\command");
                using var filterKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Directory\shell\{FILTER_BY_TAGS_KEY}\command");
                using var dropTargetKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}\DropTarget");
                using var serverKey = Registry.CurrentUser.OpenSubKey(DropTargetServerKey);

                if (addTagKey == null || viewTagKey == null || filterKey == null || dropTargetKey == null || serverKey == null)
                    return false;

                var addTagCommand = addTagKey.GetValue("") as string;
                var viewTagCommand = viewTagKey.GetValue("") as string;
                var filterCommand = filterKey.GetValue("") as string;
                var serverCommand = serverKey.GetValue("") as string;

                return CommandTargetsExecutable(addTagCommand, executablePath)
                    && CommandTargetsExecutable(viewTagCommand, executablePath)
                    && CommandTargetsExecutable(filterCommand, executablePath)
                    && CommandTargetsExecutable(serverCommand, executablePath)
                    && string.Equals(dropTargetKey.GetValue("CLSID") as string, ShellDropTarget.ManageTagsClsid, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool CommandTargetsExecutable(string command, string executablePath)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var quotedExecutable = $"\"{executablePath}\"";
            return command.StartsWith(quotedExecutable, StringComparison.OrdinalIgnoreCase);
        }

        private static void RefreshShell()
        {
            try
            {
                // Notify Windows that file associations have changed
                const uint SHCNE_ASSOCCHANGED = 0x08000000;
                const uint SHCNF_IDLIST = 0x0000;
                
                // Use SHChangeNotify to refresh shell
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // If shell refresh fails, it's not critical
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private static void RegisterDirectoryContextMenu(string executablePath)
        {
            try
            {
                // Register context menu for directories
                using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Directory\shell\{FILTER_BY_TAGS_KEY}"))
                {
                    key?.SetValue("", "Filter Files by Tags");
                }

                using (var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Directory\shell\{FILTER_BY_TAGS_KEY}\command"))
                {
                    commandKey?.SetValue("", $"\"{executablePath}\" --filter-directory \"%1\"");
                }

                // Also register for directory background (right-click in empty space)
                using (var backgroundKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Directory\Background\shell\{FILTER_BY_TAGS_KEY}"))
                {
                    backgroundKey?.SetValue("", "Filter Files by Tags (Current Directory)");
                }

                using (var backgroundCommandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Directory\Background\shell\{FILTER_BY_TAGS_KEY}\command"))
                {
                    backgroundCommandKey?.SetValue("", $"\"{executablePath}\" --filter-directory \"%V\"");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register directory context menu: {ex.Message}");
            }
        }

        /// <summary>
        /// Completely remove all context menu entries - use only for uninstallation
        /// </summary>
        public static void UnregisterContextMenu()
        {
            try
            {
                // Remove file context menus
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}", false);
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{SHELL_KEY}\{VIEW_TAGS_KEY}", false);
                Registry.CurrentUser.DeleteSubKeyTree(DropTargetClassKey, false);
                
                // Remove directory context menus
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\shell\{FILTER_BY_TAGS_KEY}", false);
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\Background\shell\{FILTER_BY_TAGS_KEY}", false);
                
                // Reset last registered path so they can be re-registered if needed
                _lastRegisteredExecutablePath = string.Empty;
                
                // Refresh shell to remove the menus immediately
                RefreshShell();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to unregister context menu: {ex.Message}");
            }
        }

        public static bool IsInWatchedDirectory(string filePath)
        {
            try
            {
                var watchedDirs = FileTagger.Services.DatabaseManager.Instance.GetAllActiveDirectories();
                var fileDir = Path.GetDirectoryName(filePath);
                return watchedDirs.Any(wd => fileDir?.StartsWith(wd, StringComparison.OrdinalIgnoreCase) == true);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get diagnostic information about context menu registration
        /// </summary>
        public static string GetDiagnosticInfo()
        {
            try
            {
                var executablePath = GetExecutablePath();
                var isRegistered = AreContextMenusRegistered(executablePath);
                var fileExists = File.Exists(executablePath);
                
                return $"Executable Path: {executablePath}\n" +
                       $"File Exists: {fileExists}\n" +
                       $"Context Menus Registered: {isRegistered}\n" +
                       $"Last Registered Path: {_lastRegisteredExecutablePath}";
            }
            catch (Exception ex)
            {
                return $"Error getting diagnostic info: {ex.Message}";
            }
        }
    }
}
