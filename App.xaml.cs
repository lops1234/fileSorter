using System.Windows;
using FileTagger.Data;

namespace FileTagger
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Handle command line arguments first
            if (e.Args.Length > 0)
            {
                if (CommandLineHandler.HandleCommandLineArgs(e.Args))
                {
                    // Command was handled, shut down the application
                    Shutdown();
                    return;
                }
            }

            // Initialize database
            using var context = new FileTagContext();
            context.Database.EnsureCreated();

            // Register shell context menu
            ShellIntegration.RegisterContextMenu();

            // Show main window if no command line args were processed
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Cleanup shell integration
            ShellIntegration.UnregisterContextMenu();
            base.OnExit(e);
        }
    }
}
