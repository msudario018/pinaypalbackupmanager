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
                var mainWindow = new MainWindow();

                mainWindow.OnLogoutRequested += () =>
                {
                    AuthService.Logout();
                    // Show login first, then close main to prevent app shutdown
                    ShowLogin(desktop);
                    mainWindow.Close();
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
