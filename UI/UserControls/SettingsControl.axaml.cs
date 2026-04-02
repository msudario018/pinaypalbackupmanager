using Avalonia.Controls;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.IO;
using System.Text.Json;
using PinayPalBackupManager.Services;
using PinayPalBackupManager.Models;

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

            LoadConfigEditor();

            var btnSaveConfig = this.FindControl<Button>("BtnSaveConfig");
            if (btnSaveConfig != null)
            {
                btnSaveConfig.Click += async (s, e) =>
                {
                    await SaveConfigEditorAsync();
                };
            }

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

            this.FindControl<TextBox>("TxtFtpHost")!.Text = s.Ftp.Host;
            this.FindControl<TextBox>("TxtFtpUser")!.Text = s.Ftp.User;
            this.FindControl<TextBox>("TxtFtpPassword")!.Text = s.Ftp.Password;
            this.FindControl<TextBox>("TxtFtpTls")!.Text = s.Ftp.TlsFingerprint;
            this.FindControl<TextBox>("TxtFtpPort")!.Text = s.Ftp.Port.ToString();

            this.FindControl<TextBox>("TxtSqlUser")!.Text = s.Sql.User;
            this.FindControl<TextBox>("TxtSqlPassword")!.Text = s.Sql.Password;
            this.FindControl<TextBox>("TxtSqlRemotePath")!.Text = s.Sql.RemotePath;
            this.FindControl<TextBox>("TxtSqlTls")!.Text = s.Sql.TlsFingerprint;

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
                    TlsFingerprint = this.FindControl<TextBox>("TxtFtpTls")!.Text ?? string.Empty,
                    Port = int.TryParse(this.FindControl<TextBox>("TxtFtpPort")!.Text, out var p) ? p : 21
                },
                Sql = new SqlSettings
                {
                    User = this.FindControl<TextBox>("TxtSqlUser")!.Text ?? string.Empty,
                    Password = this.FindControl<TextBox>("TxtSqlPassword")!.Text ?? string.Empty,
                    RemotePath = this.FindControl<TextBox>("TxtSqlRemotePath")!.Text ?? string.Empty,
                    TlsFingerprint = this.FindControl<TextBox>("TxtSqlTls")!.Text ?? string.Empty,
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
    }
}
