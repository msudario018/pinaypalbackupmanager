using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PinayPalBackupManager.Services;
using PinayPalBackupManager.UI;
using System.Threading.Tasks;

namespace PinayPalBackupManager
{
    public partial class App : Application
    {
        public override async void Initialize()
        {
            // Global crash handler: log any unhandled exception directly to disk synchronously so the app does not silently die
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [FATAL] Unhandled exception: {e.ExceptionObject}\n";
                    try { File.AppendAllText(AppDataPaths.SystemLogPath, msg); } catch { }
                    try { File.AppendAllText(AppDataPaths.GetLogPath("startup.log"), msg); } catch { }
                    try { LogService.Flush(); } catch { }
                }
                catch { }
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try
                {
                    var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [FATAL] Unobserved task exception: {e.Exception}\n";
                    try { File.AppendAllText(AppDataPaths.SystemLogPath, msg); } catch { }
                    try { LogService.Flush(); } catch { }
                }
                catch { }
                e.SetObserved();
            };

            // XAML is loaded automatically by Avalonia 11
            
            // Initialize environment configuration
            EnvironmentConfigService.Initialize();
            
            // Initialize authentication service
            await AuthService.InitializeAsync();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Initialize security enhancements
            InitializeSecurity();
            
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Must be OnExplicitShutdown: prevents Avalonia from terminating when MainWindow is hidden/minimized to tray or when modal/update dialogs close
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

                // If there are no users in database, launch the Setup Wizard
                if (!AuthService.HasAnyUsers())
                {
                    ShowSetupWizard(desktop);
                }
                else
                {
                    // Existing users in database: ensure setup is marked complete so first-run flags align
                    if (ConfigService.IsFirstRun())
                    {
                        ConfigService.MarkSetupComplete();
                    }
                    ShowLogin(desktop);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void InitializeSecurity()
        {
            try
            {
                // Check if configuration needs encryption
                if (!SecurityService.IsConfigurationEncrypted())
                {
                    SecurityService.EncryptSensitiveConfiguration();
                }
                
                // Initialize HTTP client factory (creates instance with connection pooling)
                _ = HttpClientFactory.Instance;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[App] Security initialization failed: {ex.Message}", "Error", "SYSTEM");
            }
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
            
            // Start minimized if configured and auto-logged in
            if (ConfigService.Current.Operation.StartMinimized && loginWindow == null)
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
                mainWindow.ShowInTaskbar = true;
            }
            
            loginWindow?.Close();
        }

        private void ShowSetupWizard(IClassicDesktopStyleApplicationLifetime desktop)
        {
            var setupWizard = new SetupWizardWindow();
            setupWizard.OnSetupComplete += () =>
            {
                if (AuthService.CurrentUser != null)
                    ShowMainWindow(desktop, null);
                else
                    ShowLogin(desktop);
            };
            desktop.MainWindow = setupWizard;
            setupWizard.Show();
        }
    }
}
