using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using System;
using System.Collections.Generic;
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

            // Minimize to Tray
            var chkMinimizeToTray = this.FindControl<CheckBox>("ChkMinimizeToTray");
            if (chkMinimizeToTray != null)
            {
                chkMinimizeToTray.IsChecked = ConfigService.Current.Operation.MinimizeToTray;
                chkMinimizeToTray.IsCheckedChanged += (_, _) =>
                {
                    ConfigService.Current.Operation.MinimizeToTray = chkMinimizeToTray.IsChecked == true;
                    ConfigService.SaveOperation();
                    NotificationService.ShowBackupToast("Settings", "Minimize to tray " + (chkMinimizeToTray.IsChecked == true ? "enabled" : "disabled"), "Info");
                };
            }

            // Close to Tray
            var chkCloseToTray = this.FindControl<CheckBox>("ChkCloseToTray");
            if (chkCloseToTray != null)
            {
                chkCloseToTray.IsChecked = ConfigService.Current.Operation.CloseToTray;
                chkCloseToTray.IsCheckedChanged += (_, _) =>
                {
                    ConfigService.Current.Operation.CloseToTray = chkCloseToTray.IsChecked == true;
                    ConfigService.SaveOperation();
                    NotificationService.ShowBackupToast("Settings", "Close to tray " + (chkCloseToTray.IsChecked == true ? "enabled" : "disabled"), "Info");
                };
            }

            // Auto-Update TLS Certificate
            var chkAutoTls = this.FindControl<CheckBox>("ChkAutoTls");
            if (chkAutoTls != null)
            {
                chkAutoTls.IsChecked = ConfigService.Current.Operation.AutoUpdateTlsFingerprint;
                chkAutoTls.IsCheckedChanged += (_, _) =>
                {
                    ConfigService.Current.Operation.AutoUpdateTlsFingerprint = chkAutoTls.IsChecked == true;
                    ConfigService.SaveOperation();
                    NotificationService.ShowBackupToast("Settings", "TLS auto-rotation " + (chkAutoTls.IsChecked == true ? "enabled" : "disabled"), "Info");
                };
            }

            // Daily Health Check
            var chkDailyHealth = this.FindControl<CheckBox>("ChkDailyHealth");
            if (chkDailyHealth != null)
            {
                chkDailyHealth.IsChecked = ConfigService.Current.Operation.DailyHealthCheckEnabled;
                chkDailyHealth.IsCheckedChanged += (_, _) =>
                {
                    ConfigService.Current.Operation.DailyHealthCheckEnabled = chkDailyHealth.IsChecked == true;
                    ConfigService.SaveOperation();
                    NotificationService.ShowBackupToast("Settings", "Daily health check " + (chkDailyHealth.IsChecked == true ? "enabled" : "disabled"), "Info");
                };
            }

            // Web Dashboard settings
            var chkEnableWeb = this.FindControl<CheckBox>("ChkEnableWebDashboard");
            var txtWebPort = this.FindControl<TextBox>("TxtWebPort");
            var chkRequireWebAuth = this.FindControl<CheckBox>("ChkRequireWebAuth");
            var txtWebPin = this.FindControl<TextBox>("TxtWebPin");
            var btnSaveWeb = this.FindControl<Button>("BtnSaveWebSettings");
            var btnOpenWeb = this.FindControl<Button>("BtnOpenWebDashboard");

            if (chkEnableWeb != null) chkEnableWeb.IsChecked = ConfigService.Current.HttpServer.Enabled;
            if (txtWebPort != null) txtWebPort.Text = ConfigService.Current.HttpServer.Port.ToString();
            if (chkRequireWebAuth != null) chkRequireWebAuth.IsChecked = ConfigService.Current.HttpServer.RequireAuth;
            if (txtWebPin != null) txtWebPin.Text = ConfigService.Current.HttpServer.WebPin;

            if (btnSaveWeb != null)
            {
                btnSaveWeb.Click += async (s, e) =>
                {
                    bool enabled = chkEnableWeb?.IsChecked == true;
                    ConfigService.Current.HttpServer.Enabled = enabled;
                    if (int.TryParse(txtWebPort?.Text?.Trim(), out int port) && port > 0 && port < 65535)
                    {
                        ConfigService.Current.HttpServer.Port = port;
                    }
                    ConfigService.Current.HttpServer.RequireAuth = chkRequireWebAuth?.IsChecked == true;
                    ConfigService.Current.HttpServer.WebPin = txtWebPin?.Text?.Trim() ?? string.Empty;
                    ConfigService.SaveHttpServerSettings();

                    try
                    {
                        if (enabled)
                        {
                            var username = AuthService.CurrentUser?.Username ?? "admin";
                            var backupDir = ConfigService.Current.Paths.FtpLocalFolder;
                            FileDownloadService.Initialize(username, backupDir, ConfigService.Current.HttpServer.Port);
                            if (FileDownloadService.IsRunning)
                            {
                                await FileDownloadService.RestartAsync(ConfigService.Current.HttpServer.Port);
                            }
                            else
                            {
                                await FileDownloadService.StartAsync();
                            }
                            NotificationService.ShowBackupToast("Web Dashboard", $"Web server running on port {ConfigService.Current.HttpServer.Port}.", "Success");
                        }
                        else
                        {
                            FileDownloadService.Stop();
                            NotificationService.ShowBackupToast("Web Dashboard", "Web server stopped.", "Info");
                        }
                    }
                    catch (Exception ex)
                    {
                        NotificationService.ShowBackupToast("Web Dashboard Error", $"Failed to start server: {ex.Message}", "Error");
                    }
                };
            }

            if (btnOpenWeb != null)
            {
                btnOpenWeb.Click += async (s, e) =>
                {
                    try
                    {
                        int port = ConfigService.Current.HttpServer.Port > 0 ? ConfigService.Current.HttpServer.Port : 8080;
                        if (!FileDownloadService.IsRunning)
                        {
                            var username = AuthService.CurrentUser?.Username ?? "admin";
                            var backupDir = ConfigService.Current.Paths.FtpLocalFolder;
                            FileDownloadService.Initialize(username, backupDir, port);
                            await FileDownloadService.StartAsync();
                        }

                        var url = $"http://localhost:{port}/";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        NotificationService.ShowBackupToast("Web Dashboard", $"Failed to open browser: {ex.Message}", "Warning");
                    }
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

            // Load Auto-Backup Interval setting
            var cmbAutoInterval = this.FindControl<ComboBox>("CmbAutoInterval");
            if (cmbAutoInterval != null)
            {
                var mins = ConfigService.Current.Operation.AutoIntervalMinutes;
                cmbAutoInterval.SelectedIndex = mins switch
                {
                    <= 30 => 0,
                    <= 60 => 1,
                    <= 120 => 2,
                    <= 240 => 3,
                    <= 480 => 4,
                    _ => 5
                };
                cmbAutoInterval.SelectionChanged += (_, _) =>
                {
                    var intervalMinutes = cmbAutoInterval.SelectedIndex switch
                    {
                        0 => 30,
                        1 => 60,
                        2 => 120,
                        3 => 240,
                        4 => 480,
                        5 => 1440,
                        _ => 60
                    };
                    ConfigService.Current.Operation.AutoIntervalMinutes = intervalMinutes;
                    ConfigService.Current.Schedule.FtpAutoScanHours = intervalMinutes / 60;
                    ConfigService.Current.Schedule.FtpAutoScanMinutes = intervalMinutes % 60;
                    ConfigService.Current.Schedule.SqlAutoScanHours = intervalMinutes / 60;
                    ConfigService.Current.Schedule.SqlAutoScanMinutes = intervalMinutes % 60;
                    ConfigService.Current.Schedule.MailchimpAutoScanHours = intervalMinutes / 60;
                    ConfigService.Current.Schedule.MailchimpAutoScanMinutes = intervalMinutes % 60;
                    ConfigService.SaveOperation();
                    ConfigService.Save();
                    BackupManager.Current?.ResetAutoScanTimers();
                    NotificationService.ShowBackupToast("Settings", $"Auto-backup interval set to {intervalMinutes} min.", "Info");
                    LogService.WriteSystemLog($"[Settings] Auto-backup interval changed to {intervalMinutes} minutes", "Information", "SETTINGS");
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
            var btnResetAllUsers = this.FindControl<Button>("BtnResetAllUsers");
            if (btnResetAllUsers != null)
            {
                btnResetAllUsers.Click += async (_, _) => await ShowResetAllUsersConfirmationAsync();
            }

            InitializeTelegramAlerts();
            InitializeAiAssistantSettings();
            InitializeMyComputers();
            InitializeSmartScheduling();
            InitializeSettingsSaveBar();
        }

        /// <summary>
        /// Wires the sticky save bar. Every card still saves its own section, but these two
        /// buttons give one obvious "persist everything" action that is reachable from anywhere
        /// in Settings, which is what users kept asking for.
        /// </summary>
        private void InitializeSettingsSaveBar()
        {
            var btnTop = this.FindControl<Button>("BtnSaveSettingsTop");
            var btnBottom = this.FindControl<Button>("BtnSaveSettingsBottom");
            var btnReset = this.FindControl<Button>("BtnResetSettings");
            var txtSavedAt = this.FindControl<TextBlock>("TxtSettingsSavedAt");
            var txtHint = this.FindControl<TextBlock>("TxtSettingsDirtyHint");

            void SaveAll()
            {
                try
                {
                    // Re-apply every section that owns its own persistence.
                    ConfigService.SaveOperation();
                    NotificationService.LoadSettings();
                    NotificationService.SaveSettings();

                    AIAssistantService.SaveConfig(AIAssistantService.Config);
                    ComputerManagementService.SaveNodes(ComputerManagementService.GetNodes());

                    if (txtSavedAt != null) txtSavedAt.Text = $"Saved {TimeFormat.ClockSeconds(DateTime.Now)}";
                    if (txtHint != null)
                    {
                        txtHint.Text = "All settings saved.";
                        txtHint.Foreground = TryBrush("AccentSuccess");
                    }

                    NotificationService.ShowBackupToast("Settings", "All settings saved.", "Success");
                    LogService.WriteSystemLog("[Settings] All settings saved via the save bar.", "Information", "SYSTEM");
                }
                catch (Exception ex)
                {
                    if (txtHint != null)
                    {
                        txtHint.Text = $"Save failed: {ex.Message}";
                        txtHint.Foreground = TryBrush("AccentError");
                    }
                    NotificationService.ShowBackupToast("Settings", $"Save failed: {ex.Message}", "Error");
                }
            }

            if (btnTop != null) btnTop.Click += (_, _) => SaveAll();
            if (btnBottom != null) btnBottom.Click += (_, _) => SaveAll();

            if (btnReset != null)
            {
                btnReset.Click += (_, _) =>
                {
                    RefreshAiSettings();
                    RefreshEmailAlerts();
                    RefreshServiceHealthCards();
                    if (txtSavedAt != null) txtSavedAt.Text = "";
                    if (txtHint != null)
                    {
                        txtHint.Text = "Reloaded the stored values. Nothing was written.";
                        txtHint.Foreground = TryBrush("AppMuted");
                    }
                };
            }
        }

        private static IBrush? TryBrush(string key)
        {
            if (Application.Current != null && Application.Current.TryFindResource(key, out var res))
            {
                return res as IBrush;
            }
            return null;
        }

        public void RefreshAiSettings()
        {
            var chkWidget = this.FindControl<CheckBox>("ChkAiFloatingWidget");
            var chkGreeting = this.FindControl<CheckBox>("ChkAiLoginGreeting");
            var chkChimes = this.FindControl<CheckBox>("ChkAiSoundChimes");
            var cmbProvider = this.FindControl<ComboBox>("CmbAiProvider");
            var txtOllamaEp = this.FindControl<TextBox>("TxtOllamaEndpoint");
            var txtOllamaModel = this.FindControl<TextBox>("TxtOllamaModel");
            var txtCloudEp = this.FindControl<TextBox>("TxtCloudEndpoint");
            var txtCloudModel = this.FindControl<TextBox>("TxtCloudModel");
            var txtCloudKey = this.FindControl<TextBox>("TxtCloudApiKey");

            AIAssistantService.LoadConfig();
            var cfg = AIAssistantService.Config;

            if (chkWidget != null) chkWidget.IsChecked = cfg.EnableFloatingWidget;
            if (chkGreeting != null) chkGreeting.IsChecked = cfg.EnableLoginGreeting;
            if (chkChimes != null) chkChimes.IsChecked = cfg.EnableSoundChimes;

            if (cmbProvider != null)
            {
                var p = cfg.Provider?.ToLowerInvariant() ?? "hybrid";
                cmbProvider.SelectedIndex = p switch
                {
                    "ollama" => 1,
                    "cloud" => 2,
                    "heuristics" => 3,
                    _ => 0
                };
            }

            if (txtOllamaEp != null) txtOllamaEp.Text = cfg.OllamaEndpoint;
            if (txtOllamaModel != null) txtOllamaModel.Text = cfg.OllamaModel;

            var cmbDetectedModels = this.FindControl<ComboBox>("CmbDetectedOllamaModels");
            if (cmbDetectedModels != null && cmbDetectedModels.ItemsSource != null)
            {
                cmbDetectedModels.SelectedItem = cfg.OllamaModel;
            }
            if (txtCloudEp != null) txtCloudEp.Text = cfg.CloudEndpoint;
            if (txtCloudModel != null) txtCloudModel.Text = cfg.CloudModel;
            if (txtCloudKey != null) txtCloudKey.Text = cfg.CloudApiKey;

            // Personality & safety
            var txtName = this.FindControl<TextBox>("TxtAiAssistantName");
            var sldTalk = this.FindControl<Slider>("SldAiTalkativeness");
            var sldCreativity = this.FindControl<Slider>("SldAiCreativity");
            var sldMemory = this.FindControl<Slider>("SldAiMemoryDepth");
            var sldProactive = this.FindControl<Slider>("SldAiProactiveMinutes");
            var chkFollowUp = this.FindControl<CheckBox>("ChkAiFollowUpSuggestions");
            var chkProactive = this.FindControl<CheckBox>("ChkAiProactiveUpdates");
            var chkRequireApproval = this.FindControl<CheckBox>("ChkAiRequireApproval");
            var chkZeroLeak = this.FindControl<CheckBox>("ChkAiZeroLeak");

            if (txtName != null) txtName.Text = cfg.AssistantName;
            if (sldTalk != null) sldTalk.Value = Math.Clamp(cfg.Talkativeness, 0, 100);
            if (sldCreativity != null) sldCreativity.Value = Math.Clamp(cfg.Creativity, 0.0, 1.5);
            if (sldMemory != null) sldMemory.Value = Math.Clamp(cfg.ConversationMemoryDepth, 0, 40);
            if (sldProactive != null) sldProactive.Value = Math.Clamp(cfg.ProactiveIntervalMinutes, 0, 240);

            if (chkFollowUp != null) chkFollowUp.IsChecked = cfg.EnableFollowUpSuggestions;
            if (chkProactive != null) chkProactive.IsChecked = cfg.EnableProactiveUpdates;
            if (chkRequireApproval != null) chkRequireApproval.IsChecked = cfg.RequireActionApproval;
            if (chkZeroLeak != null) chkZeroLeak.IsChecked = cfg.EnableZeroLeakSanitizer;

            var chkCloudRedact = this.FindControl<CheckBox>("ChkAiCloudRedact");
            var chkKeepModel = this.FindControl<CheckBox>("ChkAiKeepModelDuringBackups");
            var txtThreads = this.FindControl<TextBox>("TxtAiThreads");

            var cmbAgentProfile = this.FindControl<ComboBox>("CmbAiAgentProfile");
            var chkLearningMemory = this.FindControl<CheckBox>("ChkAiLearningMemory");

            if (cmbAgentProfile != null)
            {
                cmbAgentProfile.SelectedIndex = (cfg.AgentProfile ?? "guardian").ToLowerInvariant() switch
                {
                    "specialist" => 1,
                    "speedy" => 2,
                    _ => 0
                };
            }
            if (chkLearningMemory != null) chkLearningMemory.IsChecked = cfg.EnableLearningMemory;

            if (chkCloudRedact != null) chkCloudRedact.IsChecked = cfg.CloudRedactsContext;
            if (chkKeepModel != null) chkKeepModel.IsChecked = cfg.EnableLocalModelDuringBackups;
            if (txtThreads != null && int.TryParse(txtThreads.Text, out var parsedThreads))
            txtThreads.Text = Math.Clamp(parsedThreads, 0, 64).ToString();
        }

        private void InitializeAiAssistantSettings()
        {
            var chkWidget = this.FindControl<CheckBox>("ChkAiFloatingWidget");
            var chkGreeting = this.FindControl<CheckBox>("ChkAiLoginGreeting");
            var chkChimes = this.FindControl<CheckBox>("ChkAiSoundChimes");
            var cmbProvider = this.FindControl<ComboBox>("CmbAiProvider");
            var btnTestOllama = this.FindControl<Button>("BtnTestOllamaConnection");
            var btnAutoDetect = this.FindControl<Button>("BtnAutoDetectAiModels");
            var cmbDetectedModels = this.FindControl<ComboBox>("CmbDetectedOllamaModels");
            var txtDetectedCount = this.FindControl<TextBlock>("TxtDetectedModelCount");
            var txtOllamaEp = this.FindControl<TextBox>("TxtOllamaEndpoint");
            var txtOllamaModel = this.FindControl<TextBox>("TxtOllamaModel");
            var txtOllamaStatus = this.FindControl<TextBlock>("TxtOllamaStatus");
            var txtCloudEp = this.FindControl<TextBox>("TxtCloudEndpoint");
            var txtCloudModel = this.FindControl<TextBox>("TxtCloudModel");
            var txtCloudKey = this.FindControl<TextBox>("TxtCloudApiKey");
            var btnSaveAi = this.FindControl<Button>("BtnSaveAiSettings");
            var btnResetHistory = this.FindControl<Button>("BtnResetAiHistory");
            var txtAiStatus = this.FindControl<TextBlock>("TxtAiStatusMessage");

            // Personality & safety controls
            var txtName = this.FindControl<TextBox>("TxtAiAssistantName");
            var cmbAgentProfile = this.FindControl<ComboBox>("CmbAiAgentProfile");
            var chkLearningMemory = this.FindControl<CheckBox>("ChkAiLearningMemory");
            var btnClearMemories = this.FindControl<Button>("BtnClearAiMemories");
            var btnAutoTune = this.FindControl<Button>("BtnAutoTuneAi");
            var sldTalk = this.FindControl<Slider>("SldAiTalkativeness");
            var sldCreativity = this.FindControl<Slider>("SldAiCreativity");
            var sldMemory = this.FindControl<Slider>("SldAiMemoryDepth");
            var sldProactive = this.FindControl<Slider>("SldAiProactiveMinutes");
            var chkFollowUp = this.FindControl<CheckBox>("ChkAiFollowUpSuggestions");
            var chkProactive = this.FindControl<CheckBox>("ChkAiProactiveUpdates");
            var chkRequireApproval = this.FindControl<CheckBox>("ChkAiRequireApproval");
            var chkZeroLeak = this.FindControl<CheckBox>("ChkAiZeroLeak");

            // Resource & cloud-privacy controls
            var chkCloudRedact = this.FindControl<CheckBox>("ChkAiCloudRedact");
            var chkKeepModel = this.FindControl<CheckBox>("ChkAiKeepModelDuringBackups");
            var btnUnload = this.FindControl<Button>("BtnUnloadAiModel");
            var txtResourceStatus = this.FindControl<TextBlock>("TxtAiResourceStatus");
            var txtThreads = this.FindControl<TextBox>("TxtAiThreads");

            if (btnClearMemories != null)
            {
                btnClearMemories.Click += (_, _) =>
                {
                    AIMemoryStore.ClearAll();
                    NotificationService.ShowBackupToast("AI Memory", "All learned preferences & facts have been cleared.", "Info");
                };
            }

            if (btnAutoTune != null)
            {
                btnAutoTune.Click += async (_, _) =>
                {
                    btnAutoTune.IsEnabled = false;
                    var (ok, msg) = await AIAssistantService.AutoOptimizeForHardwareAsync();
                    RefreshAiSettings();
                    NotificationService.ShowBackupToast("AI Auto-Tune", msg, ok ? "Success" : "Warning");
                    btnAutoTune.IsEnabled = true;
                };
            }

            if (btnUnload != null)
            {
                btnUnload.Click += async (_, _) =>
                {
                    btnUnload.IsEnabled = false;
                    if (txtResourceStatus != null) txtResourceStatus.Text = "Releasing the local model...";

                    var (ok, msg) = await AIAssistantService.UnloadOllamaModelAsync();

                    if (txtResourceStatus != null) txtResourceStatus.Text = msg;
                    NotificationService.ShowBackupToast("AI Assistant", msg, ok ? "Success" : "Warning");
                    btnUnload.IsEnabled = true;
                };
            }

            void ShowLiveResourceUsage()
            {
                if (txtResourceStatus == null) return;
                try
                {
                    var t = HardwareTelemetryService.GetTelemetrySync();
                    txtResourceStatus.Text =
                        $"RAM {t.RamUsagePercent:F0}% used · {t.RamFreeGB:F1} GB free · App {t.AppRamUsageMB:F0} MB";
                }
                catch { }
            }

            ShowLiveResourceUsage();

            var lblTalk = this.FindControl<TextBlock>("TxtAiTalkativenessValue");
            var lblCreativity = this.FindControl<TextBlock>("TxtAiCreativityValue");
            var lblMemory = this.FindControl<TextBlock>("TxtAiMemoryDepthValue");
            var lblProactive = this.FindControl<TextBlock>("TxtAiProactiveMinutesValue");

            // Keep the numeric read-outs beside each slider live.
            void SyncSliderLabels()
            {
                if (lblTalk != null) lblTalk.Text = sldTalk?.Value.ToString("F0") ?? "60";
                if (lblCreativity != null) lblCreativity.Text = sldCreativity?.Value.ToString("F1") ?? "0.6";
                if (lblMemory != null) lblMemory.Text = sldMemory?.Value.ToString("F0") ?? "12";
                if (lblProactive != null)
                {
                    var mins = sldProactive?.Value ?? 60;
                    lblProactive.Text = mins <= 0 ? "Off" : (mins >= 60 ? $"{mins / 60:F0}h" : $"{mins:F0}m");
                }
            }

            if (sldTalk != null) sldTalk.ValueChanged += (_, _) => SyncSliderLabels();
            if (sldCreativity != null) sldCreativity.ValueChanged += (_, _) => SyncSliderLabels();
            if (sldMemory != null) sldMemory.ValueChanged += (_, _) => SyncSliderLabels();
            if (sldProactive != null) sldProactive.ValueChanged += (_, _) => SyncSliderLabels();
            SyncSliderLabels();

            RefreshAiSettings();

            if (btnAutoDetect != null && txtOllamaStatus != null)
            {
                btnAutoDetect.Click += async (_, _) =>
                {
                    btnAutoDetect.IsEnabled = false;
                    btnAutoDetect.Content = "Detecting...";
                    txtOllamaStatus.Text = "Scanning local Ollama for downloaded models...";

                    var ep = txtOllamaEp?.Text?.Trim();
                    var (ok, models, msg) = await AIAssistantService.GetInstalledOllamaModelsAsync(ep);

                    txtOllamaStatus.Text = msg;

                    if (ok && models.Count > 0)
                    {
                        if (cmbDetectedModels != null)
                        {
                            cmbDetectedModels.ItemsSource = models;
                            cmbDetectedModels.IsVisible = true;

                            var best = AIAssistantService.SelectBestModel(models, txtOllamaModel?.Text?.Trim() ?? "");
                            cmbDetectedModels.SelectedItem = best;
                            if (txtOllamaModel != null) txtOllamaModel.Text = best;
                        }

                        if (txtDetectedCount != null)
                        {
                            txtDetectedCount.Text = $"{models.Count} model{(models.Count > 1 ? "s" : "")}";
                        }

                        // Auto-save the detected model
                        var cfg = AIAssistantService.Config;
                        if (txtOllamaModel != null && !string.IsNullOrWhiteSpace(txtOllamaModel.Text))
                        {
                            cfg.OllamaModel = txtOllamaModel.Text.Trim();
                            AIAssistantService.SaveConfig(cfg);
                        }

                        NotificationService.ShowBackupToast("AI Agent Detected", $"Auto-selected '{txtOllamaModel?.Text}' as your local agent.", "Success");
                    }
                    else
                    {
                        if (txtDetectedCount != null) txtDetectedCount.Text = "";
                        NotificationService.ShowBackupToast("AI Detection", msg, ok ? "Info" : "Warning");
                    }

                    btnAutoDetect.IsEnabled = true;
                    btnAutoDetect.Content = "🔍 Auto-Detect Models";
                };
            }

            if (cmbDetectedModels != null)
            {
                cmbDetectedModels.SelectionChanged += (_, _) =>
                {
                    if (cmbDetectedModels.SelectedItem is string selectedModel && !string.IsNullOrWhiteSpace(selectedModel))
                    {
                        if (txtOllamaModel != null)
                        {
                            txtOllamaModel.Text = selectedModel;
                        }
                        var cfg = AIAssistantService.Config;
                        cfg.OllamaModel = selectedModel;
                        AIAssistantService.SaveConfig(cfg);
                    }
                };
            }

            if (btnTestOllama != null && txtOllamaStatus != null)
            {
                btnTestOllama.Click += async (_, _) =>
                {
                    txtOllamaStatus.Text = "Testing Ollama connection...";
                    var ep = txtOllamaEp?.Text?.Trim();
                    var model = txtOllamaModel?.Text?.Trim();
                    var (ok, msg) = await AIAssistantService.TestOllamaConnectionAsync(ep, model);
                    txtOllamaStatus.Text = msg;
                };
            }

            if (btnResetHistory != null && txtAiStatus != null)
            {
                btnResetHistory.Click += (_, _) =>
                {
                    AIAssistantService.ClearSessionHistory();
                    txtAiStatus.Text = "AI conversation history cleared.";
                    NotificationService.ShowBackupToast("AI Assistant", "Session history reset.", "Info");
                };
            }

            if (btnSaveAi != null && txtAiStatus != null)
            {
                btnSaveAi.Click += (_, _) =>
                {
                    try
                    {
                        var cfg = AIAssistantService.Config;
                        cfg.EnableFloatingWidget = chkWidget?.IsChecked == true;
                        cfg.EnableLoginGreeting = chkGreeting?.IsChecked == true;
                        cfg.EnableSoundChimes = chkChimes?.IsChecked == true;

                        cfg.Provider = cmbProvider?.SelectedIndex switch
                        {
                            1 => "ollama",
                            2 => "cloud",
                            3 => "heuristics",
                            _ => "hybrid"
                        };

                        if (cmbAgentProfile != null)
                        {
                            cfg.AgentProfile = cmbAgentProfile.SelectedIndex switch
                            {
                                1 => "specialist",
                                2 => "speedy",
                                _ => "guardian"
                            };
                        }

                        if (chkLearningMemory != null)
                        {
                            cfg.EnableLearningMemory = chkLearningMemory.IsChecked == true;
                        }

                        if (txtOllamaEp != null && !string.IsNullOrWhiteSpace(txtOllamaEp.Text))
                            cfg.OllamaEndpoint = txtOllamaEp.Text.Trim();

                        if (txtOllamaModel != null && !string.IsNullOrWhiteSpace(txtOllamaModel.Text))
                            cfg.OllamaModel = txtOllamaModel.Text.Trim();

                        if (txtCloudEp != null && !string.IsNullOrWhiteSpace(txtCloudEp.Text))
                            cfg.CloudEndpoint = txtCloudEp.Text.Trim();

                        if (txtCloudModel != null && !string.IsNullOrWhiteSpace(txtCloudModel.Text))
                            cfg.CloudModel = txtCloudModel.Text.Trim();

                        if (txtCloudKey != null)
                            cfg.CloudApiKey = txtCloudKey.Text?.Trim() ?? "";

                        // Personality & safety
                        if (txtName != null && !string.IsNullOrWhiteSpace(txtName.Text))
                            cfg.AssistantName = txtName.Text.Trim();

                        if (sldTalk != null) cfg.Talkativeness = (int)sldTalk.Value;
                        if (sldCreativity != null) cfg.Creativity = sldCreativity.Value;
                        if (sldMemory != null) cfg.ConversationMemoryDepth = (int)sldMemory.Value;
                        if (sldProactive != null) cfg.ProactiveIntervalMinutes = (int)sldProactive.Value;

                        if (chkFollowUp != null) cfg.EnableFollowUpSuggestions = chkFollowUp.IsChecked == true;
                        if (chkProactive != null) cfg.EnableProactiveUpdates = chkProactive.IsChecked == true;
                        if (chkRequireApproval != null) cfg.RequireActionApproval = chkRequireApproval.IsChecked == true;
                        if (chkZeroLeak != null) cfg.EnableZeroLeakSanitizer = chkZeroLeak.IsChecked == true;
                        if (chkCloudRedact != null) cfg.CloudRedactsContext = chkCloudRedact.IsChecked == true;
                        if (chkKeepModel != null) cfg.EnableLocalModelDuringBackups = chkKeepModel.IsChecked == true;
                        if (txtThreads != null && int.TryParse(txtThreads.Text, out var threadValue))
                            cfg.OllamaThreads = Math.Clamp(threadValue, 0, 64);

                        AIAssistantService.SaveConfig(cfg);
                        txtAiStatus.Text = $"AI settings saved at {TimeFormat.ClockSeconds(DateTime.Now)} (Provider: {cfg.Provider.ToUpperInvariant()})";
                        NotificationService.ShowBackupToast("AI Settings Saved", $"Inference provider: {cfg.Provider.ToUpperInvariant()}", "Success");
                    }
                    catch (Exception ex)
                    {
                        txtAiStatus.Text = $"Save error: {ex.Message}";
                    }
                };
            }
        }

        /// <summary>
        /// Formats a partially typed MAC address into A4:BB:6D:11:22:33 form.
        /// Strips every non-hex character first, so users can paste in any common style.
        /// </summary>
        private static string FormatMacAsYouType(string input)
        {
            var hex = new string((input ?? "").Where(Uri.IsHexDigit).Take(12).ToArray());

            var sb = new System.Text.StringBuilder(17);
            for (var i = 0; i < hex.Length; i++)
            {
                if (i > 0 && i % 2 == 0) sb.Append(':');
                sb.Append(char.ToUpperInvariant(hex[i]));
            }

            // Don't leave a dangling separator while the user is mid-type.
            var result = sb.ToString();
            return result.EndsWith(":") ? result[..^1] : result;
        }

        private void InitializeMyComputers()
        {
            var btnAdd = this.FindControl<Button>("BtnAddComputer");
            var btnRefresh = this.FindControl<Button>("BtnRefreshComputers");
            var txtName = this.FindControl<TextBox>("TxtNewComputerName");
            var cmbRole = this.FindControl<ComboBox>("CmbNewComputerRole");
            var txtMac = this.FindControl<TextBox>("TxtNewComputerMac");
            var txtBroadcast = this.FindControl<TextBox>("TxtNewComputerBroadcast");
            var txtIp = this.FindControl<TextBox>("TxtNewComputerIp");
            var txtPort = this.FindControl<TextBox>("TxtNewComputerPort");
            var txtUrl = this.FindControl<TextBox>("TxtNewComputerUrl");
            var txtPin = this.FindControl<TextBox>("TxtNewComputerPin");
            var txtStatus = this.FindControl<TextBlock>("TxtComputerStatus");
            var listPanel = this.FindControl<StackPanel>("ComputerListPanel");

            if (listPanel == null) return;

            // Two-way synchronization between IP / Port and Dashboard URL
            var isSyncingUrl = false;
            void SyncUrlFromIpPort()
            {
                if (isSyncingUrl) return;
                isSyncingUrl = true;
                try
                {
                    var ip = txtIp?.Text?.Trim() ?? "";
                    var port = txtPort?.Text?.Trim() ?? "8080";
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        // Only fill the URL while it is still empty. Once the user types
                        // one -- a tunnel hostname, a domain, a non-standard port -- it
                        // is theirs to keep, and the IP field must not overwrite it.
                        // The URL no longer feeds back into IP/Port either, so the two
                        // boxes are independent: IP identifies the machine to ping and
                        // Wake-on-LAN, the URL is how you reach its PinayPal app.
                        var existing = txtUrl?.Text?.Trim() ?? "";
                        if (existing.Length == 0)
                        {
                            var p = string.IsNullOrWhiteSpace(port) ? "8080" : port;
                            if (txtUrl != null) txtUrl.Text = $"http://{ip}:{p}";
                        }
                    }
                }
                finally
                {
                    isSyncingUrl = false;
                }
            }

            void AutoFillUrlIfEmpty()
            {
                // Fires on every keystroke in the URL box. Because SyncUrlFromIpPort
                // only writes when the field is empty, and isSyncingUrl breaks the
                // re-entrancy, typing here can never clobber what the user is typing.
                if (isSyncingUrl) return;
                SyncUrlFromIpPort();
            }

            if (txtIp != null)
            {
                txtIp.TextChanged += (_, _) => SyncUrlFromIpPort();
                txtIp.LostFocus += async (_, _) =>
                {
                    var ipVal = txtIp?.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(ipVal) && (txtName != null && string.IsNullOrWhiteSpace(txtName.Text)))
                    {
                        try
                        {
                            var entry = await System.Net.Dns.GetHostEntryAsync(ipVal);
                            var resolved = entry.HostName?.Split('.').FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(resolved) && string.IsNullOrWhiteSpace(txtName.Text))
                            {
                                txtName.Text = resolved;
                            }
                        }
                        catch { }
                    }
                };
            }
            if (txtPort != null) txtPort.TextChanged += (_, _) => SyncUrlFromIpPort();
            if (txtUrl != null)
            {
                // Deliberately NOT wired back to the IP/Port fields.
                //
                // These used to sync in both directions, which made the two boxes a
                // single logical field: editing the Dashboard URL silently rewrote the
                // target PC's IP (and vice versa). But they are different things --
                // the IP is the machine to ping / Wake-on-LAN / poll telemetry on, while
                // the Dashboard URL is how you reach that machine's PinayPal app, which
                // is frequently something else entirely (a Cloudflare tunnel URL, a
                // Tailscale hostname, or a different port). Typing either one now
                // leaves the other alone; IP/Port still auto-fills the URL when the URL
                // is still empty.
                txtUrl.TextChanged += (_, _) => AutoFillUrlIfEmpty();
            }

            if (btnRefresh != null)
            {
                btnRefresh.Click += async (_, _) =>
                {
                    if (txtStatus != null) txtStatus.Text = "Refreshing telemetry...";
                    await RefreshComputerListAsync();
                };
            }

            var btnScan = this.FindControl<Button>("BtnScanNetwork");
            if (btnScan != null)
            {
                btnScan.Click += async (_, _) =>
                {
                    var txtScanStatus = this.FindControl<TextBlock>("TxtScanStatus");
                    var resultsPanel = this.FindControl<StackPanel>("ScanResultsPanel");

                    btnScan.IsEnabled = false;
                    btnScan.Content = "Scanning...";
                    if (txtScanStatus != null) txtScanStatus.Text = "Probing your subnet...";
                    if (resultsPanel != null) resultsPanel.Children.Clear();

                    try
                    {
                        var found = await NetworkScannerService.ScanAsync();

                        var pinayPalCount = found.Count(h => h.IsPinayPal);
                        if (txtScanStatus != null)
                        {
                            txtScanStatus.Text = found.Count == 0
                                ? "No other devices answered on this network."
                                : $"Found {found.Count} device(s), including {pinayPalCount} PinayPal machine(s).";
                        }

                        if (resultsPanel != null)
                        {
                            foreach (var host in found.Take(30))
                            {
                                resultsPanel.Children.Add(
                                    BuildDiscoveredHostRow(host, txtName, cmbRole, txtMac, txtBroadcast, txtIp, txtPort, txtUrl, resultsPanel));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (txtScanStatus != null) txtScanStatus.Text = $"Scan failed: {ex.Message}";
                    }
                    finally
                    {
                        btnScan.IsEnabled = true;
                        btnScan.Content = "Scan Network";
                    }
                };
            }

            // Auto-format the MAC as it is typed: accepts "a4bb6d112233", "a4-bb-6d-11-22-33"
            // or any mix, and always renders as A4:BB:6D:11:22:33.
            if (txtMac != null)
            {
                var suppress = false;
                txtMac.TextChanged += (_, _) =>
                {
                    if (suppress) return;

                    var formatted = FormatMacAsYouType(txtMac.Text ?? "");
                    if (formatted == txtMac.Text) return;

                    suppress = true;
                    var caret = txtMac.CaretIndex;
                    txtMac.Text = formatted;
                    // Keep the caret near the end so typing continues naturally.
                    txtMac.CaretIndex = Math.Min(caret + (formatted.Length - (txtMac.Text?.Length ?? 0)) + 1, formatted.Length);
                    if (txtMac.CaretIndex < 0) txtMac.CaretIndex = formatted.Length;
                    suppress = false;
                };
            }

            if (btnAdd != null)
            {
                btnAdd.Click += async (_, _) =>
                {
                    var name = txtName?.Text?.Trim() ?? "";
                    var ip = txtIp?.Text?.Trim() ?? "";
                    var localIp = ComputerManagementService.GetLanIPv4Address();

                    if (!string.IsNullOrWhiteSpace(ip) && (string.Equals(ip, localIp, StringComparison.OrdinalIgnoreCase) || ip == "127.0.0.1" || ip.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (txtStatus != null) txtStatus.Text = "This IP belongs to this PC, which is already registered as 'This PC'. Enter your other computer's IP.";
                        NotificationService.ShowBackupToast("My Computers", "This IP belongs to this PC.", "Warning");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        if (!string.IsNullOrWhiteSpace(ip))
                        {
                            try
                            {
                                var entry = await System.Net.Dns.GetHostEntryAsync(ip);
                                name = entry.HostName?.Split('.').FirstOrDefault() ?? "";
                            }
                            catch { }

                            if (string.IsNullOrWhiteSpace(name))
                            {
                                var tail = ip.Split('.').LastOrDefault();
                                name = !string.IsNullOrWhiteSpace(tail) ? $"PC-{tail}" : $"PC-{ip}";
                            }
                        }
                        else
                        {
                            if (txtStatus != null) txtStatus.Text = "Please give the computer a name or enter an IP address.";
                            return;
                        }
                    }

                    var mac = ComputerManagementService.NormalizeMac(txtMac?.Text);
                    if (!string.IsNullOrWhiteSpace(txtMac?.Text) && mac.Length != 12)
                    {
                        if (txtStatus != null) txtStatus.Text = "That MAC address doesn't look right. Use 12 hex digits, e.g. A4:BB:6D:11:22:33.";
                        return;
                    }

                    var role = (cmbRole?.SelectedIndex) switch
                    {
                        0 => ComputerRole.Main,
                        1 => ComputerRole.Dev,
                        2 => ComputerRole.Target,
                        _ => ComputerRole.Aux
                    };

                    var url = txtUrl?.Text?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(ip))
                    {
                        var port = txtPort?.Text?.Trim();
                        var portNum = int.TryParse(port, out var p) ? p : 8080;
                        url = $"http://{ip}:{portNum}";
                    }
                    else if (!string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(ip))
                    {
                        try
                        {
                            var parsedUri = new Uri(url.Contains("://") ? url : $"http://{url}");
                            ip = parsedUri.Host;
                        }
                        catch { }
                    }

                    var nodes = ComputerManagementService.GetNodes();
                    nodes.Add(new ComputerNode
                    {
                        DisplayName = name,
                        Role = role,
                        MacAddress = mac,
                        BroadcastAddress = txtBroadcast?.Text?.Trim() is { Length: > 0 } b ? b : "255.255.255.255",
                        IpAddress = ip,
                        ApiBaseUrl = url,
                        Pin = txtPin?.Text?.Trim() ?? ""
                    });

                    ComputerManagementService.SaveNodes(nodes);

                    // Clear the form for the next entry.
                    if (txtName != null) txtName.Text = "";
                    if (txtMac != null) txtMac.Text = "";
                    if (txtIp != null) txtIp.Text = "";
                    if (txtPort != null) txtPort.Text = "8080";
                    if (txtUrl != null) txtUrl.Text = "";
                    if (txtPin != null) txtPin.Text = "";

                    if (txtStatus != null) txtStatus.Text = $"Added {name} ({ComputerManagementService.RoleLabel(role)}).";
                    NotificationService.ShowBackupToast("My Computers", $"{name} added as {ComputerManagementService.RoleLabel(role)}.", "Success");

                    _ = RefreshComputerListAsync();
                };
            }

            _ = RefreshComputerListAsync();
        }

        /// <summary>
        /// One row in the scan results. Clicking it pre-fills the add-computer form with
        /// everything we already know (name, MAC, IP, URL) so the user only has to press Add.
        /// </summary>
        private Border BuildDiscoveredHostRow(
            DiscoveredHost host,
            TextBox? txtName,
            ComboBox? cmbRole,
            TextBox? txtMac,
            TextBox? txtBroadcast,
            TextBox? txtIp,
            TextBox? txtPort,
            TextBox? txtUrl,
            StackPanel resultsPanel)
        {
            var isVendorKnown = !string.IsNullOrWhiteSpace(host.Vendor) && host.Vendor != "Unknown";
            var badge = host.IsPinayPal ? "PINAYPAL" : (isVendorKnown ? host.Vendor! : "LAN Device");
            var badgeColor = host.IsPinayPal ? "#10B981" : (isVendorKnown ? "#3B82F6" : "#64748B");

            var header = new StackPanel { Spacing = 2 };
            header.Children.Add(new TextBlock
            {
                Text = host.SuggestedName,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = BrushesFor("AppText")
            });
            var macInfo = !string.IsNullOrWhiteSpace(host.MacAddress) ? $" · {ComputerManagementService.FormatMac(host.MacAddress)}" : "";
            header.Children.Add(new TextBlock
            {
                Text = $"{host.IpAddress} · {badge}{macInfo}" +
                       (host.DashboardPort > 0 ? $" · port {host.DashboardPort}" : "") +
                       (host.AppVersion.Length > 0 ? $" · v{host.AppVersion}" : ""),
                FontSize = 10,
                Foreground = BrushesFor("AppMuted")
            });

            var useBtn = MakeActionButton(host.IsPinayPal ? "Use This" : "Fill Details", badgeColor);
            useBtn.Click += (_, _) =>
            {
                if (txtName != null) txtName.Text = host.SuggestedName;
                if (txtIp != null) txtIp.Text = host.IpAddress;
                var port = host.DashboardPort > 0 ? host.DashboardPort : 8080;
                if (txtPort != null) txtPort.Text = port.ToString();
                if (txtUrl != null) txtUrl.Text = $"http://{host.IpAddress}:{port}";

                if (txtMac != null && !string.IsNullOrWhiteSpace(host.MacAddress))
                    txtMac.Text = ComputerManagementService.FormatMac(host.MacAddress);

                // Default discovered peers with PinayPal to Target Computer (backup storage) if role not set
                if (cmbRole != null && cmbRole.SelectedIndex < 0)
                    cmbRole.SelectedIndex = 2; // Target Computer

                resultsPanel.IsVisible = false;

                var txtScanStatus = this.FindControl<TextBlock>("TxtScanStatus");
                if (txtScanStatus != null)
                    txtScanStatus.Text = $"Filled the form with {host.SuggestedName} ({host.IpAddress}). Choose a role, then press Add Computer.";
            };

            var badgeText = new TextBlock
            {
                Text = badge,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse(badgeColor)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto, Auto") };
            Grid.SetColumn(header, 0);
            Grid.SetColumn(badgeText, 1);
            Grid.SetColumn(useBtn, 2);
            grid.Children.Add(header);
            grid.Children.Add(badgeText);
            grid.Children.Add(useBtn);

            var border = new Border
            {
                Background = BrushesFor("AppCard"),
                BorderBrush = host.IsPinayPal ? new SolidColorBrush(Color.Parse("#10B981")) : BrushesFor("AppBorder"),
                BorderThickness = new Thickness(host.IsPinayPal ? 1.5 : 1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9),
                Child = grid
            };

            return border;
        }

        private async Task RefreshComputerListAsync()
        {
            var listPanel = this.FindControl<StackPanel>("ComputerListPanel");
            if (listPanel == null) return;

            try
            {
                var fleet = await ComputerManagementService.GetFleetAsync(forceRefresh: true);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    listPanel.Children.Clear();

                    foreach (var view in fleet)
                    {
                        listPanel.Children.Add(BuildComputerRow(view));
                    }
                });
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[Settings] Computer refresh failed: {ex.Message}", "Warning", "SYSTEM");
            }
        }
        /// <summary>Builds one telemetry row with wake / restart / shutdown actions for a computer.</summary>
        private Border BuildComputerRow(ComputerView view)
        {
            var node = view.Node;
            var t = view.Telemetry;
            var isOnline = t.IsOnline;

            var statusBrush = new SolidColorBrush(Color.Parse(isOnline ? "#10B981" : "#64748B"));
            var statusText = new TextBlock
            {
                Text = isOnline ? "Online" : "Offline",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = statusBrush
            };

            var dot = new Ellipse { Width = 8, Height = 8, Fill = statusBrush, VerticalAlignment = VerticalAlignment.Center };

            var nameStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            nameStack.Children.Add(new TextBlock
            {
                Text = node.IsLocal ? $"{node.DisplayName}  (This PC)" : node.DisplayName,
                FontSize = 12.5,
                FontWeight = FontWeight.Bold,
                Foreground = BrushesFor("AppText")
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = isOnline
                    ? $"{ComputerManagementService.RoleLabel(node.Role)} · {(!string.IsNullOrWhiteSpace(t.Hostname) ? t.Hostname : node.DisplayName)} · {(!string.IsNullOrWhiteSpace(t.LocalIp) ? t.LocalIp : node.IpAddress)}"
                    : $"{ComputerManagementService.RoleLabel(node.Role)} · {(string.IsNullOrWhiteSpace(t.Error) ? (!string.IsNullOrWhiteSpace(node.IpAddress) ? node.IpAddress : "No IP") : t.Error)}",
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = BrushesFor("AppMuted")
            });

            var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(nameStack, 1);
            Grid.SetColumn(statusText, 2);
            headerRow.Children.Add(dot);
            headerRow.Children.Add(nameStack);
            headerRow.Children.Add(statusText);

            // Telemetry gauges.
            var metrics = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 8, 0, 0) };
            metrics.Children.Add(MetricChip("CPU", t.CpuUsagePercent.HasValue ? $"{t.CpuUsagePercent.Value:F0}%" : "--", isOnline));
            metrics.Children.Add(MetricChip("RAM", t.RamUsagePercent.HasValue ? $"{t.RamUsagePercent.Value:F0}%" : "--", isOnline));
            metrics.Children.Add(MetricChip("Temp", t.CpuTempC.HasValue ? $"{t.CpuTempC.Value:F0}°C" : "--", isOnline));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            AddPowerActionButton(actions, node, "Wake", "wake");
            if (node.IsLocal || !string.IsNullOrWhiteSpace(node.ApiBaseUrl))
            {
                AddPowerActionButton(actions, node, "Restart", "restart");
                AddPowerActionButton(actions, node, "Shut Down", "shutdown");
                AddPowerActionButton(actions, node, "Lock", "lock");
                AddPowerActionButton(actions, node, "Sleep", "sleep");
            }

            if (!node.IsLocal)
            {
                var removeBtn = MakeActionButton("Remove", "AccentError");
                removeBtn.Click += async (_, _) =>
                {
                    var remaining = ComputerManagementService.GetNodes().Where(n => n.Id != node.Id);
                    ComputerManagementService.SaveNodes(remaining);

                    var txtStatus = this.FindControl<TextBlock>("TxtComputerStatus");
                    if (txtStatus != null) txtStatus.Text = $"Removed {node.DisplayName}.";
                    await RefreshComputerListAsync();
                };
                actions.Children.Add(removeBtn);
            }

            var body = new StackPanel();
            body.Children.Add(headerRow);
            body.Children.Add(metrics);
            body.Children.Add(actions);

            return new Border
            {
                Background = BrushesFor("AppSurface"),
                BorderBrush = BrushesFor("AppBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Child = body
            };
        }
        private Button MakeActionButton(string label, string foregroundKey)
        {
            return new Button
            {
                Content = label,
                FontSize = 11,
                Padding = new Thickness(12, 6),
                CornerRadius = new CornerRadius(7),
                Background = BrushesFor("AppBorder"),
                Foreground = BrushesFor(foregroundKey),
                BorderThickness = new Thickness(0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
        }

        private void AddPowerActionButton(StackPanel host, ComputerNode node, string label, string action)
        {
            var btn = MakeActionButton(label, "AppText");
            btn.Click += async (_, _) =>
            {
                btn.IsEnabled = false;
                btn.Content = "Working...";

                var result = await ComputerManagementService.ExecutePowerAsync(node.Id, action);

                btn.IsEnabled = true;
                btn.Content = label;

                var txtStatus = this.FindControl<TextBlock>("TxtComputerStatus");
                if (txtStatus != null) txtStatus.Text = result.Message;

                NotificationService.ShowBackupToast(
                    node.DisplayName,
                    result.Message.Replace("**", ""),
                    result.Success ? "Success" : "Error");

                if (result.Success) await RefreshComputerListAsync();
            };

            host.Children.Add(btn);
        }

        private static StackPanel MetricChip(string label, string value, bool isOnline)
        {
            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(new TextBlock { Text = label, FontSize = 9, Foreground = BrushesFor("AppMuted") });
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = BrushesFor(isOnline ? "AppText" : "AppMuted")
            });
            return stack;
        }

        private static IBrush BrushesFor(string resourceKey)
        {
            var theme = Application.Current?.ActualThemeVariant ?? Avalonia.Styling.ThemeVariant.Dark;
            if (Application.Current != null && Application.Current.TryGetResource(resourceKey, theme, out var res))
            {
                if (res is IBrush b) return b;
            }

            // Dark-mode fallbacks so dynamically created telemetry controls never render white-on-white
            return resourceKey switch
            {
                "AppSurface" => new SolidColorBrush(Color.Parse("#1A2332")),
                "AppCard" => new SolidColorBrush(Color.Parse("#111827")),
                "AppBorder" => new SolidColorBrush(Color.Parse("#1E293B")),
                "AppText" => new SolidColorBrush(Color.Parse("#F1F5F9")),
                "AppSubtext" => new SolidColorBrush(Color.Parse("#94A3B8")),
                "AppMuted" => new SolidColorBrush(Color.Parse("#64748B")),
                "AccentWebsite" => new SolidColorBrush(Color.Parse("#38BDF8")),
                "AccentError" => new SolidColorBrush(Color.Parse("#EF4444")),
                _ => new SolidColorBrush(Color.Parse("#1E293B"))
            };
        }

        private void InitializeSmartScheduling()
        {
            var chkEvict = this.FindControl<CheckBox>("ChkEvictAiBeforeBackup");
            var chkWindows = this.FindControl<CheckBox>("ChkSyncWindowsEnabled");
            var chkRollback = this.FindControl<CheckBox>("ChkEnableSyncRollback");
            var chkHeartbeat = this.FindControl<CheckBox>("ChkFleetHeartbeat");
            var chkBandwidth = this.FindControl<CheckBox>("ChkBandwidthGuard");

            var txtFtpStart = this.FindControl<TextBox>("TxtWindowFtpStart");
            var txtFtpEnd = this.FindControl<TextBox>("TxtWindowFtpEnd");
            var txtSqlStart = this.FindControl<TextBox>("TxtWindowSqlStart");
            var txtSqlEnd = this.FindControl<TextBox>("TxtWindowSqlEnd");
            var txtMailStart = this.FindControl<TextBox>("TxtWindowMailStart");
            var txtMailEnd = this.FindControl<TextBox>("TxtWindowMailEnd");

            var txtThreshold = this.FindControl<TextBox>("TxtBandwidthThreshold");
            var txtStatus = this.FindControl<TextBlock>("TxtSmartSchedulingStatus");
            var txtPreview = this.FindControl<TextBlock>("TxtSyncPreviewResult");
            var txtWindowPreview = this.FindControl<TextBlock>("TxtSyncWindowPreview");
            var txtBandwidthNow = this.FindControl<TextBlock>("TxtBandwidthNow");

            var op = ConfigService.Current.Operation;

            if (chkEvict != null) chkEvict.IsChecked = op.EvictAiModelBeforeBackup;
            if (chkWindows != null) chkWindows.IsChecked = op.SyncWindowsEnabled;
            if (chkRollback != null) chkRollback.IsChecked = op.EnableSyncRollback;
            if (chkHeartbeat != null) chkHeartbeat.IsChecked = op.FleetHeartbeatEnabled;
            if (chkBandwidth != null) chkBandwidth.IsChecked = op.BandwidthGuardEnabled;
            if (txtThreshold != null) txtThreshold.Text = op.BandwidthThresholdKbps.ToString();

            var windows = ConfigService.Current.Windows;
            if (windows != null)
            {
                if (txtFtpStart != null) txtFtpStart.Text = windows.Ftp.StartMinute.ToString();
                if (txtFtpEnd != null) txtFtpEnd.Text = windows.Ftp.EndMinute.ToString();
                if (txtSqlStart != null) txtSqlStart.Text = windows.Sql.StartMinute.ToString();
                if (txtSqlEnd != null) txtSqlEnd.Text = windows.Sql.EndMinute.ToString();
                if (txtMailStart != null) txtMailStart.Text = windows.Mailchimp.StartMinute.ToString();
                if (txtMailEnd != null) txtMailEnd.Text = windows.Mailchimp.EndMinute.ToString();
            }
            // Live feedback: show which services could run right now.
            void RefreshWindowPreview()
            {
                if (txtWindowPreview == null) return;

                var now = DateTime.Now;
                var parts = new List<string>();
                foreach (var (name, startTxt, endTxt) in new[]
                {
                    ("FTP", txtFtpStart, txtFtpEnd),
                    ("SQL", txtSqlStart, txtSqlEnd),
                    ("Mailchimp", txtMailStart, txtMailEnd)
                })
                {
                    if (!int.TryParse(startTxt?.Text, out var s) || !int.TryParse(endTxt?.Text, out var e)) continue;
                    var probe = new SyncWindowSettings { StartMinute = s, EndMinute = e };
                    parts.Add($"{name}: {(probe.IsAllDay ? "any time" : probe.Contains(now) ? "open now" : "closed now")}");
                }

                txtWindowPreview.Text = string.Join("  |  ", parts);
            }

            foreach (var tb in new[] { txtFtpStart, txtFtpEnd, txtSqlStart, txtSqlEnd, txtMailStart, txtMailEnd })
            {
                if (tb != null) tb.TextChanged += (_, _) => RefreshWindowPreview();
            }
            RefreshWindowPreview();

            // Keep the live upload readout fresh while the card is open.
            if (txtBandwidthNow != null)
            {
                var bwTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                bwTimer.Tick += (_, _) =>
                {
                    txtBandwidthNow.Text = $"Current upload: {BackupPolicyService.BandwidthGuard.CurrentKbps:F0} KB/s" +
                        (BackupPolicyService.BandwidthGuard.IsLinkBusy ? "  (busy - backups paused)" : "");
                };
                bwTimer.Start();

                // Stop it when the card is collapsed so it costs nothing while hidden.
                var card = this.FindControl<CollapsibleCardControl>("CardSmartScheduling");
                if (card != null)
                {
                    card.ExpansionChanged += (_, expanded) =>
                    {
                        if (expanded) bwTimer.Start();
                        else bwTimer.Stop();
                    };
                }
            }
            var btnSave = this.FindControl<Button>("BtnSaveSmartScheduling");
            if (btnSave != null && txtStatus != null)
            {
                btnSave.Click += (_, _) =>
                {
                    try
                    {
                        var cfg = ConfigService.Current.Operation;
                        cfg.EvictAiModelBeforeBackup = chkEvict?.IsChecked == true;
                        cfg.SyncWindowsEnabled = chkWindows?.IsChecked == true;
                        cfg.EnableSyncRollback = chkRollback?.IsChecked == true;
                        cfg.FleetHeartbeatEnabled = chkHeartbeat?.IsChecked == true;
                        cfg.BandwidthGuardEnabled = chkBandwidth?.IsChecked == true;

                        if (int.TryParse(txtThreshold?.Text, out var threshold))
                            cfg.BandwidthThresholdKbps = Math.Clamp(threshold, 1, 1_000_000);

                        ApplyWindow(txtFtpStart, txtFtpEnd, ConfigService.Current.Windows.Ftp);
                        ApplyWindow(txtSqlStart, txtSqlEnd, ConfigService.Current.Windows.Sql);
                        ApplyWindow(txtMailStart, txtMailEnd, ConfigService.Current.Windows.Mailchimp);

                        ConfigService.SaveOperation();

                        // Re-evaluate the guard immediately with the new threshold.
                        BackupPolicyService.BandwidthGuard.Reset();

                        txtStatus.Text = $"Saved at {TimeFormat.ClockSeconds(DateTime.Now)}.";
                        NotificationService.ShowBackupToast("Smart Scheduling", "Settings saved.", "Success");
                    }
                    catch (Exception ex)
                    {
                        txtStatus.Text = $"Save error: {ex.Message}";
                    }
                };
            }

            var btnPreview = this.FindControl<Button>("BtnPreviewSync");
            if (btnPreview != null && txtPreview != null && txtStatus != null)
            {
                btnPreview.Click += async (_, _) =>
                {
                    btnPreview.IsEnabled = false;
                    txtStatus.Text = "Comparing local files with the server...";

                    try
                    {
                        var plan = await SyncPreviewService.BuildPlanAsync("ftp");
                        txtPreview.Text = SyncPreviewService.DescribePlan(plan);
                        txtPreview.IsVisible = true;
                        txtStatus.Text = "Preview complete. Nothing was transferred.";
                    }
                    catch (Exception ex)
                    {
                        txtStatus.Text = $"Preview failed: {ex.Message}";
                    }
                    finally
                    {
                        btnPreview.IsEnabled = true;
                    }
                };
            }
        }

        private static void ApplyWindow(TextBox? startTxt, TextBox? endTxt, SyncWindowSettings? window)
        {
            if (window == null) return;
            if (int.TryParse(startTxt?.Text, out var s)) window.StartMinute = Math.Clamp(s, 0, 1440);
            if (int.TryParse(endTxt?.Text, out var e)) window.EndMinute = Math.Clamp(e, 0, 1440);
        }

        public void RefreshTelegramAlerts()
        {
            var chkTelegram = this.FindControl<CheckBox>("ChkTelegramEnabled");
            var txtToken = this.FindControl<TextBox>("TxtTelegramToken");
            var txtChatId = this.FindControl<TextBox>("TxtTelegramChatId");
            var chkStart = this.FindControl<CheckBox>("ChkNotifyBackupStart");
            var chkProg = this.FindControl<CheckBox>("ChkNotifyBackupProgress");
            var chkSucc = this.FindControl<CheckBox>("ChkNotifyBackupSuccess");
            var chkFail = this.FindControl<CheckBox>("ChkNotifyBackupFailure");
            var chkDisc = this.FindControl<CheckBox>("ChkNotifyDisconnect");
            var chkOut = this.FindControl<CheckBox>("ChkNotifyOutdated");

            NotificationService.LoadSettings();
            var s = NotificationService.Settings;

            if (chkTelegram != null) chkTelegram.IsChecked = s.TelegramEnabled;
            if (txtToken != null) txtToken.Text = s.TelegramBotToken;
            if (txtChatId != null) txtChatId.Text = s.TelegramChatId;
            if (chkStart != null) chkStart.IsChecked = s.NotifyOnBackupStart;
            if (chkProg != null) chkProg.IsChecked = s.NotifyOnBackupProgress;
            if (chkSucc != null) chkSucc.IsChecked = s.NotifyOnBackupSuccess;
            if (chkFail != null) chkFail.IsChecked = s.NotifyOnBackupFailure;
            if (chkDisc != null) chkDisc.IsChecked = s.NotifyOnDisconnect;
            if (chkOut != null) chkOut.IsChecked = s.NotifyOnOutdated;
        }

        // Backward compatibility
        public void RefreshEmailAlerts() => RefreshTelegramAlerts();

        private void InitializeTelegramAlerts()
        {
            var chkTelegram = this.FindControl<CheckBox>("ChkTelegramEnabled");
            var txtToken = this.FindControl<TextBox>("TxtTelegramToken");
            var txtChatId = this.FindControl<TextBox>("TxtTelegramChatId");
            var btnDetect = this.FindControl<Button>("BtnDetectChatId");
            var chkStart = this.FindControl<CheckBox>("ChkNotifyBackupStart");
            var chkProg = this.FindControl<CheckBox>("ChkNotifyBackupProgress");
            var chkSucc = this.FindControl<CheckBox>("ChkNotifyBackupSuccess");
            var chkFail = this.FindControl<CheckBox>("ChkNotifyBackupFailure");
            var chkDisc = this.FindControl<CheckBox>("ChkNotifyDisconnect");
            var chkOut = this.FindControl<CheckBox>("ChkNotifyOutdated");
            var btnSave = this.FindControl<Button>("BtnSaveTelegramSettings");
            var btnTest = this.FindControl<Button>("BtnSendTestTelegram");
            var btnSendQr = this.FindControl<Button>("BtnSendQrTelegram");
            var txtStatus = this.FindControl<TextBlock>("TxtTelegramStatus");

            RefreshTelegramAlerts();

            // Detect Chat ID helper
            if (btnDetect != null && txtToken != null && txtChatId != null)
            {
                btnDetect.Click += async (_, _) =>
                {
                    if (txtStatus != null) txtStatus.Text = "Querying recent messages to detect Chat ID...";
                    var (success, detectedChatId, msg) = await TelegramService.DetectChatIdAsync(txtToken.Text);
                    if (success && !string.IsNullOrWhiteSpace(detectedChatId))
                    {
                        txtChatId.Text = detectedChatId;
                        if (txtStatus != null) txtStatus.Text = msg;
                        NotificationService.ShowBackupToast("Chat ID Detected", $"Found: {detectedChatId}", "Success");
                    }
                    else
                    {
                        if (txtStatus != null) txtStatus.Text = msg;
                        NotificationService.ShowBackupToast("Chat ID Detection", msg, "Warning");
                    }
                };
            }

            // Save Telegram Settings
            if (btnSave != null)
            {
                btnSave.Click += (_, _) =>
                {
                    var s = NotificationService.Settings;
                    s.TelegramEnabled = chkTelegram?.IsChecked == true;
                    s.TelegramBotToken = txtToken?.Text?.Trim() ?? "";
                    s.TelegramChatId = txtChatId?.Text?.Trim() ?? "";
                    s.NotifyOnBackupStart = chkStart?.IsChecked == true;
                    s.NotifyOnBackupProgress = chkProg?.IsChecked == true;
                    s.NotifyOnBackupSuccess = chkSucc?.IsChecked == true;
                    s.NotifyOnBackupFailure = chkFail?.IsChecked == true;
                    s.NotifyOnDisconnect = chkDisc?.IsChecked == true;
                    s.NotifyOnOutdated = chkOut?.IsChecked == true;

                    NotificationService.SaveSettings();

                    if (txtStatus != null)
                    {
                        txtStatus.Text = s.TelegramEnabled
                            ? "Telegram Bot settings saved. Bot polling engine active."
                            : "Telegram settings saved (Bot disabled).";
                    }

                    NotificationService.ShowBackupToast("Telegram Bot", "Telegram settings saved successfully.", "Success");
                };
            }

            // Send Test Telegram Alert
            if (btnTest != null)
            {
                btnTest.Click += async (_, _) =>
                {
                    if (txtStatus != null) txtStatus.Text = "Sending verification test message to Telegram...";
                    var token = txtToken?.Text?.Trim();
                    var chat = txtChatId?.Text?.Trim();

                    var (success, msg) = await TelegramService.SendTestMessageAsync(token, chat);
                    if (txtStatus != null) txtStatus.Text = success ? "Test message delivered to Telegram! Check your app." : $"Test failed: {msg}";
                    NotificationService.ShowBackupToast("Telegram Test", success ? "Test message delivered!" : msg, success ? "Success" : "Error");
                };
            }

            // Send iOS QR Code to Telegram
            if (btnSendQr != null)
            {
                btnSendQr.Click += async (_, _) =>
                {
                    var chat = txtChatId?.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(chat))
                    {
                        NotificationService.ShowBackupToast("Telegram QR", "Please configure and save your Telegram Chat ID first.", "Warning");
                        return;
                    }

                    if (txtStatus != null) txtStatus.Text = "Generating pairing QR code and sending to Telegram...";
                    var (success, msg) = await TelegramService.SendConnectionQrAsync(chat);

                    if (txtStatus != null) txtStatus.Text = success ? "Pairing QR code sent to your Telegram!" : $"Failed to send QR: {msg}";
                    NotificationService.ShowBackupToast("Telegram QR", success ? "QR code sent to Telegram!" : msg, success ? "Success" : "Error");
                };
            }
        }

        /// <summary>
        /// Updates the health status label (called from MainWindow during initialization)
        /// </summary>
        public void UpdateHealthStatus(string status, bool isError = false)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var txtStatus = this.FindControl<TextBlock>("TxtHealthStatus");
                if (txtStatus != null)
                {
                    txtStatus.Text = $"Status: {status}";
                    txtStatus.Foreground = isError 
                        ? Avalonia.Media.Brush.Parse("#F38BA8") 
                        : Avalonia.Application.Current?.FindResource("AppSubtext") as Avalonia.Media.Brush;
                }
            });
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
                    // No Topmost - ShowDialog makes it modal to parent only
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
                    // No Topmost - ShowDialog makes it modal to parent only
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
                    // No Topmost - ShowDialog makes it modal to parent only
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
                    // No Topmost - ShowDialog makes it modal to parent only
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
                        // No Topmost - ShowDialog makes it modal to parent only
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

                // Ensure all nested objects are initialized before merging
                EnsureInitialized(existing);
                EnsureInitialized(config);
                
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

        private void EnsureInitialized(AppSettings settings)
        {
            settings.Paths ??= new PathsSettings();
            settings.Ftp ??= new FtpSettings();
            settings.Sql ??= new SqlSettings();
            settings.Mailchimp ??= new MailchimpSettings();
            settings.NetworkDrive ??= new NetworkDriveSettings();
            settings.Schedule ??= new ScheduleSettings();
            settings.Operation ??= new OperationSettings();
            settings.HttpServer ??= new HttpServerSettings();
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
                time.Text = TimeFormat.DateTimeShort(lastSuccess.Value);
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
                    // No Topmost - ShowDialog makes it modal to parent only
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.None
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
                    // No Topmost - ShowDialog makes it modal to parent only
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.None
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
                    // No Topmost - ShowDialog makes it modal to parent only
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.None
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
                    // No Topmost - ShowDialog makes it modal to parent only
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.None
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
                    // No Topmost - ShowDialog makes it modal to parent only
                    Background = Avalonia.Media.Brush.Parse("#0D1117"),
                    SystemDecorations = SystemDecorations.None
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
