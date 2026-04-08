using System;
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
            // Auto-login if a valid saved session exists
            var savedUserId = SessionService.LoadSession();
            if (savedUserId.HasValue)
            {
                var savedUser = AuthService.GetUserById(savedUserId.Value);
                if (savedUser != null && savedUser.Status == "Active" && AuthService.LoginById(savedUserId.Value))
                {
                    ShowMainWindow(desktop, null);
                    return;
                }
                // Session invalid or user disabled — clear it
                SessionService.ClearSession();
            }

            var loginWindow = new LoginWindow();
            loginWindow.OnLoginSuccess += () => ShowMainWindow(desktop, loginWindow);
            desktop.MainWindow = loginWindow;
            loginWindow.Show();
        }

        private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop, LoginWindow? loginWindow)
        {
            var mainWindow = new MainWindow();
            mainWindow.OnLogoutRequested += () =>
            {
                SessionService.ClearSession();
                ShowLogin(desktop);
                mainWindow.Close();
            };
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            loginWindow?.Close();
        }
    }
}
