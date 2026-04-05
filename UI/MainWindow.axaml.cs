using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MsBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PinayPalBackupManager.Services;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.UI.UserControls;

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
        private IBrush _activeTabAccentBrush = Brush.Parse("#A6E3A1");
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
            if (btnCustomize != null) btnCustomize.Click += async (_, _) => await OpenTabOrderDialogAsync();

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
                Title = "System Information",
                Content = dialog,
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                Background = Avalonia.Media.Brushes.Transparent
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
                        if (icon != null) icon.Foreground = Avalonia.Media.Brush.Parse("#CBA6F7");
                    }
                    else
                    {
                        btn.Classes.Remove("Selected");
                        var icon = btn.FindDescendantOfType<PathIcon>();
                        if (icon != null) icon.Foreground = Avalonia.Media.Brush.Parse("#585B70");
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
            if (control is HomeControl) return Brush.Parse("#CBA6F7");
            if (control is FtpControl) return Brush.Parse("#A6E3A1");
            if (control is MailchimpControl) return Brush.Parse("#89DCEB");
            if (control is SqlControl) return Brush.Parse("#F9E2AF");
            if (control is SettingsControl) return Brush.Parse("#89B4FA");
            return Brush.Parse("#A6ADC8");
        }

        private static IBrush GetAccentBrushForService(string service)
        {
            if (service.Equals("Website", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#CBA6F7");
            if (service.Equals("FTP", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#A6E3A1");
            if (service.Equals("Mailchimp", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#89DCEB");
            if (service.Equals("SQL", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#F9E2AF");
            if (service.Equals("Database", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#F9E2AF");
            return Brush.Parse("#A6ADC8");
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
                    NotificationService.ShowBackupToast("Startup", "Health scan complete.", "Info");
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
                        ? Brush.Parse("#A6E3A1")
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
                        txtHealth.Inlines.Add(new Run(" (")
                        {
                            Foreground = Brush.Parse("#6C7086")
                        });

                        txtHealth.Inlines.Add(new Run("Outdated: ")
                        {
                            Foreground = Brush.Parse("#A6ADC8"),
                            FontWeight = Avalonia.Media.FontWeight.SemiBold
                        });

                        for (int i = 0; i < outdated.Length; i++)
                        {
                            if (i > 0)
                            {
                                txtHealth.Inlines.Add(new Run(", ")
                                {
                                    Foreground = Brush.Parse("#6C7086")
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
                            Foreground = Brush.Parse("#6C7086")
                        });
                    }

                    if (indicator != null) indicator.Fill = healthBrush;
                    if (badge != null) badge.Background = allOk
                        ? Brush.Parse("#1A2B21")
                        : Brush.Parse("#2D1A1E");
                }

                var statusBar = this.FindControl<Border>("StatusBar");
                var txtStatus = this.FindControl<TextBlock>("TxtStatus");
                var statusIcon = this.FindControl<PathIcon>("StatusIcon");

                if (statusBar != null)
                {
                    bool allOk = reports.TrueForAll(r => string.Equals(r.Color, "LimeGreen", StringComparison.OrdinalIgnoreCase));
                    statusBar.Background = allOk
                        ? Avalonia.Media.Brush.Parse("#0D1117")
                        : Avalonia.Media.Brush.Parse("#1A0F11");
                    statusBar.BorderBrush = allOk
                        ? Avalonia.Media.Brush.Parse("#238636")
                        : Avalonia.Media.Brush.Parse("#F38BA8");

                    if (txtStatus != null)
                    {
                        txtStatus.Foreground = allOk
                            ? Avalonia.Media.Brush.Parse("#A6E3A1")
                            : Avalonia.Media.Brush.Parse("#F38BA8");
                    }

                    if (statusIcon != null)
                    {
                        statusIcon.Foreground = allOk
                            ? Avalonia.Media.Brush.Parse("#A6E3A1")
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
                        ? Avalonia.Media.Brush.Parse("#F9E2AF")
                        : Avalonia.Media.Brush.Parse("#313244");

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

        private void ToggleNotificationCenter()
        {
            _notifCenterOpen = !_notifCenterOpen;
            var panel = this.FindControl<Border>("NotificationCenter");
            if (panel != null) panel.IsVisible = _notifCenterOpen;
            if (_notifCenterOpen) { PopulateNotificationCenter(); NotificationHistoryService.MarkAllRead(); UpdateBellBadge(); }
        }

        private void PopulateNotificationCenter()
        {
            var list = this.FindControl<StackPanel>("NotificationList");
            if (list == null) return;
            list.Children.Clear();
            var entries = NotificationHistoryService.Entries;
            if (entries.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "No notifications yet.", FontSize = 11, Foreground = Brush.Parse("#6C7086"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12) });
                return;
            }
            foreach (var n in entries)
            {
                string accent = n.Type == "Error" ? "#F38BA8" : n.Type == "Warning" ? "#F9E2AF" : n.Type == "Success" ? "#A6E3A1" : "#89B4FA";
                var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Margin = new Thickness(0, 2) };
                row.Background = Brush.Parse(ThemeService.IsDark ? "#1E1E2E" : "#E6E9EF");
                var content = new StackPanel { Spacing = 2 };
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                var title = new TextBlock { Text = n.Title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse(accent) };
                var time = new TextBlock { Text = n.Time.ToString("HH:mm"), FontSize = 9, Foreground = Brush.Parse("#6C7086"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(title, 0); Grid.SetColumn(time, 1);
                header.Children.Add(title); header.Children.Add(time);
                content.Children.Add(header);
                content.Children.Add(new TextBlock { Text = n.Message, FontSize = 10, Foreground = Brush.Parse(ThemeService.IsDark ? "#A6ADC8" : "#5C5F77"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
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
