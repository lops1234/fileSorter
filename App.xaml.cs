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

            // Handle command line arguments first
            if (e.Args.Length > 0)
            {
                // This is a command line instance - don't register/unregister context menus
                // Just initialize the database manager for command line operations
                DatabaseManager.Instance.Initialize();
                
                if (CommandLineHandler.HandleCommandLineArgs(e.Args))
                {
                    // Command was handled, shut down the application
                    Shutdown();
                    return;
                }
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

        protected override void OnExit(ExitEventArgs e)
        {
            // Only cleanup shell integration when the main application exits
            // Command line instances should NOT remove the context menus
            if (_isMainApplication)
            {
                ShellIntegration.UnregisterContextMenu();
                
                // Release the mutex
                _applicationMutex?.ReleaseMutex();
                _applicationMutex?.Dispose();
            }
            base.OnExit(e);
        }
    }
}
