using System;
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
            // Global crash handler: log any unhandled exception so the app does not silently die
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { LogService.WriteSystemLog($"[FATAL] Unhandled exception: {e.ExceptionObject}", "Error", "SYSTEM"); } catch { }
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try { LogService.WriteSystemLog($"[FATAL] Unobserved task exception: {e.Exception}", "Error", "SYSTEM"); } catch { }
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
                // Check if this is the first run
                if (ConfigService.IsFirstRun())
                {
                    // If users were pulled from Firebase on a fresh install, show login
                    // Setup wizard will be shown after first login if needed
                    if (AuthService.HasAnyUsers())
                    {
                        ShowLogin(desktop);
                    }
                    else
                    {
                        ShowSetupWizard(desktop);
                    }
                }
                else
                {
                    ShowLogin(desktop);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void InitializeSecurity()
        {
            try
            {
                // Initialize configuration encryption
                Console.WriteLine("[App] Initializing security enhancements...");
                
                // Check if configuration needs encryption
                if (!SecurityService.IsConfigurationEncrypted())
                {
                    Console.WriteLine("[App] Encrypting sensitive configuration data...");
                    SecurityService.EncryptSensitiveConfiguration();
                }
                else
                {
                    Console.WriteLine("[App] Configuration already encrypted");
                }
                
                // Initialize HTTP client factory
                var httpClientFactory = HttpClientFactory.Instance;
                Console.WriteLine("[App] HTTP client factory initialized with connection pooling");
                
                Console.WriteLine("[App] Security initialization completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Security initialization failed: {ex.Message}");
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
                    if (ConfigService.IsFirstRun())
                    {
                        ShowSetupWizardPostLogin(desktop);
                    }
                    else
                    {
                        ShowMainWindow(desktop, null);
                    }
                    return;
                }
                // Session invalid or user disabled — clear it
                SessionService.ClearSession();
            }

            var loginWindow = new LoginWindow();
            loginWindow.OnLoginSuccess += () =>
            {
                if (ConfigService.IsFirstRun())
                {
                    ShowSetupWizardPostLogin(desktop);
                }
                else
                {
                    ShowMainWindow(desktop, loginWindow);
                }
            };
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

        private void ShowSetupWizardPostLogin(IClassicDesktopStyleApplicationLifetime desktop)
        {
            var setupWizard = new SetupWizardWindow();
            setupWizard.SetPostLoginMode();
            setupWizard.OnSetupComplete += () =>
            {
                ShowMainWindow(desktop, null);
            };
            desktop.MainWindow = setupWizard;
            setupWizard.Show();
        }
    }
}
