using Avalonia.Controls;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Runtime.Versioning;
using PinayPalBackupManager.Services;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class SettingsControl : UserControl
    {
        private readonly BackupManager? _manager;
        public event Func<System.Threading.Tasks.Task>? OnShowSystemInfo;
        public event Func<System.Threading.Tasks.Task>? OnCheckUpdates;

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
