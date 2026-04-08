using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Firebase.Database;
using Firebase.Database.Query;
using PinayPalBackupManager.Services;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.UI.UserControls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI
{
    public partial class MainWindow : Window
    {
        private readonly BackupManager _backupManager;
        private readonly HomeControl _homeControl;
        private readonly FtpControl _ftpControl;
        private readonly MailchimpControl _mailchimpControl;
        private readonly SqlControl _sqlControl;
        private readonly SettingsControl _settingsControl;
        private readonly ProfileControl _profileControl;
        private DispatcherTimer? _activeProcessMonitorTimer;
        private bool _allowClose;
        private DispatcherTimer? _toastTimer;
        private IBrush _activeTabAccentBrush = Brush.Parse("#52B788");
        private bool _startupHealthPending = true;
        private bool _configRequired;
        public event Action? OnLogoutRequested;

        public MainWindow()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            _backupManager = new BackupManager();
            _backupManager.OnTimeUpdate += UpdateTime;
            _backupManager.OnHealthUpdate += UpdateHealthStatus;
            NotificationService.OnToast += HandleToast;

            WindowStateService.Restore(this);

            ThemeService.Load();
            UpdateThemeIcon();
            ThemeService.OnThemeChanged += _ => UpdateThemeIcon();

            var btnTheme = this.FindControl<Button>("BtnThemeToggle");
            if (btnTheme != null) btnTheme.Click += (_, _) => { ThemeService.Toggle(); UpdateThemeIcon(); };

            var btnCustomize = this.FindControl<Button>("BtnCustomizeTabs");
            if (btnCustomize != null) 
            {
                btnCustomize.Click += async (_, _) => await OpenTabOrderDialogAsync();
                // Hide during startup health check
                btnCustomize.IsVisible = !_startupHealthPending;
            }

            var btnBell = this.FindControl<Button>("BtnNotificationCenter");
            if (btnBell != null) btnBell.Click += (_, _) => ToggleNotificationCenter();
            var btnClearNotif = this.FindControl<Button>("BtnClearNotifications");
            if (btnClearNotif != null) btnClearNotif.Click += (_, _) => { NotificationHistoryService.ClearAll(); PopulateNotificationCenter(); UpdateBellBadge(); };

            NotificationHistoryService.OnNewNotification += () => Dispatcher.UIThread.Post(() => { UpdateBellBadge(); if (_notifCenterOpen) PopulateNotificationCenter(); });

            SetupSystemTray();

            // Handle window state changes for layout optimization
            this.GetObservable(Window.WindowStateProperty).Subscribe(OnWindowStateChanged);
            OnWindowStateChanged(this.WindowState);

            _homeControl = new HomeControl(_backupManager);
            _homeControl.OnNavigateFtp += () => { ShowControl(_ftpControl!); UpdateSidebarSelection("FTP"); };
            _homeControl.OnNavigateMailchimp += () => { ShowControl(_mailchimpControl!); UpdateSidebarSelection("Mailchimp"); };
            _homeControl.OnNavigateSql += () => { ShowControl(_sqlControl!); UpdateSidebarSelection("SQL"); };
            _homeControl.OnRunAllChecks += () => _ = RunAllChecksAsync();
            _homeControl.OnFtpSyncCheck += () => { ShowControl(_ftpControl!); UpdateSidebarSelection("FTP"); _ftpControl?.PerformSyncCheck(); };
            _homeControl.OnFtpQuickBackup += () => { ShowControl(_ftpControl!); UpdateSidebarSelection("FTP"); _ftpControl?.StartBackupFromShell(); };
            _homeControl.OnMailchimpSyncCheck += () => { ShowControl(_mailchimpControl!); UpdateSidebarSelection("Mailchimp"); _mailchimpControl?.PerformSyncCheck(); };
            _homeControl.OnMailchimpQuickBackup += () => { ShowControl(_mailchimpControl!); UpdateSidebarSelection("Mailchimp"); _mailchimpControl?.StartFullBackupFromShell(); };
            _homeControl.OnSqlSyncCheck += () => { ShowControl(_sqlControl!); UpdateSidebarSelection("SQL"); _sqlControl?.PerformSyncCheck(); };
            _homeControl.OnSqlQuickBackup += () => { ShowControl(_sqlControl!); UpdateSidebarSelection("SQL"); _sqlControl?.StartBackupFromShell(); };
            _homeControl.OnEmergencyStop += () =>
            {
                if (_ftpControl?.IsBusy == true) _ftpControl.RequestCancelFromShell();
                if (_mailchimpControl?.IsBusy == true) _mailchimpControl.RequestCancelFromShell();
                if (_sqlControl?.IsBusy == true) _sqlControl.RequestCancelFromShell();
                NotificationService.ShowBackupToast("Emergency Stop", "All running tasks cancelled.", "Warning");
            };
            _ftpControl = new FtpControl(_backupManager);
            _mailchimpControl = new MailchimpControl(_backupManager);
            _sqlControl = new SqlControl(_backupManager);
            _settingsControl = new SettingsControl(_backupManager);
            _settingsControl.OnShowSystemInfo += ShowSystemInfoAsync;
            _profileControl = new ProfileControl();
            _profileControl.OnAvatarChanged += LoadSidebarAvatar;
            _profileControl.OnLogoutRequested += () => {
                _allowClose = true;
                AuthService.Logout();
                OnLogoutRequested?.Invoke();
            };
            _settingsControl.OnCheckUpdates += async () => await UpdateService.CheckForUpdatesWithUiAsync();

            // Start active process monitor
            _activeProcessMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _activeProcessMonitorTimer.Tick += UpdateActiveProcessCount;
            _activeProcessMonitorTimer.Start();
            _settingsControl.OnConfigSaved += () => SetConfigRequiredMode(!ConfigService.IsConfigured());

            // Setup button click handlers
            foreach (var btn in this.FindControl<StackPanel>("Sidebar")?.Children ?? [])
            {
                if (btn is Button button)
                {
                    button.Click += SidebarButton_Click;
                }
            }

            var btnSysInfo = this.FindControl<Button>("BtnSystemInfo");
            if (btnSysInfo != null)
            {
                btnSysInfo.Click += async (s, e) =>
                {
                    ShowControl(_settingsControl);
                    await ShowSystemInfoAsync();
                };
            }

            // Initialize profile section
            InitializeProfileSection();

            SetStartupBusy(true);
            
            // Hide entire sidepanel during startup for cleaner loading experience
            var sidepanelBorder = this.FindControl<Border>("SidepanelBorder");
            if (sidepanelBorder != null) sidepanelBorder.IsVisible = false;
            
            // Expand main content to full width during startup
            var mainGrid = this.FindControl<Grid>("MainGrid");
            if (mainGrid != null)
            {
                mainGrid.ColumnDefinitions.Clear();
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) }); // Hidden sidepanel
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Full width content
            }
            
            // Disable notifications during startup
            NotificationService.DisableNotifications();
            
            // Show startup notification (this will be logged but not displayed)
            NotificationService.ShowBackupToast("Startup", "Running health scan...", "Info");

            if (!ConfigService.IsConfigured())
            {
                NotificationService.ShowBackupToast("Config", "Missing appsettings.local.json values. Please configure credentials first.", "Warning");
                SetConfigRequiredMode(true);
            }
            else
            {
                ShowControl(_homeControl);
                UpdateSidebarSelection("Home");
            }

            _backupManager.Start();

            // Set version dynamically from assembly
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            var versionStr = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v?.?.?";
            var txtVer = this.FindControl<TextBlock>("TxtVersionBadge");
            if (txtVer != null) txtVer.Text = versionStr;

            if (UpdatePreferences.LoadAutoCheckOnStartup())
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    await UpdateService.CheckForUpdatesWithUiAsync(silentIfNone: true);
                });
            }
            
            // Handle window closing event
            this.Closing += async (sender, e) =>
            {
                try
                {
                    LogService.WriteSystemLog("[MAINWINDOW] Starting graceful shutdown...", "Information", "SYSTEM");
                    await RealtimeMonitoringService.AddLogAsync("Info", "Starting graceful shutdown of all services", "MAINWINDOW");
                    
                    // Stop all running backup operations first
                    if (_ftpControl?.IsBusy == true)
                    {
                        LogService.WriteSystemLog("[MAINWINDOW] Stopping FTP operations...", "Information", "SYSTEM");
                        await RealtimeMonitoringService.AddLogAsync("Info", "Stopping FTP operations...", "MAINWINDOW");
                        _ftpControl.RequestCancelFromShell();
                        await Task.Delay(1000); // Allow time for graceful cancellation
                    }
                    
                    if (_mailchimpControl?.IsBusy == true)
                    {
                        LogService.WriteSystemLog("[MAINWINDOW] Stopping Mailchimp operations...", "Information", "SYSTEM");
                        await RealtimeMonitoringService.AddLogAsync("Info", "Stopping Mailchimp operations...", "MAINWINDOW");
                        _mailchimpControl.RequestCancelFromShell();
                        await Task.Delay(1000); // Allow time for graceful cancellation
                    }
                    
                    if (_sqlControl?.IsBusy == true)
                    {
                        LogService.WriteSystemLog("[MAINWINDOW] Stopping SQL operations...", "Information", "SYSTEM");
                        await RealtimeMonitoringService.AddLogAsync("Info", "Stopping SQL operations...", "MAINWINDOW");
                        _sqlControl.RequestCancelFromShell();
                        await Task.Delay(1000); // Allow time for graceful cancellation
                    }
                    
                    // Stop real-time monitoring service
                    RealtimeMonitoringService.Stop();
                    
                    // Stop backup manager
                    _backupManager?.Stop();
                    
                    // Stop active process monitor timer
                    _activeProcessMonitorTimer?.Stop();
                    
                    // Stop toast timer
                    _toastTimer?.Stop();
                    
                    // Update connection status to offline
                    if (AuthService.CurrentUser?.Username != null)
                    {
                        try
                        {
                            var databaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
                            var database = new FirebaseClient(databaseUrl);
                            await database
                                .Child("users")
                                .Child(AuthService.CurrentUser.Username)
                                .Child("connection")
                                .PatchAsync(new 
                                { 
                                    status = "offline", 
                                    lastSeen = DateTime.UtcNow.ToString("o"),
                                    appShutdown = true
                                });
                                
                            await RealtimeMonitoringService.AddLogAsync("Info", "Connection status updated to offline", "MAINWINDOW");
                        }
                        catch (Exception ex)
                        {
                            LogService.WriteSystemLog($"[MAINWINDOW] Failed to update offline status: {ex.Message}", "Error", "SYSTEM");
                            await RealtimeMonitoringService.AddLogAsync("Error", $"Failed to update offline status: {ex.Message}", "MAINWINDOW");
                        }
                    }
                    
                    LogService.WriteSystemLog("[MAINWINDOW] All services stopped successfully", "Information", "SYSTEM");
                    await RealtimeMonitoringService.AddLogAsync("Info", "All services stopped successfully", "MAINWINDOW");
                    await RealtimeMonitoringService.AddLogAsync("Info", "PC application shutdown completed", "MAINWINDOW");
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[MAINWINDOW] Error during shutdown: {ex.Message}", "Error", "SYSTEM");
                    await RealtimeMonitoringService.AddLogAsync("Error", $"Error during shutdown: {ex.Message}", "MAINWINDOW");
                }
            };
            
            // Initialize Firebase remote control only
            LogService.WriteSystemLog("[MAINWINDOW] About to initialize Firebase remote control...", "Information", "SYSTEM");
            
            // Initialize Firebase remote control
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000); // Wait for UI to fully load
                LogService.WriteSystemLog("[MAINWINDOW] Starting Firebase remote control initialization...", "Information", "SYSTEM");
                await InitializeFirebaseRemoteControl();
                LogService.WriteSystemLog("[MAINWINDOW] Firebase remote control initialization completed", "Information", "SYSTEM");
            });
        }

        private async Task InitializeFirebaseRemoteControl()
        {
            try
            {
                LogService.WriteSystemLog("[MAINWINDOW] InitializeFirebaseRemoteControl started", "Information", "SYSTEM");
                await RealtimeMonitoringService.AddLogAsync("Info", "InitializeFirebaseRemoteControl started", "MAINWINDOW");
                
                var databaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
                var username = AuthService.CurrentUser?.Username;
                
                LogService.WriteSystemLog($"[MAINWINDOW] Username check: {username ?? "NULL"}", "Information", "SYSTEM");
                await RealtimeMonitoringService.AddLogAsync("Info", $"Username check: {username ?? "NULL"}", "MAINWINDOW");
                
                if (username != null)
                {
                    LogService.WriteSystemLog("[MAINWINDOW] Username found, initializing services...", "Information", "SYSTEM");
                    await RealtimeMonitoringService.AddLogAsync("Info", "Username found, initializing services...", "MAINWINDOW");
                    
                    // Initialize real-time monitoring
                    RealtimeMonitoringService.Initialize(databaseUrl, username);
                    LogService.WriteSystemLog("[MAINWINDOW] RealtimeMonitoringService initialized", "Information", "SYSTEM");
                    await RealtimeMonitoringService.AddLogAsync("Info", "RealtimeMonitoringService initialized", "MAINWINDOW");
                    
                    // Initialize Firebase remote service
                    FirebaseRemoteService.Initialize(databaseUrl, username);
                    LogService.WriteSystemLog("[MAINWINDOW] FirebaseRemoteService initialized", "Information", "SYSTEM");
                    await RealtimeMonitoringService.AddLogAsync("Info", "FirebaseRemoteService initialized", "MAINWINDOW");
                    
                    // Listen for remote commands
                    FirebaseRemoteService.ListenForCommands((commandType, commandId) => 
                    {
                        _ = ExecuteRemoteCommandAsync(commandType, commandId);
                    });
                    
                    // Listen for quick actions
                    ListenForQuickActions();
                    
                    LogService.WriteSystemLog("[FIREBASE] Remote control initialized", "Information", "SYSTEM");
                    await RealtimeMonitoringService.AddLogAsync("Info", "Remote control initialized", "MAINWINDOW");
                }
                else
                {
                    LogService.WriteSystemLog("[FIREBASE] Failed to initialize remote control - no username", "Error", "SYSTEM");
                    await RealtimeMonitoringService.AddLogAsync("Error", "Failed to initialize remote control - no username", "MAINWINDOW");
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Initialization error: {ex.Message}", "Error", "SYSTEM");
                await RealtimeMonitoringService.AddLogAsync("Error", $"Initialization error: {ex.Message}", "MAINWINDOW");
            }
        }

                
        
        private void SetConfigRequiredMode(bool required)
        {
            _configRequired = required;

            var sidebar = this.FindControl<StackPanel>("Sidebar");
            if (sidebar != null)
            {
                foreach (var child in sidebar.Children)
                {
                    if (child is Button b && b.Tag is string tag)
                    {
                        b.IsEnabled = !required || string.Equals(tag, "Settings", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            var mainContent = this.FindControl<ContentControl>("MainContent");
            if (mainContent != null) mainContent.IsEnabled = !required;

            if (required)
            {
                ShowControl(_settingsControl);
                UpdateSidebarSelection("Settings");
                if (mainContent != null) mainContent.IsEnabled = true; // keep Settings itself usable
            }
        }

        private void SetStartupBusy(bool busy)
        {
            var overlay = this.FindControl<Border>("StartupOverlay");
            if (overlay != null) overlay.IsVisible = busy;

            var sidebar = this.FindControl<StackPanel>("Sidebar");
            if (sidebar != null) sidebar.IsEnabled = !busy;

            var mainContent = this.FindControl<ContentControl>("MainContent");
            if (mainContent != null) mainContent.IsEnabled = !busy;

            var btnProfile = this.FindControl<Button>("BtnProfile");
            if (btnProfile != null) btnProfile.IsEnabled = !busy;
        }

        public static async System.Threading.Tasks.Task ShowSystemInfoAsync()
        {
            string buildDate = System.DateTime.Now.ToString("yyyy-MM-dd");
            string creator = "Wesley";

            string changelog = string.Empty;
            try
            {
                var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
                var changelogPath = System.IO.Path.Combine(baseDir, "CHANGELOG.md");
                if (System.IO.File.Exists(changelogPath))
                {
                    var md = System.IO.File.ReadAllText(changelogPath);
                    changelog = BuildChangelogSummary(md);
                }
            }
            catch { /* ignore file read errors and fall back to inline changelog */ }

            if (string.IsNullOrWhiteSpace(changelog))
            {
                changelog = BackupConfig.AppVersion + "\n\n" +
                           "UI:\n" +
                           "- Modernized Fluent-dark look and unified button styles\n" +
                           "- Accent Primary buttons per service (FTP/Mailchimp/SQL/Settings)\n" +
                           "- Sidebar selected tab state\n" +
                           "- Health badge shows detailed outdated services with per-service colors\n\n" +
                           "STARTUP:\n" +
                           "- Startup health scan overlay that blocks UI until scan completes\n\n" +
                           "FIXES:\n" +
                           "- SQL health check aligned with SQL Sync Check to prevent false OUTDATED";
            }

            string buildInfo = $"Build Date: {buildDate}\nCreator: {creator}";

            // Create and show custom dialog
            var dialog = new SystemInfoDialog(buildInfo, changelog);
            var window = new Window
            {
                Content = dialog,
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                Background = Avalonia.Media.Brushes.Transparent,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaTitleBarHeightHint = 0
            };

            dialog.OnOk += (sender, e) => window.Close();

            // Get the main window as owner
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow != null)
            {
                await window.ShowDialog(mainWindow);
            }
        }

        private static string BuildChangelogSummary(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

            var normalized = markdown.Replace("\r\n", "\n").Trim();
            if (normalized.Length == 0) return string.Empty;

            var section = ExtractSection(normalized, "## Unreleased");
            if (string.IsNullOrWhiteSpace(section))
            {
                section = ExtractFirstReleaseSection(normalized);
            }

            if (string.IsNullOrWhiteSpace(section)) return string.Empty;

            var added = ExtractSubSectionBullets(section, "### Added");
            var changed = ExtractSubSectionBullets(section, "### Changed");
            var fixedItems = ExtractSubSectionBullets(section, "### Fixed");

            var sb = new StringBuilder();
            sb.AppendLine("CHANGELOG SUMMARY:");
            sb.AppendLine();

            AppendBulletBlock(sb, "ADDED", added);
            AppendBulletBlock(sb, "CHANGED", changed);
            AppendBulletBlock(sb, "FIXED", fixedItems);

            return sb.ToString().TrimEnd();
        }

        private static void AppendBulletBlock(StringBuilder sb, string title, string[] items)
        {
            sb.AppendLine(title + ":");
            if (items.Length == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                foreach (var item in items)
                {
                    sb.AppendLine("- " + item);
                }
            }
            sb.AppendLine();
        }

        private static string ExtractSection(string markdown, string headerStartsWith)
        {
            var lines = markdown.Split('\n');
            var start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i].TrimEnd();
                if (l.StartsWith(headerStartsWith, StringComparison.OrdinalIgnoreCase))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                var l = lines[i];
                if (i != start && l.StartsWith("## ")) break;
                sb.AppendLine(l);
            }
            return sb.ToString();
        }

        private static string ExtractFirstReleaseSection(string markdown)
        {
            var lines = markdown.Split('\n');
            var start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i].TrimEnd();
                if (l.StartsWith("## ") && !l.StartsWith("## Unreleased", StringComparison.OrdinalIgnoreCase))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                var l = lines[i];
                if (i != start && l.StartsWith("## ")) break;
                sb.AppendLine(l);
            }
            return sb.ToString();
        }

        private static string[] ExtractSubSectionBullets(string sectionMarkdown, string subHeader)
        {
            var lines = sectionMarkdown.Replace("\r\n", "\n").Split('\n');
            var start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i].TrimEnd();
                if (l.StartsWith(subHeader, StringComparison.OrdinalIgnoreCase))
                {
                    start = i + 1;
                    break;
                }
            }

            if (start < 0) return [];

            var list = new List<string>();
            for (int i = start; i < lines.Length; i++)
            {
                var l = lines[i].Trim();
                if (l.StartsWith("### ") || l.StartsWith("## ")) break;
                if (l.StartsWith("- "))
                {
                    list.Add(l.Substring(2).Trim());
                }
            }

            return list.ToArray();
        }

        private void SidebarButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (_configRequired && !string.Equals(tag, "Settings", StringComparison.OrdinalIgnoreCase))
                {
                    NotificationService.ShowBackupToast("Config", "Please complete Settings first.", "Warning");
                    ShowControl(_settingsControl);
                    UpdateSidebarSelection("Settings");
                    return;
                }

                UpdateSidebarSelection(tag);
                switch (tag)
                {
                    case "Home":
                        NotificationService.ShowBackupToast("Tab", "Switched to Home Dashboard", "Info");
                        ShowControl(_homeControl);
                        break;
                    case "FTP":
                        NotificationService.ShowBackupToast("Tab", "Switched to FTP", "Info");
                        ShowControl(_ftpControl);
                        break;
                    case "Mailchimp":
                        NotificationService.ShowBackupToast("Tab", "Switched to Mailchimp", "Info");
                        ShowControl(_mailchimpControl);
                        break;
                    case "SQL":
                        NotificationService.ShowBackupToast("Tab", "Switched to SQL", "Info");
                        ShowControl(_sqlControl);
                        break;
                    case "Settings":
                        NotificationService.ShowBackupToast("Tab", "Switched to Settings", "Info");
                        ShowControl(_settingsControl);
                        break;
                }
            }
        }

        private void UpdateSidebarSelection(string activeTag)
        {
            var sidebar = this.FindControl<StackPanel>("Sidebar");
            if (sidebar == null) return;

            foreach (var child in sidebar.Children)
            {
                if (child is Button btn && btn.Tag is string tag)
                {
                    if (tag == activeTag)
                    {
                        btn.Classes.Add("Selected");
                        var icon = btn.FindDescendantOfType<PathIcon>();
                        if (icon != null) icon.Foreground = Avalonia.Media.Brush.Parse("#FCA311");
                    }
                    else
                    {
                        btn.Classes.Remove("Selected");
                        var icon = btn.FindDescendantOfType<PathIcon>();
                        if (icon != null) icon.Foreground = Avalonia.Media.Brush.Parse("#808080");
                    }
                }
            }
        }

        private void OnWindowStateChanged(WindowState state)
        {
            var mainContent = this.FindControl<ContentControl>("MainContent");
            if (mainContent == null) return;

            // Adjust margins based on window state
            if (state == WindowState.Maximized)
            {
                // Smaller margin when maximized for more screen space
                mainContent.Margin = new Thickness(8);
            }
            else
            {
                // Standard margin for normal window
                mainContent.Margin = new Thickness(20);
            }

            // Update HomeControl layout for maximized state
            _homeControl?.SetMaximizedLayout(state == WindowState.Maximized);
        }

        private void ShowControl(UserControl control)
        {
            var contentControl = this.FindControl<ContentControl>("MainContent");
            if (contentControl != null)
            {
                contentControl.Content = control;
            }

            _activeTabAccentBrush = GetAccentBrushForControl(control);
        }

        private async Task RunAllChecksAsync()
        {
            NotificationService.ShowBackupToast("Dashboard", "Running parallel sync check on all services...", "Info");
            
            var tasks = new List<Task<(string service, bool success)>>();
            
            if (!_ftpControl.IsBusy) tasks.Add(Task.Run(async () => 
            {
                try { await _ftpControl.TriggerSyncCheckAsync(); return ("FTP", true); }
                catch { return ("FTP", false); }
            }));
            
            if (!_mailchimpControl.IsBusy) tasks.Add(Task.Run(async () => 
            {
                try { await _mailchimpControl.TriggerSyncCheckAsync(); return ("Mailchimp", true); }
                catch { return ("Mailchimp", false); }
            }));
            
            if (!_sqlControl.IsBusy) tasks.Add(Task.Run(async () => 
            {
                try { await _sqlControl.TriggerSyncCheckAsync(); return ("SQL", true); }
                catch { return ("SQL", false); }
            }));
            
            if (tasks.Count == 0)
            {
                NotificationService.ShowBackupToast("Dashboard", "All services are busy. Try again later.", "Warning");
                return;
            }
            
            var results = await Task.WhenAll(tasks);
            var successCount = results.Count(r => r.success);
            var failedServices = results.Where(r => !r.success).Select(r => r.service).ToList();
            
            // Run health check after all sync checks complete
            _ = _backupManager.RunHealthCheckAsync();
            
            if (failedServices.Count > 0)
            {
                NotificationService.ShowBackupToast("Dashboard", $"Checks complete. {successCount}/{tasks.Count} succeeded. Failed: {string.Join(", ", failedServices)}", "Warning");
            }
            else
            {
                NotificationService.ShowBackupToast("Dashboard", $"All checks complete ({successCount}/{tasks.Count} succeeded).", "Info");
            }
        }

        private static IBrush GetAccentBrushForControl(UserControl control)
        {
            if (control is HomeControl) return Brush.Parse("#FCA311");
            if (control is FtpControl) return Brush.Parse("#52B788");
            if (control is MailchimpControl) return Brush.Parse("#48CAE4");
            if (control is SqlControl) return Brush.Parse("#FAD643");
            if (control is SettingsControl) return Brush.Parse("#FCA311");
            return Brush.Parse("#FCA311");
        }

        private static IBrush GetAccentBrushForService(string service)
        {
            if (service.Equals("Website", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#FCA311");
            if (service.Equals("FTP", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#52B788");
            if (service.Equals("Mailchimp", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#48CAE4");
            if (service.Equals("SQL", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#FAD643");
            if (service.Equals("Database", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#FAD643");
            return Brush.Parse("#FCA311");
        }

        private void UpdateTime(DateTime usTime, DateTime mnlTime, DateTime nextAuto, DateTime nextDaily)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var txtUs = this.FindControl<TextBlock>("TxtUsTime");
                var txtMnl = this.FindControl<TextBlock>("TxtMnlTime");
                if (txtUs != null) txtUs.Text = usTime.ToString("yyyy-MM-dd hh:mm:sstt");
                if (txtMnl != null) txtMnl.Text = mnlTime.ToString("yyyy-MM-dd hh:mm:sstt");

                DateTime activeNextAuto = nextAuto;
                DateTime activeNextDailyMnl = nextDaily;

                var contentControl = this.FindControl<ContentControl>("MainContent");
                if (contentControl?.Content is UserControl activeControl && activeControl is not HomeControl)
                {
                    if (activeControl is FtpControl)
                    {
                        activeNextAuto = _backupManager.NextFtpAutoScan;
                        activeNextDailyMnl = BackupManager.NextFtpDailySyncMnl;
                    }
                    else if (activeControl is MailchimpControl)
                    {
                        activeNextAuto = _backupManager.NextMailchimpAutoScan;
                        activeNextDailyMnl = BackupManager.NextMailchimpDailySyncMnl;
                    }
                    else if (activeControl is SqlControl)
                    {
                        activeNextAuto = _backupManager.NextSqlAutoScan;
                        activeNextDailyMnl = BackupManager.NextSqlDailySyncMnl;
                    }

                    var txtAuto = activeControl.FindControl<TextBlock>("TxtAutoScan");
                    var txtDaily = activeControl.FindControl<TextBlock>("TxtNextDaily");

                    if (txtAuto != null) 
                    {
                        var diff = activeNextAuto - usTime;
                        txtAuto.Text = $"Auto-Scan: {(diff.TotalSeconds > 0 ? diff.ToString(@"hh\:mm\:ss") : "00:00:00")}";
                    }
                    if (txtDaily != null)
                    {
                        var diff = activeNextDailyMnl - mnlTime;
                        txtDaily.Text = $"Next Daily: {(diff.TotalSeconds > 0 ? diff.ToString(@"hh\:mm\:ss") : "00:00:00")}";
                    }
                }
            });
        }

        private void UpdateHealthStatus(List<BackupHealthReport> reports)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_startupHealthPending)
                {
                    _startupHealthPending = false;
                    SetStartupBusy(false);
                    
                    // Restore original grid layout with sidepanel first
                    var mainGrid = this.FindControl<Grid>("MainGrid");
                    if (mainGrid != null)
                    {
                        mainGrid.ColumnDefinitions.Clear();
                        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); // Sidepanel width
                        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content area
                    }
                    
                    // Animate sidepanel appearance with fade-in and slide-in
                    var sidepanelBorder = this.FindControl<Border>("SidepanelBorder");
                    if (sidepanelBorder != null)
                    {
                        // Set initial state for animation
                        sidepanelBorder.IsVisible = true;
                        sidepanelBorder.Opacity = 0;
                        sidepanelBorder.RenderTransform = new TranslateTransform(-72, 0); // Start from left
                        
                        // Animate with Task.Delay for smooth transitions
                        Task.Delay(50).ContinueWith(_ =>
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                // Fade-in animation
                                sidepanelBorder.Opacity = 1;
                                
                                // Slide-in animation
                                sidepanelBorder.RenderTransform = new TranslateTransform(0, 0);
                            });
                        });
                    }
                    
                    // Enable notifications after startup complete
                    NotificationService.EnableNotifications();
                    
                    // Show completion notification (now visible)
                    NotificationService.ShowBackupToast("Startup", "Health scan complete.", "Info");
                    
                    // Show customize tab order button after health check completes
                    var btnCustomize = this.FindControl<Button>("BtnCustomizeTabs");
                    if (btnCustomize != null) btnCustomize.IsVisible = true;
                }

                var txtHealth = this.FindControl<TextBlock>("TxtHealth");
                var indicator = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("HealthIndicator");
                var badge = this.FindControl<Border>("HealthBadge");

                if (txtHealth != null)
                {
                    bool allOk = reports.TrueForAll(r => string.Equals(r.Color, "LimeGreen", StringComparison.OrdinalIgnoreCase));
                    var outdated = reports
                        .Where(r => !string.Equals(r.Color, "LimeGreen", StringComparison.OrdinalIgnoreCase))
                        .Select(r => r.Service)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    var healthBrush = allOk
                        ? Brush.Parse("#52B788")
                        : Brush.Parse("#F38BA8");

                    txtHealth.Inlines?.Clear();
                    txtHealth.Text = string.Empty;

                    txtHealth.Inlines ??= [];

                    txtHealth.Inlines.Add(new Run(allOk ? "HEALTH: ALL BACKUPS IS UPDATED" : "HEALTH: ATTENTION REQUIRED")
                    {
                        Foreground = healthBrush
                    });

                    if (!allOk && outdated.Length > 0)
                    {
                        txtHealth.Inlines.Add(new Run("(") 
                        {
                            Foreground = Brush.Parse("#808080")
                        });

                        txtHealth.Inlines.Add(new Run("Outdated: ")
                        {
                            Foreground = Brush.Parse("#FCA311"),
                            FontWeight = Avalonia.Media.FontWeight.SemiBold
                        });

                        for (int i = 0; i < outdated.Length; i++)
                        {
                            if (i > 0)
                            {
                                txtHealth.Inlines.Add(new Run(", ")
                                {
                                    Foreground = Brush.Parse("#808080")
                                });
                            }

                            var svc = outdated[i];
                            txtHealth.Inlines.Add(new Run(svc)
                            {
                                Foreground = GetAccentBrushForService(svc),
                                FontWeight = Avalonia.Media.FontWeight.Bold
                            });
                        }

                        txtHealth.Inlines.Add(new Run(")")
                        {
                            Foreground = Brush.Parse("#808080")
                        });
                    }

                    if (indicator != null) indicator.Fill = healthBrush;
                    if (badge != null) badge.Background = allOk
                        ? Brush.Parse("#112B1E")
                        : Brush.Parse("#2D1515");
                }

                var statusBar = this.FindControl<Border>("StatusBar");
                var txtStatus = this.FindControl<TextBlock>("TxtStatus");
                var statusIcon = this.FindControl<PathIcon>("StatusIcon");

                if (statusBar != null)
                {
                    bool allOk = reports.TrueForAll(r => string.Equals(r.Color, "LimeGreen", StringComparison.OrdinalIgnoreCase));
                    statusBar.Background = allOk
                        ? Avalonia.Media.Brush.Parse("#0D1F15")
                        : Avalonia.Media.Brush.Parse("#1A0F11");
                    statusBar.BorderBrush = allOk
                        ? Avalonia.Media.Brush.Parse("#2D6A4F")
                        : Avalonia.Media.Brush.Parse("#F38BA8");

                    if (txtStatus != null)
                    {
                        txtStatus.Foreground = allOk
                            ? Avalonia.Media.Brush.Parse("#52B788")
                            : Avalonia.Media.Brush.Parse("#F38BA8");
                    }

                    if (statusIcon != null)
                    {
                        statusIcon.Foreground = allOk
                            ? Avalonia.Media.Brush.Parse("#52B788")
                            : Avalonia.Media.Brush.Parse("#F38BA8");
                    }
                }
            });
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            WindowStateService.Save(this);

            if (!_allowClose)
            {
                e.Cancel = true;
                _ = ConfirmCloseAsync();
                return;
            }

            NotificationService.OnToast -= HandleToast;
            _backupManager.Stop();
            base.OnClosing(e);
        }

        private async System.Threading.Tasks.Task ConfirmCloseAsync()
        {
            bool anyBusy = _ftpControl.IsBusy || _mailchimpControl.IsBusy || _sqlControl.IsBusy;
            string message = anyBusy
                ? "A backup task is currently running.\n\nExit anyway? Running tasks will be cancelled."
                : "Exit PinayPal Backup Manager?";

            bool shouldClose = await NotificationService.ConfirmAsync(message, "Confirm Exit");
            if (!shouldClose) return;

            if (_ftpControl.IsBusy) _ftpControl.RequestCancelFromShell();
            if (_mailchimpControl.IsBusy) _mailchimpControl.RequestCancelFromShell();
            if (_sqlControl.IsBusy) _sqlControl.RequestCancelFromShell();

            // Wait for tasks to cancel
            await Task.Delay(1000);

            // Stop real-time monitoring service
            RealtimeMonitoringService.Stop();

            NotificationService.ShowBackupToast("Exiting", anyBusy ? "Closing app and cancelling running tasks." : "Closing app.", anyBusy ? "Warning" : "Info");

            _allowClose = true;
            Close();
        }

        private void UpdateActiveProcessCount(object? sender, EventArgs e)
        {
            int activeCount = 0;
            if (_ftpControl?.IsBusy == true) activeCount++;
            if (_mailchimpControl?.IsBusy == true) activeCount++;
            if (_sqlControl?.IsBusy == true) activeCount++;
            
            _homeControl.SetActiveOperations(activeCount);
        }

        private void HandleToast(string title, string message, string type)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var border = this.FindControl<Border>("ToastBorder");
                if (border == null) return;

                var tTitle = this.FindControl<TextBlock>("ToastTitle");
                var tMsg = this.FindControl<TextBlock>("ToastMessage");
                if (tTitle != null) tTitle.Text = title;
                if (tMsg != null) tMsg.Text = message;

                border.BorderBrush = type.Equals("Error", StringComparison.OrdinalIgnoreCase)
                    ? Avalonia.Media.Brush.Parse("#F38BA8")
                    : type.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                        ? Avalonia.Media.Brush.Parse("#FAD643")
                        : Avalonia.Media.Brush.Parse("#5A189A");

                border.IsVisible = true;
                border.Opacity = 1;

                _toastTimer?.Stop();
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                _toastTimer.Tick += (_, _) =>
                {
                    _toastTimer?.Stop();
                    border.IsVisible = false;
                };
                _toastTimer.Start();
            });
        }

        private bool _notifCenterOpen;

        public void ToggleNotificationCenter()
        {
            _notifCenterOpen = !_notifCenterOpen;
            var panel = this.FindControl<Border>("NotificationCenter");
            var overlay = this.FindControl<Border>("NotificationOverlay");
            if (panel != null) panel.IsVisible = _notifCenterOpen;
            if (overlay != null) overlay.IsVisible = _notifCenterOpen;
            if (_notifCenterOpen) { PopulateNotificationCenter(); NotificationHistoryService.MarkAllRead(); UpdateBellBadge(); }
        }
        
        private void NotificationOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Close notification center when overlay is clicked
            if (_notifCenterOpen)
            {
                ToggleNotificationCenter();
            }
        }

        private void PopulateNotificationCenter()
        {
            var list = this.FindControl<StackPanel>("NotificationList");
            if (list == null) return;
            list.Children.Clear();
            var entries = NotificationHistoryService.Entries;
            if (entries.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "No notifications yet.", FontSize = 11, Foreground = Brush.Parse("#808080"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12) });
                return;
            }
            foreach (var n in entries)
            {
                string accent = n.Type == "Error" ? "#F38BA8" : n.Type == "Warning" ? "#FAD643" : n.Type == "Success" ? "#52B788" : "#FCA311";
                var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Margin = new Thickness(0, 2) };
                row.Background = Brush.Parse(ThemeService.IsDark ? "#1F1505" : "#FFF3CD");
                var content = new StackPanel { Spacing = 2 };
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                var title = new TextBlock { Text = n.Title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse(accent) };
                var time = new TextBlock { Text = n.Time.ToString("HH:mm"), FontSize = 9, Foreground = Brush.Parse("#808080"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(title, 0); Grid.SetColumn(time, 1);
                header.Children.Add(title); header.Children.Add(time);
                content.Children.Add(header);
                content.Children.Add(new TextBlock { Text = n.Message, FontSize = 10, Foreground = Brush.Parse(ThemeService.IsDark ? "#FCA311" : "#D4880E"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                row.Child = content;
                list.Children.Add(row);
            }
        }

        private void UpdateBellBadge()
        {
            var badge = this.FindControl<Border>("BellBadge");
            var count = this.FindControl<TextBlock>("BellCount");
            int unread = NotificationHistoryService.UnreadCount;
            if (badge != null) badge.IsVisible = unread > 0;
            if (count != null) count.Text = unread > 9 ? "9+" : unread.ToString();
        }

        private void SetupSystemTray()
        {
            try
            {
                var tray = new Avalonia.Controls.TrayIcon();
                tray.Icon = new Avalonia.Controls.WindowIcon(new Avalonia.Media.Imaging.Bitmap(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico")));
                tray.ToolTipText = "PinayPal Backup Manager";
                tray.Clicked += (_, _) => Dispatcher.UIThread.Post(() => { Show(); WindowState = WindowState.Normal; Activate(); });
                var menu = new Avalonia.Controls.NativeMenu();
                var showItem = new Avalonia.Controls.NativeMenuItem { Header = "Show" };
                showItem.Click += (_, _) => Dispatcher.UIThread.Post(() => { Show(); WindowState = WindowState.Normal; Activate(); });
                var exitItem = new Avalonia.Controls.NativeMenuItem { Header = "Exit" };
                exitItem.Click += (_, _) => { _allowClose = true; Close(); };
                menu.Items.Add(showItem);
                menu.Items.Add(new Avalonia.Controls.NativeMenuItemSeparator());
                menu.Items.Add(exitItem);
                tray.Menu = menu;
                Avalonia.Controls.TrayIcon.SetIcons(Avalonia.Application.Current!, new Avalonia.Controls.TrayIcons { tray });
            }
            catch { }
        }

        private void UpdateThemeIcon()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var icon = this.FindControl<TextBlock>("TxtThemeIcon");
                if (icon != null) icon.Text = ThemeService.IsDark ? "☀" : "🌙";
            });
        }

        private async Task OpenTabOrderDialogAsync()
        {
            var dialog = new TabOrderDialog();
            await dialog.ShowDialog<bool?>(this);
            if (dialog.Saved) ApplySavedTabOrder();
        }

        private void ApplySavedTabOrder()
        {
            var sidebar = this.FindControl<StackPanel>("Sidebar");
            if (sidebar == null) return;

            var order = TabOrderDialog.LoadSavedTagOrder();
            var buttons = sidebar.Children.OfType<Button>().ToList();
            var separators = sidebar.Children.OfType<Rectangle>().ToList();

            sidebar.Children.Clear();

            bool first = true;
            foreach (var tag in order)
            {
                var btn = buttons.FirstOrDefault(b => b.Tag is string t && t == tag);
                if (btn == null) continue;

                if (!first && tag == "Settings")
                {
                    var sep = separators.LastOrDefault();
                    if (sep != null) sidebar.Children.Add(sep);
                }
                else if (!first && tag != "Settings" && first == false)
                {
                    if (tag == order.Skip(1).FirstOrDefault() && separators.Count > 0)
                    {
                    }
                }

                sidebar.Children.Add(btn);
                first = false;
            }

            NotificationService.ShowBackupToast("Tabs", "Tab order updated.", "Success");
        }

        #region Profile Management

        private void InitializeProfileSection()
        {
            // Update user info display
            UpdateProfileDisplay();

            // Setup profile button click
            var btnProfile = this.FindControl<Button>("BtnProfile");
            if (btnProfile != null)
            {
                btnProfile.Click += ToggleProfileMenu;
            }

            
            // Listen for auth changes
            AuthService.OnUserChanged += (user) => UpdateProfileDisplay();
        }

        private void UpdateProfileDisplay()
        {
            // Profile display simplified - only avatar shown in sidebar
            LoadSidebarAvatar();
        }

        private void LoadSidebarAvatar()
        {
            try
            {
                AppDataPaths.MigrateFile("avatar.png");
                var avatarPath = AppDataPaths.GetExistingOrCurrentPath("avatar.png");
                
                var imgAvatar = this.FindControl<Image>("AvatarImage");
                var ellipseBg = this.FindControl<Ellipse>("AvatarImageBg");
                
                if (System.IO.File.Exists(avatarPath) && imgAvatar != null)
                {
                    var bitmap = new Avalonia.Media.Imaging.Bitmap(avatarPath);
                    imgAvatar.Source = bitmap;
                    imgAvatar.IsVisible = true;
                    if (ellipseBg != null) ellipseBg.IsVisible = false;
                }
                else
                {
                    if (imgAvatar != null) imgAvatar.IsVisible = false;
                    if (ellipseBg != null) ellipseBg.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] Failed to load sidebar avatar: {ex.Message}");
            }
        }

        private async Task ExecuteRemoteCommandAsync(string commandType, string commandId)
        {
            await FirebaseRemoteService.UpdateCommandStatusAsync(commandId, "running");
            await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "running", 0, "Starting operation...", "", "", "");
            await RealtimeMonitoringService.AddLogAsync("Info", $"Remote command received: {commandType}", "REMOTE");
            
            try
            {
                switch (commandType)
                {
                    case "ftp_sync":
                        await RealtimeMonitoringService.AddActivityAsync("sync_check", "FTP", "FTP sync check started");
                        await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "running", 10, "Initializing FTP sync...", "", "", "");
                        
                        Dispatcher.UIThread.Post(() =>
                        {
                            ShowControl(_ftpControl!);
                            UpdateSidebarSelection("FTP");
                            _ftpControl?.PerformSyncCheck();
                        });
                        break;
                        
                    case "ftp_backup":
                        await RealtimeMonitoringService.AddActivityAsync("backup_started", "FTP", "FTP backup started");
                        await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "running", 5, "Preparing FTP backup...", "", "", "");
                        
                        // Start progress tracking
                        BackupProgressService.StartBackup(commandId, "FTP");
                        
                        Dispatcher.UIThread.Post(() =>
                        {
                            ShowControl(_ftpControl!);
                            UpdateSidebarSelection("FTP");
                            _ftpControl?.StartBackupFromShell();
                        });
                        break;
                        
                    case "mailchimp_sync":
                        await RealtimeMonitoringService.AddActivityAsync("sync_check", "Mailchimp", "Mailchimp sync check started");
                        await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "running", 10, "Initializing Mailchimp sync...", "", "", "");
                        
                        Dispatcher.UIThread.Post(() =>
                        {
                            ShowControl(_mailchimpControl!);
                            UpdateSidebarSelection("Mailchimp");
                            _mailchimpControl?.PerformSyncCheck();
                        });
                        break;
                        
                    case "mailchimp_backup":
                        await RealtimeMonitoringService.AddActivityAsync("backup_started", "Mailchimp", "Mailchimp backup started");
                        await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "running", 5, "Preparing Mailchimp backup...", "", "", "");
                        
                        // Start progress tracking
                        BackupProgressService.StartBackup(commandId, "Mailchimp");
                        
                        Dispatcher.UIThread.Post(() =>
                        {
                            ShowControl(_mailchimpControl!);
                            UpdateSidebarSelection("Mailchimp");
                            _mailchimpControl?.StartFullBackupFromShell();
                        });
                        break;
                        
                    case "sql_sync":
                        await RealtimeMonitoringService.AddActivityAsync("sync_check", "SQL", "SQL sync check started");
                        await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "running", 10, "Initializing SQL sync...", "", "", "");
                        
                        Dispatcher.UIThread.Post(() =>
                        {
                            ShowControl(_sqlControl!);
                            UpdateSidebarSelection("SQL");
                            _sqlControl?.PerformSyncCheck();
                        });
                        break;
                        
                    case "sql_backup":
                        await RealtimeMonitoringService.AddActivityAsync("backup_started", "SQL", "SQL backup started");
                        await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "running", 5, "Preparing SQL backup...", "", "", "");
                        
                        // Start progress tracking
                        BackupProgressService.StartBackup(commandId, "SQL");
                        
                        Dispatcher.UIThread.Post(() =>
                        {
                            ShowControl(_sqlControl!);
                            UpdateSidebarSelection("SQL");
                            _sqlControl?.StartBackupFromShell();
                        });
                        break;
                        
                    case "test_log":
                        await RealtimeMonitoringService.AddLogAsync("Info", "Test log triggered from Flutter app", "SYSTEM");
                        await FirebaseRemoteService.UpdateCommandStatusAsync(commandId, "completed", "Test log sent");
                        await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "completed", 100, "Test log sent successfully", "", "", "");
                        break;
                        
                    default:
                        await FirebaseRemoteService.UpdateCommandStatusAsync(commandId, "failed", $"Unknown command type: {commandType}");
                        await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "failed", 0, "Unknown command type", "", "", "");
                        await RealtimeMonitoringService.AddLogAsync("Error", $"Unknown command type: {commandType}", "REMOTE");
                        return;
                }
                
                await FirebaseRemoteService.UpdateCommandStatusAsync(commandId, "completed", "Success");
                await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "completed", 100, "Operation completed successfully", "", "", "");
                await RealtimeMonitoringService.AddActivityAsync("backup_completed", commandType.Split('_')[0], $"{commandType.Split('_')[0].ToUpper()} operation completed");
                await RealtimeMonitoringService.AddLogAsync("Info", $"Command completed: {commandType}", "REMOTE");
            }
            catch (Exception ex)
            {
                await FirebaseRemoteService.UpdateCommandStatusAsync(commandId, "failed", ex.Message);
                await RealtimeMonitoringService.UpdateCommandStatusAsync(commandId, commandType, "failed", 0, ex.Message, "", "", "");
                await RealtimeMonitoringService.AddActivityAsync("backup_failed", commandType.Split('_')[0], $"{commandType.Split('_')[0].ToUpper()} operation failed: {ex.Message}");
                await RealtimeMonitoringService.AddLogAsync("Error", $"Command failed: {commandType} - {ex.Message}", "REMOTE");
                
                // End progress tracking if it was started
                if (BackupProgressService.IsTracking)
                {
                    await BackupProgressService.CompleteBackupAsync(false, ex.Message);
                }
            }
        }

        private void ListenForQuickActions()
        {
            try
            {
                var databaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
                var database = new FirebaseClient(databaseUrl);
                var username = AuthService.CurrentUser?.Username;
                
                if (username == null) return;
                
                // Simple polling approach for quick actions
                Task.Run(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            var quickActions = await database
                                .Child("users")
                                .Child(username)
                                .Child("quick_actions")
                                .OnceAsync<QuickAction>();

                            foreach (var action in quickActions)
                            {
                                if (action.Object?.Action == "emergency_stop" && action.Object?.Status == "pending")
                                {
                                    await HandleEmergencyStopAsync(action.Key);
                                }
                            }
                            
                            await Task.Delay(2000); // Check every 2 seconds
                        }
                        catch (Exception ex)
                        {
                            LogService.WriteSystemLog($"[QUICK_ACTIONS] Monitoring error: {ex.Message}", "Error", "SYSTEM");
                            await Task.Delay(5000); // Wait longer on error
                        }
                    }
                });
                
                LogService.WriteSystemLog("[FIREBASE] Listening for quick actions...", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to listen for quick actions: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private async Task HandleEmergencyStopAsync(string actionId)
        {
            try
            {
                LogService.WriteSystemLog("[QUICK_ACTIONS] Emergency stop triggered", "Warning", "SYSTEM");
                await RealtimeMonitoringService.AddActivityAsync("emergency_stop", "System", "Emergency stop triggered from Flutter app");
                
                // Stop all running operations
                if (_ftpControl?.IsBusy == true) _ftpControl.RequestCancelFromShell();
                if (_mailchimpControl?.IsBusy == true) _mailchimpControl.RequestCancelFromShell();
                if (_sqlControl?.IsBusy == true) _sqlControl.RequestCancelFromShell();
                
                NotificationService.ShowBackupToast("Emergency Stop", "All running tasks cancelled from Flutter app.", "Warning");
                
                // Update action status
                var databaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
                var database = new FirebaseClient(databaseUrl);
                var username = AuthService.CurrentUser?.Username;
                
                if (username != null)
                {
                    await database
                        .Child("users")
                        .Child(username)
                        .Child("quick_actions")
                        .Child(actionId)
                        .PatchAsync(new { status = "completed", timestamp = DateTime.UtcNow.ToString("o") });
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[QUICK_ACTIONS] Emergency stop failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private void ToggleProfileMenu(object? sender, RoutedEventArgs e)
        {
            // Show ProfileControl in main content area
            ShowControl(_profileControl);
            UpdateSidebarSelection("Profile");
        }

        
        private async Task ShowChangePasswordDialog()
        {
            // TODO: Implement change password dialog
            NotificationService.ShowBackupToast("Profile", "Password change feature coming soon!", "Info");
        }

        private async Task ShowChangeUsernameDialog()
        {
            // TODO: Implement change username dialog
            NotificationService.ShowBackupToast("Profile", "Username change feature coming soon!", "Info");
        }

        private async Task UploadAvatar()
        {
            // TODO: Implement avatar upload
            NotificationService.ShowBackupToast("Profile", "Avatar upload feature coming soon!", "Info");
        }

        #endregion
    }
}

public class QuickAction
{
    public string Action { get; set; } = "";
    public string Status { get; set; } = "";
    public string Timestamp { get; set; } = "";
}
