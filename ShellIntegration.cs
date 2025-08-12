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

        public static void RegisterContextMenu()
        {
            try
            {
                var executablePath = Assembly.GetExecutingAssembly().Location;
                if (executablePath.EndsWith(".dll"))
                {
                    executablePath = executablePath.Replace(".dll", ".exe");
                }

                // Register "Add/Manage Tags" context menu
                using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}"))
                {
                    key?.SetValue("", "Manage Tags");
                }

                using (var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}\command"))
                {
                    commandKey?.SetValue("", $"\"{executablePath}\" --manage-tags \"%1\"");
                }

                // Register "View Tags" context menu
                using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{VIEW_TAGS_KEY}"))
                {
                    key?.SetValue("", "View File Tags");
                }

                using (var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SHELL_KEY}\{VIEW_TAGS_KEY}\command"))
                {
                    commandKey?.SetValue("", $"\"{executablePath}\" --view-tags \"%1\"");
                }

                // Register for directories as well
                RegisterDirectoryContextMenu(executablePath);
            }
            catch (Exception ex)
            {
                // Log error but don't fail application startup
                System.Diagnostics.Debug.WriteLine($"Failed to register context menu: {ex.Message}");
            }
        }

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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register directory context menu: {ex.Message}");
            }
        }

        public static void UnregisterContextMenu()
        {
            try
            {
                // Remove file context menus
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{SHELL_KEY}\{ADD_TAG_KEY}", false);
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{SHELL_KEY}\{VIEW_TAGS_KEY}", false);
                
                // Remove directory context menu
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\shell\{FILTER_BY_TAGS_KEY}", false);
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
