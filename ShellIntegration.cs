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
        
        private static string _lastRegisteredExecutablePath = string.Empty;

        public static void RegisterContextMenu()
        {
            try
            {
                var executablePath = GetExecutablePath();
                
                // If path hasn't changed and menus are already registered, skip
                if (_lastRegisteredExecutablePath == executablePath && AreContextMenusRegistered())
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
                }

                using (var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}\command"))
                {
                    commandKey?.SetValue("", $"\"{executablePath}\" --manage-tags \"%1\"");
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

        private static string GetExecutablePath()
        {
            var executablePath = Assembly.GetExecutingAssembly().Location;
            if (executablePath.EndsWith(".dll"))
            {
                executablePath = executablePath.Replace(".dll", ".exe");
            }
            return executablePath;
        }

        private static bool AreContextMenusRegistered()
        {
            try
            {
                using var addTagKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}");
                using var viewTagKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{SHELL_KEY}\{VIEW_TAGS_KEY}");
                using var filterKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Directory\shell\{FILTER_BY_TAGS_KEY}");
                
                return addTagKey != null && viewTagKey != null && filterKey != null;
            }
            catch
            {
                return false;
            }
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
    }
}
