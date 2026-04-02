using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PinayPalBackupManager.Services;
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
                ShowLogin(desktop);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void ShowLogin(IClassicDesktopStyleApplicationLifetime desktop)
        {
            var loginWindow = new LoginWindow();
            loginWindow.OnLoginSuccess += () =>
            {
                var backupManager = new Services.BackupManager();
                var mainWindow = new MainWindow
                {
                    DataContext = new UI.ViewModels.FtpViewModel(backupManager)
                };

                mainWindow.OnLogoutRequested += () =>
                {
                    mainWindow.Close();
                    ShowLogin(desktop);
                };

                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                loginWindow.Close();
            };

            desktop.MainWindow = loginWindow;
            loginWindow.Show();
        }
    }
}
