using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.IO;
using System.IO.Compression;
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

        public SettingsControl() : this(null) { }
        public SettingsControl(BackupManager? manager)
        {
            _manager = manager;
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            
            var chkStartup = this.FindControl<CheckBox>("ChkStartup")!;
            chkStartup.IsChecked = IsStartupEnabled();
            chkStartup.IsCheckedChanged += ToggleStartup;

            // Load Start Minimized setting
            var chkStartMinimized = this.FindControl<CheckBox>("ChkStartMinimized");
            if (chkStartMinimized != null)
            {
                chkStartMinimized.IsChecked = ConfigService.Current.Operation.StartMinimized;
                chkStartMinimized.IsCheckedChanged += (_, _) => 
                {
                    ConfigService.Current.Operation.StartMinimized = chkStartMinimized.IsChecked == true;
                    ConfigService.SaveOperation();
                    NotificationService.ShowBackupToast("Settings", "Start minimized " + (chkStartMinimized.IsChecked == true ? "enabled" : "disabled"), "Info");
                };
            }

            // Load Notification Sound setting
            var chkNotificationSound = this.FindControl<CheckBox>("ChkNotificationSound");
            if (chkNotificationSound != null)
            {
                chkNotificationSound.IsChecked = ConfigService.Current.Operation.NotificationSound;
                chkNotificationSound.IsCheckedChanged += (_, _) => 
                {
                    ConfigService.Current.Operation.NotificationSound = chkNotificationSound.IsChecked == true;
                    ConfigService.SaveOperation();
                    NotificationService.ShowBackupToast("Settings", "Notification sound " + (chkNotificationSound.IsChecked == true ? "enabled" : "muted"), "Info");
                };
            }

            
            // Load Language setting
            var cmbLanguage = this.FindControl<ComboBox>("CmbLanguage");
            if (cmbLanguage != null)
            {
                cmbLanguage.SelectedIndex = ConfigService.Current.Operation.Language == "fil" ? 1 : 0;
                cmbLanguage.SelectionChanged += (_, _) => 
                {
                    var lang = cmbLanguage.SelectedIndex == 1 ? "fil" : "en";
                    System.Diagnostics.Debug.WriteLine($"[Language] Changing to: {lang} (index: {cmbLanguage.SelectedIndex})");
                    ConfigService.Current.Operation.Language = lang;
                    ConfigService.SaveOperation();
                    Services.LocalizationService.SetLanguage(lang);
                    NotificationService.ShowBackupToast("Settings", "Language changed to " + Services.LocalizationService.GetLanguageName(lang), "Info");
                };
            }

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
                    var status = chkAutoUpdate.IsChecked == true ? "enabled" : "disabled";
                    NotificationService.ShowBackupToast("Updates", $"Auto-check {status}.", "Info");
                    LogService.WriteSystemLog($"Auto-update check on startup {status}", "Information", "SETTINGS");
                };
            }

            var btnCheckUpdates = this.FindControl<Button>("BtnCheckUpdates");
            if (btnCheckUpdates != null)
            {
                btnCheckUpdates.Click += async (s, e) =>
                {
                    NotificationService.ShowBackupToast("Updates", "Checking for updates...", "Info");
                    if (OnCheckUpdates != null) await OnCheckUpdates.Invoke();
                };
            }

            // Retention days
            var txtRetention = this.FindControl<TextBox>("TxtRetentionDays");
            if (txtRetention != null) txtRetention.Text = ConfigService.Current.Operation.RetentionDays.ToString();
            var btnSaveRetention = this.FindControl<Button>("BtnSaveRetention");
            if (btnSaveRetention != null) btnSaveRetention.Click += (_, _) =>
            {
                if (int.TryParse(txtRetention?.Text?.Trim(), out int days) && days >= 1 && days <= 365)
                {
                    ConfigService.Current.Operation.RetentionDays = days;
                    ConfigService.SaveOperation();
                    ConfigService.Load();
                    NotificationService.ShowBackupToast("Retention", $"Backup files older than {days} day(s) will be deleted automatically.", "Info");
                    LogService.WriteSystemLog($"Retention days changed to {days} days", "Information", "SETTINGS");
                }
                else NotificationService.ShowBackupToast("Retention", "Enter a value between 1 and 365 days.", "Warning");
            };

            // Export Logs
            var btnExportLogs = this.FindControl<Button>("BtnExportLogs");
            if (btnExportLogs != null)
            {
                btnExportLogs.Click += async (_, _) => await ExportLogsAsync();
            }

            // Set version dynamically
            var txtVersion = this.FindControl<TextBlock>("TxtVersion");
            if (txtVersion != null) txtVersion.Text = BackupConfig.AppVersion;

            // Dialog buttons for credentials and paths
            var btnEditCredentials = this.FindControl<Button>("BtnEditCredentials");
            if (btnEditCredentials != null)
            {
                btnEditCredentials.Click += async (s, e) => await ShowCredentialsDialogAsync();
            }

            var btnEditPaths = this.FindControl<Button>("BtnEditPaths");
            if (btnEditPaths != null)
            {
                btnEditPaths.Click += async (s, e) => await ShowPathsDialogAsync();
            }

            var btnEditNetworkDrive = this.FindControl<Button>("BtnEditNetworkDrive");
            if (btnEditNetworkDrive != null)
            {
                btnEditNetworkDrive.Click += async (s, e) => await ShowNetworkDriveDialogAsync();
            }

            // Per-service Run Now buttons
            var btnRunFtp = this.FindControl<Button>("BtnRunFtp");
            if (btnRunFtp != null)
                btnRunFtp.Click += (_, _) => { _manager?.TriggerFtpBackup(); NotificationService.ShowBackupToast("FTP", "Backup triggered — check FTP tab.", "Info"); };

            var btnRunMailchimp = this.FindControl<Button>("BtnRunMailchimp");
            if (btnRunMailchimp != null)
                btnRunMailchimp.Click += (_, _) => { _manager?.TriggerMailchimpBackup(); NotificationService.ShowBackupToast("Mailchimp", "Backup triggered — check Mailchimp tab.", "Info"); };

            var btnRunSql = this.FindControl<Button>("BtnRunSql");
            if (btnRunSql != null)
                btnRunSql.Click += (_, _) => { _manager?.TriggerSqlBackup(); NotificationService.ShowBackupToast("SQL", "Backup triggered — check SQL tab.", "Info"); };

            // Populate service status on load
            RefreshServiceHealthCards();

            var btnDiagnostics = this.FindControl<Button>("BtnDiagnostics");
            if (btnDiagnostics != null)
            {
                btnDiagnostics.Click += async (s, e) => {
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
                    RefreshServiceHealthCards();
                }
            };
            }

            // Management Tools buttons
            var btnHealthCheck = this.FindControl<Button>("BtnHealthCheck");
            if (btnHealthCheck != null)
            {
                btnHealthCheck.Click += async (s, e) => await ShowHealthCheckDialogAsync();
            }

            var btnErrorReports = this.FindControl<Button>("BtnErrorReports");
            if (btnErrorReports != null)
            {
                btnErrorReports.Click += async (s, e) => await ShowErrorReportsDialogAsync();
            }

            var btnPerformance = this.FindControl<Button>("BtnPerformance");
            if (btnPerformance != null)
            {
                btnPerformance.Click += async (s, e) => await ShowPerformanceDialogAsync();
            }

            var btnBackupHistory = this.FindControl<Button>("BtnBackupHistory");
            if (btnBackupHistory != null)
            {
                btnBackupHistory.Click += async (s, e) => await ShowBackupHistoryDialogAsync();
            }

            var btnBackupSchedules = this.FindControl<Button>("BtnBackupSchedules");
            if (btnBackupSchedules != null)
            {
                btnBackupSchedules.Click += async (s, e) => await ShowBackupSchedulesDialogAsync();
            }

            var btnResetAllUsers = this.FindControl<Button>("BtnResetAllUsers");
            if (btnResetAllUsers != null)
            {
                btnResetAllUsers.Click += async (_, _) => await ShowResetAllUsersConfirmationAsync();
            }
        }

        /// <summary>
        /// Updates the health status label (called from MainWindow during initialization)
        /// </summary>
        public void UpdateHealthStatus(string status, bool isError = false)
        {
            var txtStatus = this.FindControl<TextBlock>("TxtHealthStatus");
            if (txtStatus != null)
            {
                txtStatus.Text = $"Status: {status}";
                txtStatus.Foreground = isError 
                    ? Avalonia.Media.Brush.Parse("#F38BA8") 
                    : Avalonia.Application.Current?.FindResource("AppSubtext") as Avalonia.Media.Brush;
            }
        }

        private static bool IsStartupEnabled()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
            return CheckRegistryStartup;
        }

        private async System.Threading.Tasks.Task ShowCredentialsDialogAsync()
        {
            const string dialogKey = "credentials_dialog";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var dialog = new CredentialsDialog();
                var window = new Window
                {
                    Title = "Edit Credentials",
                    Content = dialog,
                    Width = 500,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brushes.Transparent,
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                dialog.OnSave += async (sender, e) =>
                {
                    await SaveSettingsAsync(dialog.GetSettings(), "Credentials saved.");
                    LogService.WriteSystemLog("Credentials updated", "Information", "SETTINGS");
                    window.Close();
                };

                dialog.OnCancel += (sender, e) => window.Close();

                await window.ShowDialog(parentWindow!);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task ShowPathsDialogAsync()
        {
            const string dialogKey = "paths_dialog";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var dialog = new PathsDialog();
                var window = new Window
                {
                    Title = "Edit Backup Paths",
                    Content = dialog,
                    Width = 500,
                    Height = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brushes.Transparent,
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                dialog.OnSave += async (sender, e) =>
                {
                    await SaveSettingsAsync(dialog.GetSettings(), "Paths saved.");
                    LogService.WriteSystemLog("Backup paths updated", "Information", "SETTINGS");
                    window.Close();
                };

                dialog.OnCancel += (sender, e) => window.Close();

                await window.ShowDialog(parentWindow!);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task ShowNetworkDriveDialogAsync()
        {
            const string dialogKey = "networkdrive_dialog";
            if (NotificationService.IsDialogOpen(dialogKey)) return;

            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var dialog = new NetworkDriveDialog();
                var window = new Window
                {
                    Title = "Network Drive Backup",
                    Content = dialog,
                    Width = 500,
                    Height = 420,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brushes.Transparent,
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                dialog.OnSave += async (sender, e) =>
                {
                    await SaveSettingsAsync(dialog.GetSettings(), "Network drive settings saved.");
                    LogService.WriteSystemLog("Network drive settings updated", "Information", "SETTINGS");
                    window.Close();
                };

                dialog.OnCancel += (sender, e) => window.Close();

                await window.ShowDialog(parentWindow!);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task ShowResetAllUsersConfirmationAsync()
        {
            const string dialogKey = "reset_users_confirm";
            if (NotificationService.IsDialogOpen(dialogKey)) return;

            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var dialog = new ConfirmDialog("Reset All Users", "This will permanently delete ALL users from the local database and Firebase.\n\nYou will need to create a new admin account afterwards.\n\nAre you sure?");

                var window = new Window
                {
                    Title = "Confirm Reset",
                    Content = dialog,
                    Width = 420,
                    Height = 280,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brushes.Transparent,
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                bool confirmed = false;

                dialog.OnResult += (_, result) =>
                {
                    confirmed = result;
                    window.Close();
                };

                await window.ShowDialog(parentWindow!);

                if (!confirmed) return;

                NotificationService.ShowBackupToast("Users", "Resetting all users...", "Warning");

                // Clear Firebase first
                var firebaseCleared = await FirebaseUserService.ClearAllUsersAsync();
                
                // Clear local DB
                var (localSuccess, localMessage) = AuthService.ResetAllUsers();
                
                if (localSuccess)
                {
                    NotificationService.ShowBackupToast("Users", $"All users reset. Firebase: {(firebaseCleared ? "cleared" : "failed")}", "Info");
                    LogService.WriteSystemLog("All users reset from local DB and Firebase", "Warning", "SETTINGS");
                    
                    // Clear session and log out
                    SessionService.ClearSession();
                    
                    // Show message that app needs restart
                    var restartDialog = new ConfirmDialog("Restart Required", "All users have been reset.\n\nThe application will now close. Please restart to create a new admin account.");
                    var restartWindow = new Window
                    {
                        Title = "Restart",
                        Content = restartDialog,
                        Width = 380,
                        Height = 220,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        CanResize = false,
                        ShowInTaskbar = false,
                        Topmost = true,
                        Background = Avalonia.Media.Brushes.Transparent,
                        ExtendClientAreaToDecorationsHint = true,
                        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome
                    };
                    restartDialog.OnResult += (_, _) =>
                    {
                        restartWindow.Close();
                        Environment.Exit(0);
                    };
                    await restartWindow.ShowDialog(parentWindow!);
                }
                else
                {
                    NotificationService.ShowBackupToast("Users", localMessage, "Error");
                }
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task SaveSettingsAsync(AppSettings config, string successMessage)
        {
            var status = this.FindControl<TextBlock>("TxtConfigStatus");
            if (status != null) status.Text = "Saving...";

            try
            {
                var dir = ConfigService.GetConfigDirectory();
                var path = System.IO.Path.Combine(dir, "appsettings.local.json");
                
                // Read existing config to preserve other settings
                AppSettings existing;
                if (File.Exists(path))
                {
                    var existingJson = await File.ReadAllTextAsync(path);
                    existing = JsonSerializer.Deserialize<AppSettings>(existingJson) ?? new AppSettings();
                }
                else
                {
                    existing = new AppSettings();
                }
                
                // Merge new config into existing (this preserves settings not being changed)
                MergeSettings(existing, config);
                
                var json = JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, json);

                ConfigService.Load();
                NotificationService.ShowBackupToast("Config", successMessage, "Info");
                if (status != null) status.Text = "Saved.";
                OnConfigSaved?.Invoke();
                
                // Log the save operation
                LogService.WriteSystemLog($"Configuration saved: {successMessage}", "Information", "SETTINGS");
            }
            catch (Exception ex)
            {
                NotificationService.ShowBackupToast("Config", "Save failed.", "Error");
                if (status != null) status.Text = ex.Message;
                LogService.WriteSystemLog($"Configuration save failed: {ex.Message}", "Error", "SETTINGS");
            }
        }

        private void RefreshServiceHealthCards()
        {
            try
            {
                RefreshCard("Ftp", BackupConfig.FtpLogFile, "DiagFtpDot", "DiagFtpStatus", "DiagFtpTime");
                RefreshCard("Mailchimp", BackupConfig.McLogFile, "DiagMcDot", "DiagMcStatus", "DiagMcTime");
                RefreshCard("Sql", BackupConfig.SqlLogFile, "DiagSqlDot", "DiagSqlStatus", "DiagSqlTime");
            }
            catch { }
        }

        private void RefreshCard(string service, string logFile, string dotName, string statusName, string timeName)
        {
            var dot = this.FindControl<Avalonia.Controls.Shapes.Ellipse>(dotName);
            var status = this.FindControl<TextBlock>(statusName);
            var time = this.FindControl<TextBlock>(timeName);
            if (dot == null || status == null || time == null) return;

            try
            {
                if (!File.Exists(logFile)) { dot.Fill = Avalonia.Media.Brush.Parse("#6C7086"); status.Text = "No log file"; time.Text = "—"; return; }
                var logs = LogService.ImportLatestLogs(logFile, 100).ToList();
                DateTime? lastSuccess = null;
                foreach (var log in logs)
                {
                    var up = log.ToUpperInvariant();
                    if (up.Contains("COMPLETE") || up.Contains("SUCCESS") || up.Contains("DOWNLOAD COMPLETE"))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(log, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?: [AP]M)?)\]");
                        if (m.Success && DateTime.TryParse(m.Groups[1].Value, out var t)) { lastSuccess = t; break; }
                    }
                }

                if (!lastSuccess.HasValue)
                {
                    dot.Fill = Avalonia.Media.Brush.Parse("#6C7086");
                    status.Text = "No successful backup found";
                    time.Text = "—";
                    return;
                }

                var age = DateTime.Now - lastSuccess.Value;
                bool stale = age.TotalHours > 48;
                dot.Fill = Avalonia.Media.Brush.Parse(stale ? "#e6c55c" : "#52B788");
                status.Text = stale ? $"Stale ({(int)age.TotalHours}h ago)" : "OK";
                time.Text = lastSuccess.Value.ToString("MM/dd HH:mm");
            }
            catch
            {
                dot.Fill = Avalonia.Media.Brush.Parse("#6C7086");
                status.Text = "Error reading log";
                time.Text = "—";
            }
        }

        private void MergeSettings(AppSettings target, AppSettings source)
        {
            if (!string.IsNullOrWhiteSpace(source.Paths.FtpLocalFolder)) target.Paths.FtpLocalFolder = source.Paths.FtpLocalFolder;
            if (!string.IsNullOrWhiteSpace(source.Paths.MailchimpFolder)) target.Paths.MailchimpFolder = source.Paths.MailchimpFolder;
            if (!string.IsNullOrWhiteSpace(source.Paths.SqlLocalFolder)) target.Paths.SqlLocalFolder = source.Paths.SqlLocalFolder;

            if (!string.IsNullOrWhiteSpace(source.Ftp.Host)) target.Ftp.Host = source.Ftp.Host;
            if (!string.IsNullOrWhiteSpace(source.Ftp.User)) target.Ftp.User = source.Ftp.User;
            if (!string.IsNullOrWhiteSpace(source.Ftp.Password)) target.Ftp.Password = source.Ftp.Password;
            if (!string.IsNullOrWhiteSpace(source.Ftp.TlsFingerprint)) target.Ftp.TlsFingerprint = source.Ftp.TlsFingerprint;
            if (source.Ftp.Port != 0) target.Ftp.Port = source.Ftp.Port;

            if (!string.IsNullOrWhiteSpace(source.Sql.Host)) target.Sql.Host = source.Sql.Host;
            if (!string.IsNullOrWhiteSpace(source.Sql.User)) target.Sql.User = source.Sql.User;
            if (!string.IsNullOrWhiteSpace(source.Sql.Password)) target.Sql.Password = source.Sql.Password;
            if (!string.IsNullOrWhiteSpace(source.Sql.RemotePath)) target.Sql.RemotePath = source.Sql.RemotePath;
            if (!string.IsNullOrWhiteSpace(source.Sql.TlsFingerprint)) target.Sql.TlsFingerprint = source.Sql.TlsFingerprint;

            if (!string.IsNullOrWhiteSpace(source.Mailchimp.ApiKey)) target.Mailchimp.ApiKey = source.Mailchimp.ApiKey;
            if (!string.IsNullOrWhiteSpace(source.Mailchimp.AudienceId)) target.Mailchimp.AudienceId = source.Mailchimp.AudienceId;

            target.NetworkDrive.Enabled = source.NetworkDrive.Enabled;
            if (!string.IsNullOrWhiteSpace(source.NetworkDrive.Path)) target.NetworkDrive.Path = source.NetworkDrive.Path;
            if (!string.IsNullOrWhiteSpace(source.NetworkDrive.Username)) target.NetworkDrive.Username = source.NetworkDrive.Username;
            if (!string.IsNullOrWhiteSpace(source.NetworkDrive.Password)) target.NetworkDrive.Password = source.NetworkDrive.Password;
            if (!string.IsNullOrWhiteSpace(source.Paths.NetworkDriveFolder)) target.Paths.NetworkDriveFolder = source.Paths.NetworkDriveFolder;

            if (source.Operation.RetentionDays > 0) target.Operation.RetentionDays = source.Operation.RetentionDays;
            target.Operation.AutoStartWindows = source.Operation.AutoStartWindows;

            target.Schedule.FtpDailySyncHourMnl = source.Schedule.FtpDailySyncHourMnl;
            target.Schedule.FtpDailySyncMinuteMnl = source.Schedule.FtpDailySyncMinuteMnl;
            target.Schedule.MailchimpDailySyncHourMnl = source.Schedule.MailchimpDailySyncHourMnl;
            target.Schedule.MailchimpDailySyncMinuteMnl = source.Schedule.MailchimpDailySyncMinuteMnl;
            target.Schedule.SqlDailySyncHourMnl = source.Schedule.SqlDailySyncHourMnl;
            target.Schedule.SqlDailySyncMinuteMnl = source.Schedule.SqlDailySyncMinuteMnl;
            target.Schedule.FtpAutoScanHours = source.Schedule.FtpAutoScanHours;
            target.Schedule.FtpAutoScanMinutes = source.Schedule.FtpAutoScanMinutes;
            target.Schedule.MailchimpAutoScanHours = source.Schedule.MailchimpAutoScanHours;
            target.Schedule.MailchimpAutoScanMinutes = source.Schedule.MailchimpAutoScanMinutes;
            target.Schedule.SqlAutoScanHours = source.Schedule.SqlAutoScanHours;
            target.Schedule.SqlAutoScanMinutes = source.Schedule.SqlAutoScanMinutes;
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
            var status = this.FindControl<CheckBox>("ChkStartup")?.IsChecked == true ? "enabled" : "disabled";
            NotificationService.ShowBackupToast("Startup", this.FindControl<CheckBox>("ChkStartup")?.IsChecked == true ? "Enabled." : "Disabled.", "Info");
            LogService.WriteSystemLog($"Windows startup {status}", "Information", "SETTINGS");
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
        
        private async Task ExportLogsAsync()
        {
            try
            {
                var tempZip = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pinaypal-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                
                using var zipStream = new System.IO.FileStream(tempZip, System.IO.FileMode.Create);
                using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create);
                
                var logDirs = new[]
                {
                    EnvironmentConfigService.GetLogPath(),
                    System.IO.Path.Combine(AppContext.BaseDirectory, "logs")
                };
                
                foreach (var logDir in logDirs)
                {
                    if (System.IO.Directory.Exists(logDir))
                    {
                        foreach (var file in System.IO.Directory.GetFiles(logDir, "*.log", System.IO.SearchOption.AllDirectories))
                        {
                            var entryName = file.Substring(logDir.Length + 1).Replace('\\', '/');
                            archive.CreateEntryFromFile(file, "logs/" + entryName);
                        }
                    }
                }
                
                // Add config info
                var configEntry = archive.CreateEntry("config-info.txt");
                using var writer = new System.IO.StreamWriter(configEntry.Open());
                await writer.WriteLineAsync($"PinayPal Backup Manager - Log Export");
                await writer.WriteLineAsync($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                await writer.WriteLineAsync($"Version: {BackupConfig.AppVersion}");
                await writer.WriteLineAsync($"");
                await writer.WriteLineAsync($"FTP Log: {BackupConfig.FtpLogFile}");
                await writer.WriteLineAsync($"Mailchimp Log: {BackupConfig.McLogFile}");
                await writer.WriteLineAsync($"SQL Log: {BackupConfig.SqlLogFile}");
                
                NotificationService.ShowBackupToast("Export", "Logs exported successfully!", "Info");
                LogService.WriteSystemLog($"Logs exported to {tempZip}", "Information", "SETTINGS");
                
                // Open the folder containing the zip
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{tempZip}\"");
            }
            catch (Exception ex)
            {
                NotificationService.ShowBackupToast("Export", $"Failed to export logs: {ex.Message}", "Error");
            }
        }

        private async System.Threading.Tasks.Task ShowHealthCheckDialogAsync()
        {
            const string dialogKey = "healthcheck_dialog";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var control = new HealthCheckControl();
                var window = new Window
                {
                    Title = "System Health Check",
                    Content = control,
                    Width = 600,
                    Height = 800,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.BorderOnly
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                await window.ShowDialog(parentWindow!);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task ShowErrorReportsDialogAsync()
        {
            const string dialogKey = "errorreports_dialog";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var control = new ErrorReportViewerControl();
                var window = new Window
                {
                    Title = "Error Reports",
                    Content = control,
                    Width = 700,
                    Height = 900,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.BorderOnly
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                await window.ShowDialog(parentWindow!);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task ShowPerformanceDialogAsync()
        {
            const string dialogKey = "performance_dialog";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var control = new PerformanceMetricsControl();
                var window = new Window
                {
                    Title = "Performance Metrics",
                    Content = control,
                    Width = 600,
                    Height = 800,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.BorderOnly
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                await window.ShowDialog(parentWindow!);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task ShowBackupHistoryDialogAsync()
        {
            const string dialogKey = "backuphistory_dialog";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var control = new BackupHistoryControl();
                var window = new Window
                {
                    Title = "Backup History",
                    Content = control,
                    Width = 700,
                    Height = 900,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.BorderOnly
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                await window.ShowDialog(parentWindow!);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task ShowBackupSchedulesDialogAsync()
        {
            const string dialogKey = "backupschedules_dialog";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var control = new BackupScheduleControl();
                var window = new Window
                {
                    Title = "Backup Schedules",
                    Content = control,
                    Width = 700,
                    Height = 900,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.BorderOnly
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                await window.ShowDialog(parentWindow!);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }
    }
}
