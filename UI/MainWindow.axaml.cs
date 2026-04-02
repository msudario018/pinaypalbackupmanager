using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PinayPalBackupManager.Services;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.UI.UserControls;
using Avalonia.Threading;
using System.Text;

namespace PinayPalBackupManager.UI
{
    public partial class MainWindow : Window
    {
        private readonly BackupManager _backupManager;
        private readonly FtpControl _ftpControl;
        private readonly MailchimpControl _mailchimpControl;
        private readonly SqlControl _sqlControl;
        private readonly SettingsControl _settingsControl;
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

            _ftpControl = new FtpControl(_backupManager);
            _mailchimpControl = new MailchimpControl(_backupManager);
            _sqlControl = new SqlControl(_backupManager);
            _settingsControl = new SettingsControl(_backupManager);
            _settingsControl.OnShowSystemInfo += ShowSystemInfoAsync;
            _settingsControl.OnCheckUpdates += async () => await UpdateService.CheckForUpdatesWithUiAsync();
            _settingsControl.OnConfigSaved += () => SetConfigRequiredMode(!ConfigService.IsConfigured());
            _settingsControl.OnLogout += () =>
            {
                _allowClose = true;
                OnLogoutRequested?.Invoke();
            };

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
                ShowControl(_ftpControl);
                UpdateSidebarSelection("FTP");
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

            string message = $"Build Date: {buildDate}\n" +
                             $"Creator: {creator}\n\n" +
                             $"CHANGELOG:\n{changelog}";

            await NotificationService.ShowMessageBoxAsync(message, "System Information", MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Info);
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
                if (child is Button b && b.Tag is string t)
                {
                    if (string.Equals(t, activeTag, StringComparison.OrdinalIgnoreCase)) b.Classes.Add("Selected");
                    else b.Classes.Remove("Selected");
                }
            }
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

        private static IBrush GetAccentBrushForControl(UserControl control)
        {
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
                if (contentControl?.Content is UserControl activeControl)
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

            // Setup profile menu buttons
            SetupProfileMenuButtons();

            // Listen for auth changes
            AuthService.OnUserChanged += (user) => UpdateProfileDisplay();
        }

        private void UpdateProfileDisplay()
        {
            var txtUsername = this.FindControl<TextBlock>("TxtUsername");
            var txtUserRole = this.FindControl<TextBlock>("TxtUserRole");
            var adminMenuItems = this.FindControl<StackPanel>("AdminMenuItems");
            var userMenuItems = this.FindControl<StackPanel>("UserMenuItems");

            if (AuthService.CurrentUser != null)
            {
                txtUsername!.Text = AuthService.CurrentUser.Username;
                txtUserRole!.Text = AuthService.CurrentUser.Role;

                // Show/hide admin menu items
                adminMenuItems!.IsVisible = AuthService.IsAdmin;
            }
            else
            {
                txtUsername!.Text = "Guest";
                txtUserRole!.Text = "Not logged in";
                adminMenuItems!.IsVisible = false;
            }
        }

        private void ToggleProfileMenu(object? sender, RoutedEventArgs e)
        {
            var profileMenu = this.FindControl<StackPanel>("ProfileMenu");
            if (profileMenu != null)
            {
                profileMenu.IsVisible = !profileMenu.IsVisible;
            }
        }

        private void SetupProfileMenuButtons()
        {
            // User Management (Admin only)
            var btnUserManagement = this.FindControl<Button>("BtnUserManagement");
            if (btnUserManagement != null)
            {
                btnUserManagement.Click += (s, e) =>
                {
                    ShowControl(_settingsControl);
                    ToggleProfileMenu(null, null!);
                };
            }

            // Change Password
            var btnChangePassword = this.FindControl<Button>("BtnChangePassword");
            if (btnChangePassword != null)
            {
                btnChangePassword.Click += async (s, e) => await ShowChangePasswordDialog();
            }

            // Change Username
            var btnChangeUsername = this.FindControl<Button>("BtnChangeUsername");
            if (btnChangeUsername != null)
            {
                btnChangeUsername.Click += async (s, e) => await ShowChangeUsernameDialog();
            }

            // Upload Avatar
            var btnUploadAvatar = this.FindControl<Button>("BtnUploadAvatar");
            if (btnUploadAvatar != null)
            {
                btnUploadAvatar.Click += async (s, e) => await UploadAvatar();
            }

            // Logout
            var btnLogout = this.FindControl<Button>("BtnLogout");
            if (btnLogout != null)
            {
                btnLogout.Click += (s, e) =>
                {
                    _allowClose = true;
                    OnLogoutRequested?.Invoke();
                };
            }
        }

        private async Task ShowChangePasswordDialog()
        {
            var dialog = new ChangePasswordDialog();
            var window = new Window
            {
                Title = "Change Password",
                Content = dialog,
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Background = Avalonia.Media.Brushes.Transparent
            };

            dialog.OnPasswordChanged += (sender, e) =>
            {
                window.Close();
                NotificationService.ShowBackupToast("Profile", "Password changed successfully!", "Success");
            };

            dialog.OnCancel += (sender, e) => window.Close();

            await window.ShowDialog(this);
        }

        private async Task ShowChangeUsernameDialog()
        {
            var dialog = new ChangeUsernameDialog();
            var window = new Window
            {
                Title = "Change Username",
                Content = dialog,
                Width = 400,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Background = Avalonia.Media.Brushes.Transparent
            };

            dialog.OnUsernameChanged += (sender, e) =>
            {
                window.Close();
                NotificationService.ShowBackupToast("Profile", "Username changed successfully!", "Success");
            };

            dialog.OnCancel += (sender, e) => window.Close();

            await window.ShowDialog(this);
        }

        private async Task UploadAvatar()
        {
            var dialog = new UploadAvatarDialog();
            var window = new Window
            {
                Title = "Upload Avatar",
                Content = dialog,
                Width = 400,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Background = Avalonia.Media.Brushes.Transparent
            };

            dialog.OnAvatarUploaded += (sender, filePath) =>
            {
                window.Close();
                NotificationService.ShowBackupToast("Profile", $"Avatar uploaded: {Path.GetFileName(filePath)}", "Success");
                // TODO: Update avatar display in profile
            };

            dialog.OnCancel += (sender, e) => window.Close();

            await window.ShowDialog(this);
        }

        #endregion
    }
}
