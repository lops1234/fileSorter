using System;
using System.Windows;
using System.Threading;
using FileTagger.Services;

namespace FileTagger
{
    public partial class App : Application
    {
        private bool _isMainApplication = false;
        private static Mutex _applicationMutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Handle command line arguments first (Explorer context menu, etc.)
            if (e.Args.Length > 0)
            {
                if (string.Equals(Environment.GetEnvironmentVariable("FILETAGGER_DEBUG"), "1", StringComparison.Ordinal))
                {
                    CliConsole.Attach();
                    CliConsole.WriteArgs(e.Args);
                }

                DispatcherUnhandledException += OnCliDispatcherUnhandledException;

                if (ShellDropTarget.IsEmbeddingLaunch(e.Args))
                {
                    StartShellDropTargetServer();
                    return;
                }

                try
                {
                    if (!CommandLineHandler.HandleCommandLineArgs(e.Args))
                    {
                        CliConsole.Show(
                            "File Tagger",
                            "File Tagger could not run the requested action.\n\n" +
                            string.Join("\n", e.Args),
                            MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    CliConsole.ShowError("File Tagger startup error", "An unexpected error occurred.", ex);
                }

                Shutdown();
                return;
            }

            // This is the main application instance
            _isMainApplication = true;

            // Create mutex to track that main application is running
            bool createdNew;
            _applicationMutex = new Mutex(true, "FileTagger_MainApplication", out createdNew);
            
            if (!createdNew)
            {
                // Another main instance is already running
                MessageBox.Show("File Tagger is already running. Check the system tray.", 
                    "File Tagger", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Initialize distributed database system
            DatabaseManager.Instance.Initialize();

            // Register shell context menu (only for main application)
            ShellIntegration.RegisterContextMenu();

            // Show main window if no command line args were processed
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private void StartShellDropTargetServer()
        {
            try
            {
                ShellDropTarget.RunServer(
                    files =>
                    {
                        try
                        {
                            if (files.Count > 0)
                            {
                                var args = new string[files.Count + 1];
                                args[0] = "--manage-tags";
                                files.CopyTo(args, 1);
                                CommandLineHandler.HandleCommandLineArgs(args);
                            }
                        }
                        catch (Exception ex)
                        {
                            CliConsole.ShowError("File Tagger error", "Could not open Manage Tags.", ex);
                        }
                        finally
                        {
                            Shutdown();
                        }
                    },
                    Shutdown);
            }
            catch (Exception ex)
            {
                CliConsole.ShowError("File Tagger startup error", "Could not start the Explorer integration.", ex);
                Shutdown();
            }
        }

        private void OnCliDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            CliConsole.Attach();
            CliConsole.ShowError("File Tagger error", "An unexpected error occurred.", e.Exception);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
    {
        // Only cleanup shell integration when the main application exits
        // Command line instances should NOT remove the context menus
        if (_isMainApplication)
        {
            ShellIntegration.UnregisterContextMenu();
            
            // Clean up temporary search results directory
            Services.TempResultsManager.Instance.CleanupTempDirectory();
            
            // Release the mutex
            _applicationMutex?.ReleaseMutex();
            _applicationMutex?.Dispose();
        }
        base.OnExit(e);
    }
    }
}
