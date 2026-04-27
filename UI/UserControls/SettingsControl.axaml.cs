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
                }
            };
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

                await window.ShowDialog(parentWindow);
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

                await window.ShowDialog(parentWindow);
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
    }
}
