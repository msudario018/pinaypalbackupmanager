using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.IO;
using System.Text.Json;
using PinayPalBackupManager.Services;
using PinayPalBackupManager.Models;
using Avalonia.Threading;
using Avalonia.Interactivity;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class SettingsControl : UserControl
    {
        private readonly BackupManager? _manager;
        public event Func<System.Threading.Tasks.Task>? OnShowSystemInfo;
        public event Func<System.Threading.Tasks.Task>? OnCheckUpdates;
        public event Action? OnConfigSaved;
        public event Action? OnLogout;
        private DispatcherTimer? _inviteTimer;
        private DateTime _nextRotateTime;

        public SettingsControl() : this(null) { }
        public SettingsControl(BackupManager? manager)
        {
            _manager = manager;
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            
            var chkStartup = this.FindControl<CheckBox>("ChkStartup")!;
            chkStartup.IsChecked = IsStartupEnabled();
            chkStartup.IsCheckedChanged += ToggleStartup;

            var btnShowInfo = this.FindControl<Button>("BtnShowSystemInfo");
            if (btnShowInfo != null)
            {
                btnShowInfo.Click += async (s, e) => {
                    if (OnShowSystemInfo != null) await OnShowSystemInfo.Invoke();
                };
            }

            var chkAutoUpdate = this.FindControl<CheckBox>("ChkAutoUpdate");
            if (chkAutoUpdate != null)
            {
                chkAutoUpdate.IsChecked = UpdatePreferences.LoadAutoCheckOnStartup();
                chkAutoUpdate.IsCheckedChanged += (s, e) =>
                {
                    UpdatePreferences.SaveAutoCheckOnStartup(chkAutoUpdate.IsChecked == true);
                    NotificationService.ShowBackupToast("Updates", chkAutoUpdate.IsChecked == true ? "Auto-check enabled." : "Auto-check disabled.", "Info");
                };
            }

            var btnCheckUpdates = this.FindControl<Button>("BtnCheckUpdates");
            if (btnCheckUpdates != null)
            {
                btnCheckUpdates.Click += async (s, e) =>
                {
                    if (OnCheckUpdates != null) await OnCheckUpdates.Invoke();
                };
            }

            // Set version dynamically
            var txtVersion = this.FindControl<TextBlock>("TxtVersion");
            if (txtVersion != null) txtVersion.Text = BackupConfig.AppVersion;

            LoadConfigEditor();

            var btnSaveConfig = this.FindControl<Button>("BtnSaveConfig");
            if (btnSaveConfig != null)
            {
                btnSaveConfig.Click += async (s, e) =>
                {
                    await SaveConfigEditorAsync();
                };
            }

            InitAdminPanel();

            this.FindControl<Button>("BtnDiagnostics")!.Click += async (s, e) => {
                var txtStatus = this.FindControl<TextBlock>("TxtHealthStatus")!;
                txtStatus.Text = "Status: Running System Scan...";
                NotificationService.ShowBackupToast("Diagnostics", "Running system scan...", "Info");
                
                if (_manager != null)
                {
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<System.Collections.Generic.List<BackupHealthReport>>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                    void Handler(System.Collections.Generic.List<BackupHealthReport> reports)
                    {
                        _manager.OnHealthUpdate -= Handler;
                        tcs.TrySetResult(reports);
                    }

                    _manager.OnHealthUpdate += Handler;
                    await _manager.RunHealthCheckAsync();
                    var reports = await tcs.Task;

                    var outdated = reports
                        .Where(r => !string.Equals(r.Color, "LimeGreen", StringComparison.OrdinalIgnoreCase))
                        .Select(r => r.Service)
                        .ToArray();

                    txtStatus.Text = outdated.Length == 0
                        ? "Status: OK (Website, Mailchimp, SQL)"
                        : $"Status: OUTDATED ({string.Join(", ", outdated)})";

                    NotificationService.ShowBackupToast("Diagnostics", txtStatus.Text.Replace("Status: ", ""), outdated.Length == 0 ? "Info" : "Warning");
                }
            };
        }

        private void InitAdminPanel()
        {
            var adminPanel = this.FindControl<Border>("AdminPanel");
            if (adminPanel == null) return;

            var isAdmin = AuthService.IsAdmin;
            adminPanel.IsVisible = isAdmin; // only visible to admins

            var txtLoggedIn = this.FindControl<TextBlock>("TxtLoggedInUser");
            if (txtLoggedIn != null && AuthService.CurrentUser != null)
                txtLoggedIn.Text = $"{AuthService.CurrentUser.Username} ({AuthService.CurrentUser.Role})";

            // Invite code with auto-rotation (admin only)
            var txtInviteCode = this.FindControl<TextBox>("TxtInviteCode");
            var btnCopy = this.FindControl<Button>("BtnCopyCode");
            var btnRegenerate = this.FindControl<Button>("BtnRegenerateCode");

            if (isAdmin)
            {
                var code = AuthService.GetInviteCode();
                if (txtInviteCode != null) txtInviteCode.Text = string.IsNullOrEmpty(code) ? "(none)" : code;
                
                // Setup copy button
                if (btnCopy != null)
                {
                    btnCopy.IsVisible = true;
                    btnCopy.Click += async (_, _) =>
                    {
                        if (txtInviteCode != null && !string.IsNullOrEmpty(txtInviteCode.Text) && txtInviteCode.Text != "(none)")
                        {
                            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                            if (clipboard != null)
                                await clipboard.SetTextAsync(txtInviteCode.Text);
                            NotificationService.ShowBackupToast("Users", "Invite code copied to clipboard.", "Info");
                        }
                    };
                }
                
                // Setup regenerate button
                if (btnRegenerate != null)
                {
                    btnRegenerate.IsVisible = true;
                    btnRegenerate.Click += (_, _) =>
                    {
                        var newCode = AuthService.RotateInviteCode();
                        if (txtInviteCode != null) txtInviteCode.Text = newCode;
                        _nextRotateTime = DateTime.UtcNow.AddMinutes(5);
                        NotificationService.ShowBackupToast("Users", "Invite code regenerated!", "Success");
                    };
                }

                // Start auto-rotation timer
                _nextRotateTime = DateTime.UtcNow.AddMinutes(5);
                _inviteTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _inviteTimer.Tick += (_, _) =>
                {
                    var remaining = _nextRotateTime - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        var newCode = AuthService.RotateInviteCode();
                        if (txtInviteCode != null) txtInviteCode.Text = newCode;
                        _nextRotateTime = DateTime.UtcNow.AddMinutes(5);
                        NotificationService.ShowBackupToast("Users", "Invite code auto-rotated.", "Info");
                    }
                    UpdateTimerArc(remaining.TotalSeconds / 300.0); // 300 seconds = 5 minutes
                };
                _inviteTimer.Start();
                UpdateTimerArc(1.0);
            }
            else
            {
                if (txtInviteCode != null) { txtInviteCode.Text = "(admin only)"; txtInviteCode.IsEnabled = false; }
                if (btnCopy != null) btnCopy.IsVisible = false;
            }

            RefreshUserList();

            var btnLogout = this.FindControl<Button>("BtnLogout");
            if (btnLogout != null)
            {
                btnLogout.Click += (_, _) =>
                {
                    AuthService.Logout();
                    OnLogout?.Invoke();
                };
            }
        }

        private void RefreshUserList()
        {
            var userListPanel = this.FindControl<StackPanel>("UserListPanel");
            var txtNoUsers = this.FindControl<TextBlock>("TxtNoUsers");
            if (userListPanel == null) return;

            userListPanel.Children.Clear();

            var users = AuthService.GetAllUsers();
            var otherUsers = users.Where(u => u.Id != (AuthService.CurrentUser?.Id ?? -1)).ToList();

            if (txtNoUsers != null) txtNoUsers.IsVisible = otherUsers.Count == 0;

            foreach (var user in otherUsers)
            {
                var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };

                var statusColor = user.Status == "Active" ? "#A6E3A1" : user.Status == "Disabled" ? "#F38BA8" : "#F9E2AF";
                row.Children.Add(new TextBlock
                {
                    Text = $"{user.Username} — {user.Role} — {user.Status}",
                    Foreground = Avalonia.Media.Brush.Parse(statusColor),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    FontSize = 12,
                    Width = 240
                });

                if (AuthService.IsAdmin)
                {
                    if (user.Status == "Active")
                    {
                        var btnDisable = new Button { Content = "Disable", FontSize = 10, Padding = new Avalonia.Thickness(8, 4), Background = Avalonia.Media.Brush.Parse("#F9E2AF"), Foreground = Avalonia.Media.Brush.Parse("#0B0F17"), CornerRadius = new Avalonia.CornerRadius(6) };
                        var uid = user.Id;
                        btnDisable.Click += (_, _) => { AuthService.SetUserStatus(uid, "Disabled"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User disabled.", "Warning"); };
                        row.Children.Add(btnDisable);
                    }
                    else if (user.Status == "Disabled")
                    {
                        var btnEnable = new Button { Content = "Enable", FontSize = 10, Padding = new Avalonia.Thickness(8, 4), Background = Avalonia.Media.Brush.Parse("#A6E3A1"), Foreground = Avalonia.Media.Brush.Parse("#0B0F17"), CornerRadius = new Avalonia.CornerRadius(6) };
                        var uid = user.Id;
                        btnEnable.Click += (_, _) => { AuthService.SetUserStatus(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User enabled.", "Info"); };
                        row.Children.Add(btnEnable);
                    }

                    var btnDelete = new Button { Content = "Delete", FontSize = 10, Padding = new Avalonia.Thickness(8, 4), Background = Avalonia.Media.Brush.Parse("#F38BA8"), Foreground = Avalonia.Media.Brush.Parse("#0B0F17"), CornerRadius = new Avalonia.CornerRadius(6) };
                    var deleteId = user.Id;
                    btnDelete.Click += (_, _) => { AuthService.DeleteUser(deleteId); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User deleted.", "Warning"); };
                    row.Children.Add(btnDelete);
                }

                userListPanel.Children.Add(row);
            }
        }

        private static bool IsStartupEnabled()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
            return CheckRegistryStartup;
        }

        private void LoadConfigEditor()
        {
            var s = ConfigService.Current;

            this.FindControl<TextBox>("TxtFtpLocalFolder")!.Text = s.Paths.FtpLocalFolder;
            this.FindControl<TextBox>("TxtMailchimpFolder")!.Text = s.Paths.MailchimpFolder;
            this.FindControl<TextBox>("TxtSqlLocalFolder")!.Text = s.Paths.SqlLocalFolder;

            // Shared TLS fingerprint (prefer FTP value, fall back to SQL)
            var sharedTls = !string.IsNullOrWhiteSpace(s.Ftp.TlsFingerprint) ? s.Ftp.TlsFingerprint : s.Sql.TlsFingerprint;
            this.FindControl<TextBox>("TxtSharedTls")!.Text = sharedTls;

            this.FindControl<TextBox>("TxtFtpHost")!.Text = s.Ftp.Host;
            this.FindControl<TextBox>("TxtFtpUser")!.Text = s.Ftp.User;
            this.FindControl<TextBox>("TxtFtpPassword")!.Text = s.Ftp.Password;
            this.FindControl<TextBox>("TxtFtpPort")!.Text = s.Ftp.Port.ToString();

            this.FindControl<TextBox>("TxtSqlUser")!.Text = s.Sql.User;
            this.FindControl<TextBox>("TxtSqlPassword")!.Text = s.Sql.Password;
            this.FindControl<TextBox>("TxtSqlRemotePath")!.Text = s.Sql.RemotePath;

            this.FindControl<TextBox>("TxtMcApiKey")!.Text = s.Mailchimp.ApiKey;
            this.FindControl<TextBox>("TxtMcAudienceId")!.Text = s.Mailchimp.AudienceId;
        }

        private async System.Threading.Tasks.Task SaveConfigEditorAsync()
        {
            var status = this.FindControl<TextBlock>("TxtConfigStatus");
            if (status != null) status.Text = "Saving...";

            var config = new AppSettings
            {
                Paths = new PathsSettings
                {
                    FtpLocalFolder = this.FindControl<TextBox>("TxtFtpLocalFolder")!.Text ?? string.Empty,
                    MailchimpFolder = this.FindControl<TextBox>("TxtMailchimpFolder")!.Text ?? string.Empty,
                    SqlLocalFolder = this.FindControl<TextBox>("TxtSqlLocalFolder")!.Text ?? string.Empty,
                },
                Ftp = new FtpSettings
                {
                    Host = this.FindControl<TextBox>("TxtFtpHost")!.Text ?? string.Empty,
                    User = this.FindControl<TextBox>("TxtFtpUser")!.Text ?? string.Empty,
                    Password = this.FindControl<TextBox>("TxtFtpPassword")!.Text ?? string.Empty,
                    TlsFingerprint = this.FindControl<TextBox>("TxtSharedTls")!.Text ?? string.Empty,
                    Port = int.TryParse(this.FindControl<TextBox>("TxtFtpPort")!.Text, out var p) ? p : 21
                },
                Sql = new SqlSettings
                {
                    User = this.FindControl<TextBox>("TxtSqlUser")!.Text ?? string.Empty,
                    Password = this.FindControl<TextBox>("TxtSqlPassword")!.Text ?? string.Empty,
                    RemotePath = this.FindControl<TextBox>("TxtSqlRemotePath")!.Text ?? string.Empty,
                    TlsFingerprint = this.FindControl<TextBox>("TxtSharedTls")!.Text ?? string.Empty,
                },
                Mailchimp = new MailchimpSettings
                {
                    ApiKey = this.FindControl<TextBox>("TxtMcApiKey")!.Text ?? string.Empty,
                    AudienceId = this.FindControl<TextBox>("TxtMcAudienceId")!.Text ?? string.Empty,
                },
                Schedule = ConfigService.Current.Schedule
            };

            try
            {
                var dir = ConfigService.GetConfigDirectory();
                var path = Path.Combine(dir, "appsettings.local.json");
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, json);

                ConfigService.Load();
                NotificationService.ShowBackupToast("Config", "Saved appsettings.local.json", "Info");
                if (status != null) status.Text = "Saved.";
                OnConfigSaved?.Invoke();
            }
            catch (Exception ex)
            {
                NotificationService.ShowBackupToast("Config", "Save failed.", "Error");
                if (status != null) status.Text = ex.Message;
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool CheckRegistryStartup
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                    return key?.GetValue("PinaypalBackupManager") != null;
                }
                catch { return false; }
            }
        }

        private void ToggleStartup(object? sender, EventArgs e)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            UpdateRegistryStartup();
            NotificationService.ShowBackupToast("Startup", this.FindControl<CheckBox>("ChkStartup")?.IsChecked == true ? "Enabled." : "Disabled.", "Info");
        }

        [SupportedOSPlatform("windows")]
        private void UpdateRegistryStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (this.FindControl<CheckBox>("ChkStartup")?.IsChecked == true)
                {
                    key.SetValue("PinaypalBackupManager", $"\"{AppDomain.CurrentDomain.BaseDirectory}PinayPalBackupManager.exe\"");
                }
                else
                {
                    key.DeleteValue("PinaypalBackupManager", false);
                }
            }
            catch { }
        }

        private void UpdateTimerArc(double progress)
        {
            var timerArc = this.FindControl<Avalonia.Controls.Shapes.Path>("TimerArc");
            if (timerArc == null) return;

            // Clamp progress between 0 and 1
            progress = Math.Clamp(progress, 0.0, 1.0);
            
            // Create an arc from top (-90°) clockwise
            const double radius = 7;
            const double centerX = 8;
            const double centerY = 8;
            const double startAngle = -90;
            double endAngle = startAngle + (360 * progress);

            // Convert angles to radians
            double startRad = startAngle * Math.PI / 180.0;
            double endRad = endAngle * Math.PI / 180.0;

            // Calculate start and end points
            double startX = centerX + radius * Math.Cos(startRad);
            double startY = centerY + radius * Math.Sin(startRad);
            double endX = centerX + radius * Math.Cos(endRad);
            double endY = centerY + radius * Math.Sin(endRad);

            // Determine if the arc is large (>180°)
            bool isLargeArc = progress > 0.5;

            // Create the path data
            string pathData = $"M {startX},{startY} A {radius},{radius} 0 {(isLargeArc ? 1 : 0)},1 {endX},{endY}";
            timerArc.Data = Geometry.Parse(pathData);
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            _inviteTimer?.Stop();
            _inviteTimer = null;
        }
    }
}
