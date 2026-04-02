using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PinayPalBackupManager.UI;

namespace PinayPalBackupManager
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Create services and view models for DI
                var backupManager = new Services.BackupManager();

                // Create main window and inject services
                var mainWindow = new MainWindow
                {
                    // Provide backupManager to main window via property for now
                    DataContext = new UI.ViewModels.FtpViewModel(backupManager)
                };

                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
