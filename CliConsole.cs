using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace FileTagger
{
    public static class CliConsole
    {
        private static bool _attached;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        public static void Attach()
        {
            if (_attached)
                return;

            if (AllocConsole())
            {
                _attached = true;
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.Title = "File Tagger";
            }
        }

        public static void WriteArgs(string[] args)
        {
            if (!_attached)
                return;

            Console.WriteLine("FileTagger CLI");
            Console.WriteLine($"Args ({args.Length}): {string.Join(" | ", args)}");
            Console.WriteLine();
        }

        public static void Show(string title, string message, MessageBoxImage icon = MessageBoxImage.Information)
        {
            WriteToConsole(title, message);

            MessageBox.Show(message, title, MessageBoxButton.OK, icon);

            if (_attached && ShouldWaitBeforeClose(icon))
                WaitForEnter();
        }

        public static void ShowError(string title, string message, Exception ex = null)
        {
            var consoleMessage = message;
            if (ex != null)
                consoleMessage += $"\n\n{FormatException(ex)}";

            WriteToConsole(title, consoleMessage);

            var dialogMessage = ex == null ? message : $"{message}\n\n{ex.Message}";
            MessageBox.Show(dialogMessage, title, MessageBoxButton.OK, MessageBoxImage.Error);

            if (_attached)
                WaitForEnter();
        }

        private static void WriteToConsole(string title, string message)
        {
            if (!_attached)
                return;

            Console.WriteLine($"=== {title} ===");
            Console.WriteLine(message);
            Console.WriteLine();
        }

        private static string FormatException(Exception ex)
        {
            return $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}";
        }

        private static bool ShouldWaitBeforeClose(MessageBoxImage icon)
        {
            return icon == MessageBoxImage.Error || icon == MessageBoxImage.Warning;
        }

        private static void WaitForEnter()
        {
            Console.WriteLine("Press Enter to close...");
            Console.ReadLine();
        }
    }
}
