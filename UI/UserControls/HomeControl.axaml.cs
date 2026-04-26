using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class HomeControl : UserControl
    {
        private readonly BackupManager _manager;
#pragma warning disable CS0649
        private System.Timers.Timer? _autoPingTimer;
        private System.Timers.Timer? _statsTimer;
        private System.Timers.Timer? _scheduleTimer;
        private System.Timers.Timer? _storageTimer;
        private System.Timers.Timer? _healthRefreshTimer;
#pragma warning restore CS0649
        private bool _compactMode = false;
        private int _activeOperations = 0;
        private System.Timers.Timer? _activeProcessUpdateTimer;
        private System.Timers.Timer? _statsRefreshTimer;
        private System.Timers.Timer? _dashboardRefreshTimer;

        public event Action? OnNavigateFtp;
        public event Action? OnNavigateMailchimp;
        public event Action? OnNavigateSql;
        public event Action? OnRunAllChecks;
        public event Action? OnEmergencyStop;
        public event Action? OnFtpSyncCheck;
        public event Action? OnFtpQuickBackup;
        public event Action? OnMailchimpSyncCheck;
        public event Action? OnMailchimpQuickBackup;
        public event Action? OnSqlSyncCheck;
        public event Action? OnSqlQuickBackup;

        private bool _autoPinged;
        private bool _maintenancePaused;

        public HomeControl() : this(null!)
        {
            // Load saved dashboard customization
            var savedSettings = DashboardCustomization.Load();
            _compactMode = savedSettings.CompactMode;
            if (_compactMode)
            {
                ApplyCompactMode(true);
                var btn = this.FindControl<Button>("BtnCompactToggle");
                if (btn != null) 
                {
                    btn.Content = "⊞ Expand";
                    btn.Foreground = Brush.Parse("#588157");
                }
            }
        }

        public HomeControl(BackupManager manager)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            _manager = manager;

            _manager.OnAutoScanTimersReset += OnAutoScanTimersReset;
            _manager.OnDailyScheduleUpdated += OnDailyScheduleUpdated;

            _manager.OnHealthUpdate += OnHealthUpdate;
            _manager.OnTimeUpdate += OnTimeUpdate;
            _manager.OnBackupProgress += OnBackupProgress;

            // Start active process update timer (every 1 second for real-time updates)
            _activeProcessUpdateTimer = new System.Timers.Timer(1000);
            _activeProcessUpdateTimer.Elapsed += (_, _) => UpdateActiveProcessDisplay();
            _activeProcessUpdateTimer.Start();

            this.FindControl<Button>("BtnGoFtp")!.Click += (_, _) => OnNavigateFtp?.Invoke();
            this.FindControl<Button>("BtnGoMailchimp")!.Click += (_, _) => OnNavigateMailchimp?.Invoke();
            this.FindControl<Button>("BtnGoSql")!.Click += (_, _) => OnNavigateSql?.Invoke();
            this.FindControl<Button>("BtnRunAllChecks")!.Click += (_, _) => OnRunAllChecks?.Invoke();
            this.FindControl<Button>("BtnRefreshActivity")!.Click += (_, _) => LoadRecentActivity();

            // Quick action buttons
            this.FindControl<Button>("BtnFtpSyncCheck")!.Click += (_, _) => OnFtpSyncCheck?.Invoke();
            this.FindControl<Button>("BtnFtpQuickBackup")!.Click += (_, _) => OnFtpQuickBackup?.Invoke();
            this.FindControl<Button>("BtnMcSyncCheck")!.Click += (_, _) => OnMailchimpSyncCheck?.Invoke();
            this.FindControl<Button>("BtnMcQuickBackup")!.Click += (_, _) => OnMailchimpQuickBackup?.Invoke();
            this.FindControl<Button>("BtnSqlSyncCheck")!.Click += (_, _) => OnSqlSyncCheck?.Invoke();
            this.FindControl<Button>("BtnSqlQuickBackup")!.Click += (_, _) => OnSqlQuickBackup?.Invoke();

            this.FindControl<Button>("BtnPingAll")!.Click += async (_, _) => await PingAllAsync();
            this.FindControl<Button>("BtnOpenSchedule")!.Click += async (_, _) => await OpenScheduleDialogAsync();
            this.FindControl<Button>("BtnBackupAll")!.Click += async (_, _) => await RunAllBackupsAsync();
            this.FindControl<Button>("BtnTestAllConn")!.Click += async (_, _) => await PingAllAsync();
            this.FindControl<Button>("BtnRetryFailed")!.Click += (_, _) => { SetOpStatus("Retrying all services...", "#dad7cd"); OnRunAllChecks?.Invoke(); SetOpStatus("Retry triggered. Check service tabs for results.", "#588157"); };
            this.FindControl<Button>("BtnCompactToggle")!.Click += (_, _) => ToggleCompactMode();
            this.FindControl<Button>("BtnEmergencyStop")!.Click += (_, _) => { OnEmergencyStop?.Invoke(); SetOpStatus("Emergency stop sent to all services.", "#F38BA8"); };
            this.FindControl<Button>("BtnMaintenanceToggle")!.Click += (_, _) => ToggleMaintenance();
            this.FindControl<Button>("BtnCustomizeDashboard")!.Click += (_, _) => ShowDashboardCustomization();
            this.FindControl<Button>("BtnClearErrors")!.Click += (_, _) => ClearRecentErrors();
            this.FindControl<Button>("BtnExportCsv")!.Click += (_, _) => ExportActivityCsv();

            this.FindControl<Button>("BtnFtpFiles")!.Click += (_, _) => ToggleFileBrowser("Ftp", BackupConfig.FtpLocalFolder);
            this.FindControl<Button>("BtnMcFiles")!.Click  += (_, _) => ToggleFileBrowser("Mc",  BackupConfig.MailchimpFolder);
            this.FindControl<Button>("BtnSqlFiles")!.Click += (_, _) => ToggleFileBrowser("Sql", BackupConfig.SqlLocalFolder);
            this.FindControl<Button>("BtnFtpOpenFolder")!.Click += (_, _) => OpenFolder(BackupConfig.FtpLocalFolder);
            this.FindControl<Button>("BtnMcOpenFolder")!.Click  += (_, _) => OpenFolder(BackupConfig.MailchimpFolder);
            this.FindControl<Button>("BtnSqlOpenFolder")!.Click += (_, _) => OpenFolder(BackupConfig.SqlLocalFolder);

            this.FindControl<Button>("BtnRefreshHealth")!.Click += (_, _) => _ = LoadHealthDashboardAsync();
            this.FindControl<Button>("BtnClearSystemLogs")!.Click += (_, _) => ClearSystemLogs();
            this.FindControl<Button>("BtnRefreshSystemLogs")!.Click += (_, _) => _ = LoadSystemLogsAsync();
            this.FindControl<Button>("BtnViewLogsInNotepad")!.Click += (_, _) => ViewLogsInNotepad();
            this.FindControl<Button>("BtnRefreshFirebaseLogs")!.Click += (_, _) => _ = LoadFirebaseLogsAsync();
            this.FindControl<Button>("BtnViewFirebaseLogs")!.Click += (_, _) => ViewLogsInNotepad();

            UpdateGreeting();
            UpdateDailySchedule();
            LoadRecentActivity();
            UpdateSchedSummary();
            _ = UpdateStorageAsync();
            _ = LoadWeeklyStatsAsync();
            _ = LoadLastBackupSummariesAsync();
            _ = LoadHealthDashboardAsync();
            
            // Start auto-refresh for health dashboard and stats
            StartHealthAutoRefresh();
            StartStatsAutoRefresh();
            
            // Subscribe to system log events
            LogService.OnNewLogEntry += OnNewSystemLogEntry;

            // Subscribe to schedule changes from Firebase
            ConfigService.OnScheduleChanged += OnScheduleChangedFromFirebase;

            // Load system logs
            _ = LoadSystemLogsAsync();

            // Load Firebase logs
            _ = LoadFirebaseLogsAsync();
            
            // Initialize new dashboard features
            _ = UpdateSystemStatusAsync();
            _ = UpdateQuickStatsAsync();
            _ = UpdateTimeSinceLastBackupAsync();
            _ = LoadRecentErrorsAsync();
            
            // Initialize service status immediately
            UpdateServicesStatusSummary(null);
            
            // Start dashboard auto-refresh (every 30 seconds)
            StartDashboardAutoRefresh();
        }

        private async Task LoadHealthDashboardAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var health = BackupHealthService.CalculateHealthScore();
                    
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        // Update overall score
                        var scoreText = this.FindControl<TextBlock>("HealthScoreText");
                        var trendText = this.FindControl<TextBlock>("HealthTrendText");
                        
                        if (scoreText != null) scoreText.Text = $"{health.OverallScore}%";
                        if (trendText != null) trendText.Text = $"{health.Trend} {health.TrendText}";
                        
                        // Update service scores
                        var ftpScore = this.FindControl<TextBlock>("HealthFtpScore");
                        var mcScore = this.FindControl<TextBlock>("HealthMcScore");
                        var sqlScore = this.FindControl<TextBlock>("HealthSqlScore");
                        
                        if (ftpScore != null) ftpScore.Text = $"{health.ServiceScores.GetValueOrDefault("FTP", 0)}%";
                        if (mcScore != null) mcScore.Text = $"{health.ServiceScores.GetValueOrDefault("Mailchimp", 0)}%";
                        if (sqlScore != null) sqlScore.Text = $"{health.ServiceScores.GetValueOrDefault("SQL", 0)}%";
                        
                        // Update services status summary
                        UpdateServicesStatusSummary(health.ServiceScores);
                        
                        // Update critical alerts
                        var alertsCount = this.FindControl<TextBlock>("CriticalAlertsCount");
                        var alertsPanel = this.FindControl<Border>("CriticalAlertsPanel");
                        var alertsList = this.FindControl<StackPanel>("CriticalAlertsList");
                        
                        if (alertsCount != null) alertsCount.Text = health.CriticalAlerts.Count.ToString();
                        if (alertsPanel != null && alertsList != null)
                        {
                            alertsPanel.IsVisible = health.CriticalAlerts.Count > 0;
                            alertsList.Children.Clear();
                            
                            foreach (var alert in health.CriticalAlerts.Take(5))
                            {
                                var alertBorder = new Border
                                {
                                    Background = Avalonia.Media.Brush.Parse("#3D1515"),
                                    CornerRadius = new Avalonia.CornerRadius(6),
                                    Padding = new Avalonia.Thickness(10, 6),
                                    Margin = new Avalonia.Thickness(0, 2)
                                };
                                
                                var alertGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };
                                
                                // Service icon
                                var serviceIcon = new TextBlock
                                {
                                    Text = alert.Service switch
                                    {
                                        "FTP" => "FTP",
                                        "Mailchimp" => "MC",
                                        "SQL" => "SQL",
                                        _ => "ERR"
                                    },
                                    FontSize = 12,
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                    Margin = new Avalonia.Thickness(0, 0, 8, 0)
                                };
                                
                                // Alert message
                                var messageText = new TextBlock
                                {
                                    Text = alert.Message,
                                    FontSize = 10,
                                    Foreground = Avalonia.Media.Brush.Parse("#F38BA8"),
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                                };
                                
                                // Time ago
                                var timeText = new TextBlock
                                {
                                    Text = alert.AgeText,
                                    FontSize = 9,
                                    Foreground = Avalonia.Media.Brush.Parse("#6C7086"),
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                                };
                                
                                Grid.SetColumn(serviceIcon, 0);
                                Grid.SetColumn(messageText, 1);
                                Grid.SetColumn(timeText, 2);
                                
                                alertGrid.Children.Add(serviceIcon);
                                alertGrid.Children.Add(messageText);
                                alertGrid.Children.Add(timeText);
                                
                                alertBorder.Child = alertGrid;
                                alertsList.Children.Add(alertBorder);
                            }
                            
                            if (health.CriticalAlerts.Count == 0)
                            {
                                var noAlerts = new TextBlock
                                {
                                    Text = "No critical alerts - all systems healthy!",
                                    FontSize = 10,
                                    Foreground = Avalonia.Media.Brush.Parse("#588157"),
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                                };
                                alertsList.Children.Add(noAlerts);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[HEALTH] Error loading health dashboard: {ex.Message}", "", "Error", "SYSTEM");
                }
            });
        }

        private void UpdateGreeting()
        {
            try
            {
                var mnlTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
                var hour = mnlTime.Hour;
                var greeting = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
                var username = AuthService.CurrentUser?.Username ?? "User";

                var txt = this.FindControl<TextBlock>("TxtGreeting");
                if (txt != null) txt.Text = $"{greeting}, {username}";

                var sub = this.FindControl<TextBlock>("TxtSubtitle");
                if (sub != null) sub.Text = mnlTime.ToString("dddd, MMMM d · hh:mm tt") + " Manila";
            }
            catch { }
        }

        private void OnTimeUpdate(DateTime now, DateTime mnlTime, DateTime nextFtp, DateTime nextFtpDaily)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var sub = this.FindControl<TextBlock>("TxtTime");
                if (sub != null) sub.Text = mnlTime.ToString("dddd, MMMM d · hh:mm tt") + " Manila";
                
                // Update timer displays
                SetTimer("FtpNextScan", _manager.NextFtpAutoScan, now);
                SetTimer("MailchimpNextScan", _manager.NextMailchimpAutoScan, now);
                SetTimer("SqlNextScan", _manager.NextSqlAutoScan, now);
                UpdateDailySchedule(mnlTime);

                // Update schedule overview
                UpdateScheduleOverview(now);
            });
        }

        private DateTime _lastBackupProgressUpdate = DateTime.MinValue;

        private void OnBackupProgress(string service, int percent, string status)
        {
            _lastBackupProgressUpdate = DateTime.UtcNow;
            
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var progressBar = this.FindControl<ProgressBar>("GlobalBackupProgress");
                var progressText = this.FindControl<TextBlock>("BackupProgressText");
                var progressPercent = this.FindControl<TextBlock>("BackupProgressPercent");

                if (progressBar != null)
                {
                    progressBar.Value = percent;
                }

                if (progressText != null)
                {
                    progressText.Text = $"{service}: {status}";
                }

                if (progressPercent != null)
                {
                    progressPercent.Text = percent + "%";
                }
            });
        }

        private void ResetGlobalBackupProgressIfIdle()
        {
            // Reset progress bar if no backup activity for 10 seconds
            if ((DateTime.UtcNow - _lastBackupProgressUpdate).TotalSeconds > 10 && _lastBackupProgressUpdate != DateTime.MinValue)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var progressBar = this.FindControl<ProgressBar>("GlobalBackupProgress");
                    var progressText = this.FindControl<TextBlock>("BackupProgressText");
                    var progressPercent = this.FindControl<TextBlock>("BackupProgressPercent");

                    if (progressBar != null)
                    {
                        progressBar.Value = 0;
                    }

                    if (progressText != null)
                    {
                        progressText.Text = "No active backups";
                    }

                    if (progressPercent != null)
                    {
                        progressPercent.Text = "0%";
                    }
                    
                    _lastBackupProgressUpdate = DateTime.MinValue;
                });
            }
        }

        private void OnAutoScanTimersReset()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var now = DateTime.Now;
                SetTimer("FtpNextScan", _manager.NextFtpAutoScan, now);
                SetTimer("MailchimpNextScan", _manager.NextMailchimpAutoScan, now);
                SetTimer("SqlNextScan", _manager.NextSqlAutoScan, now);
                UpdateScheduleOverview(now);
            });
        }

        private void OnDailyScheduleUpdated()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var now = DateTime.Now;
                UpdateScheduleOverview(now);
                UpdateDailySchedule(now.AddHours(15));
            });
        }

        private void OnHealthUpdate(List<BackupHealthReport> reports)
        {
            if (!_autoPinged) { _autoPinged = true; _ = PingAllAsync(); }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                int servicesOk = 0;
                var alertServices = new List<string>();

                foreach (var report in reports)
                {
                    var color = ColorFromReport(report.Color);
                    bool ok = !report.NeedsSync;
                    if (ok) servicesOk++;
                    else alertServices.Add(FriendlyServiceName(report.Service));

                    switch (report.Service)
                    {
                        case "Website":
                            SetCard("Ftp", color, report.Status, report.LastUpdate);
                            break;
                        case "Mailchimp":
                            SetCard("Mailchimp", color, report.Status, report.LastUpdate);
                            break;
                        case "Database":
                            SetCard("Sql", color, report.Status, report.LastUpdate);
                            break;
                    }
                }

                bool allOk = alertServices.Count == 0;
                var healthBrush = allOk ? Brush.Parse("#588157") : Brush.Parse("#F38BA8");

                var dot = this.FindControl<Ellipse>("DashHealthDot");
                var txt = this.FindControl<TextBlock>("DashHealthText");
                if (dot != null) dot.Fill = healthBrush;
                if (txt != null) { txt.Text = allOk ? "ALL SYSTEMS OK" : "ATTENTION REQUIRED"; txt.Foreground = healthBrush; }

                var alertBanner = this.FindControl<Border>("AlertBanner");
                var alertText = this.FindControl<TextBlock>("AlertText");
                if (alertBanner != null) alertBanner.IsVisible = !allOk;
                if (alertText != null && !allOk)
                    alertText.Text = $"Sync required: {string.Join(", ", alertServices)}. Open the relevant tab or use Run All Checks.";

                var statOk = this.FindControl<TextBlock>("StatServicesOk");
                if (statOk != null)
                {
                    statOk.Text = $"{servicesOk}/3";
                    statOk.Foreground = allOk ? Brush.Parse("#588157") : Brush.Parse("#F38BA8");
                }

                UpdateGreeting();
                
                // Update retry failed button state based on failed backups
                _ = UpdateRetryFailedButtonStateAsync();
            });
        }

        private void SetCard(string prefix, IBrush color, string status, string lastSync)
        {
            var dot = this.FindControl<Ellipse>($"{prefix}StatusDot");
            var txt = this.FindControl<TextBlock>($"{prefix}StatusText");
            var last = this.FindControl<TextBlock>($"{prefix}LastSync");

            if (dot != null) dot.Fill = color;
            if (txt != null) { txt.Text = status; txt.Foreground = color; }
            if (last != null) last.Text = string.IsNullOrWhiteSpace(lastSync) ? "Never" : lastSync;
        }

        private void SetTimer(string controlName, DateTime next, DateTime now)
        {
            var txt = this.FindControl<TextBlock>(controlName);
            if (txt == null) return;
            var diff = next - now;
            txt.Text = diff.TotalSeconds > 0 ? diff.ToString(@"hh\:mm\:ss") : "Due now";
        }

        private void UpdateScheduleOverview(DateTime now)
        {
            // Update schedule times for each service
            Set("FtpScheduleTime", _manager.NextFtpAutoScan.ToString("hh:mm tt"));
            Set("McScheduleTime", _manager.NextMailchimpAutoScan.ToString("hh:mm tt"));
            Set("SqlScheduleTime", _manager.NextSqlAutoScan.ToString("hh:mm tt"));

            // Calculate countdown to next backup
            var nextBackup = new[] { _manager.NextFtpAutoScan, _manager.NextMailchimpAutoScan, _manager.NextSqlAutoScan }
                .Where(d => d > now)
                .OrderBy(d => d)
                .FirstOrDefault();

            if (nextBackup != default)
            {
                var diff = nextBackup - now;
                string countdown;
                if (diff.TotalHours < 1)
                    countdown = $"{diff.TotalMinutes:F0}m";
                else if (diff.TotalHours < 24)
                    countdown = $"{diff.TotalHours:F1}h";
                else
                    countdown = $"{diff.TotalDays:F1}d";

                Set("NextBackupCountdown", $"Next in: {countdown}");
            }
            else
            {
                Set("NextBackupCountdown", "Next in: --");
            }

            // Update status based on whether the service is due
            Set("FtpScheduleStatus", _manager.NextFtpAutoScan <= now ? "Due now" : "Scheduled");
            Set("McScheduleStatus", _manager.NextMailchimpAutoScan <= now ? "Due now" : "Scheduled");
            Set("SqlScheduleStatus", _manager.NextSqlAutoScan <= now ? "Due now" : "Scheduled");
        }

        private void UpdateDailySchedule(DateTime? mnlNow = null)
        {
            void SetSched(string ctrl, DateTime next, DateTime now)
            {
                var tb = this.FindControl<TextBlock>(ctrl);
                if (tb == null) return;
                var diff = next - now;
                tb.Text = diff.TotalSeconds > 0
                    ? $"in {diff.ToString(@"hh\:mm\:ss")}"
                    : "Due now";
            }

            try
            {
                var now = mnlNow ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

                var ftpNext = BackupManager.NextFtpDailySyncMnl;
                var mcNext = BackupManager.NextMailchimpDailySyncMnl;
                var sqlNext = BackupManager.NextSqlDailySyncMnl;

                LogService.WriteSystemLog($"[HOMECTRL] UpdateDailySchedule - FTP: {ftpNext:HH:mm}, MC: {mcNext:HH:mm}, SQL: {sqlNext:HH:mm}", "Information", "SYSTEM");

                SetSched("SchedFtp", ftpNext, now);
                SetSched("SchedMailchimp", mcNext, now);
                SetSched("SchedSql", sqlNext, now);
            }
            catch { }
        }

        private async Task UpdateStorageAsync()
        {
            try
            {
                LogService.WriteLiveLog("[STORAGE] Starting storage calculation...", "", "Information", "SYSTEM");
                
                var ftpSize = await Task.Run(() => GetFolderSize(BackupConfig.FtpLocalFolder));
                var mcSize = await Task.Run(() => GetFolderSize(BackupConfig.MailchimpFolder));
                var sqlSize = await Task.Run(() => GetFolderSize(BackupConfig.SqlLocalFolder));

                var ftpCount = await Task.Run(() => GetFileCount(BackupConfig.FtpLocalFolder));
                var mcCount = await Task.Run(() => GetFileCount(BackupConfig.MailchimpFolder));
                var sqlCount = await Task.Run(() => GetFileCount(BackupConfig.SqlLocalFolder));

                long totalSize = ftpSize + mcSize + sqlSize;
                int totalFiles = ftpCount + mcCount + sqlCount;
                long maxSize = Math.Max(1, Math.Max(ftpSize, Math.Max(mcSize, sqlSize)));

                // Get total HDD storage
                long totalDiskSpace = await Task.Run(() => GetTotalDiskSpace());

                LogService.WriteLiveLog($"[STORAGE] FTP: {FormatSize(ftpSize)}, MC: {FormatSize(mcSize)}, SQL: {FormatSize(sqlSize)}, Total: {FormatSize(totalSize)}/{FormatSize(totalDiskSpace)}", "", "Information", "SYSTEM");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Set("StorageFtp", FormatSize(ftpSize));
                    Set("StorageMailchimp", FormatSize(mcSize));
                    Set("StorageSql", FormatSize(sqlSize));
                    Set("StorageTotal", FormatSize(totalSize));
                    Set("StatStorage", $"{FormatSize(totalSize)}/{FormatSize(totalDiskSpace)}");
                    Set("StatTotalFiles", totalFiles.ToString("N0"));

                    // Update breakdown section
                    Set("StorageFtpBreakdown", FormatSize(ftpSize));
                    Set("StorageMcBreakdown", FormatSize(mcSize));
                    Set("StorageSqlBreakdown", FormatSize(sqlSize));

                    // Calculate percentages
                    double ftpPercent = totalSize > 0 ? (double)ftpSize / totalSize * 100 : 0;
                    double mcPercent = totalSize > 0 ? (double)mcSize / totalSize * 100 : 0;
                    double sqlPercent = totalSize > 0 ? (double)sqlSize / totalSize * 100 : 0;

                    SetBar("StorageFtpBar", ftpSize, maxSize);
                    SetBar("StorageMailchimpBar", mcSize, maxSize);
                    SetBar("StorageSqlBar", sqlSize, maxSize);

                    // Update breakdown section progress bars
                    SetBar("StorageFtpBarBreakdown", ftpSize, maxSize);
                    SetBar("StorageMcBarBreakdown", mcSize, maxSize);
                    SetBar("StorageSqlBarBreakdown", sqlSize, maxSize);

                    Set("StorageFtpPercent", $"{ftpPercent:F1}%");
                    Set("StorageMcPercent", $"{mcPercent:F1}%");
                    Set("StorageSqlPercent", $"{sqlPercent:F1}%");

                    // Update breakdown section percentages
                    Set("StorageFtpPercentBreakdown", $"{ftpPercent:F1}%");
                    Set("StorageMcPercentBreakdown", $"{mcPercent:F1}%");
                    Set("StorageSqlPercentBreakdown", $"{sqlPercent:F1}%");
                });
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[STORAGE] Error calculating storage: {ex.Message}", "", "Error", "SYSTEM");
            }
            
            // Update retry failed button state
            await UpdateRetryFailedButtonStateAsync();
        }

        private async Task UpdateRetryFailedButtonStateAsync()
        {
            try
            {
                var hasFailedBackups = await Task.Run(() => CheckForFailedBackups());
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var btnRetryFailed = this.FindControl<Button>("BtnRetryFailed");
                    if (btnRetryFailed != null)
                    {
                        btnRetryFailed.IsEnabled = hasFailedBackups;
                        btnRetryFailed.Opacity = hasFailedBackups ? 1.0 : 0.5;
                    }
                });
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[RETRY] Error checking failed backups: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private static bool CheckForFailedBackups()
        {
            try
            {
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                
                // Check FTP logs
                var ftpLogs = File.Exists(BackupConfig.FtpLogFile) 
                    ? File.ReadAllLines(BackupConfig.FtpLogFile).Where(l => l.Contains(today)).ToList() 
                    : new List<string>();
                var ftpFailed = ftpLogs.Any(l => l.Contains("ERROR") || l.Contains("FAILED"));
                
                // Check Mailchimp logs
                var mcLogs = File.Exists(BackupConfig.McLogFile) 
                    ? File.ReadAllLines(BackupConfig.McLogFile).Where(l => l.Contains(today)).ToList() 
                    : new List<string>();
                var mcFailed = mcLogs.Any(l => l.Contains("ERROR") || l.Contains("FAILED"));
                
                // Check SQL logs
                var sqlLogs = File.Exists(BackupConfig.SqlLogFile) 
                    ? File.ReadAllLines(BackupConfig.SqlLogFile).Where(l => l.Contains(today)).ToList() 
                    : new List<string>();
                var sqlFailed = sqlLogs.Any(l => l.Contains("ERROR") || l.Contains("FAILED"));
                
                return ftpFailed || mcFailed || sqlFailed;
            }
            catch
            {
                return false;
            }
        }

        private static long GetTotalDiskSpace()
        {
            try
            {
                // Get the drive where the first backup folder is located
                string backupPath = BackupConfig.FtpLocalFolder;
                if (string.IsNullOrEmpty(backupPath) || !System.IO.Path.IsPathRooted(backupPath))
                {
                    backupPath = BackupConfig.MailchimpFolder;
                }
                if (string.IsNullOrEmpty(backupPath) || !System.IO.Path.IsPathRooted(backupPath))
                {
                    backupPath = BackupConfig.SqlLocalFolder;
                }
                
                if (string.IsNullOrEmpty(backupPath))
                {
                    return 0;
                }
                
                var driveRoot = System.IO.Path.GetPathRoot(backupPath);
                if (string.IsNullOrEmpty(driveRoot))
                {
                    return 0;
                }
                
                var driveInfo = new DriveInfo(driveRoot);
                return driveInfo.TotalSize;
            }
            catch
            {
                return 0;
            }
        }

        private void LoadRecentActivity()
        {
            _ = Task.Run(() =>
            {
                var entries = new List<(DateTime ts, string service, string level, string msg)>();

                void ParseLog(string path, string service)
                {
                    try
                    {
                        var lines = LogService.ImportLatestLogs(path, 30);
                        foreach (var line in lines)
                        {
                            if (TryParseLogLine(line, out var ts, out var level, out var msg))
                                entries.Add((ts, service, level, msg));
                        }
                    }
                    catch { }
                }

                ParseLog(BackupConfig.FtpLogFile, "FTP");
                ParseLog(BackupConfig.McLogFile, "MC");
                ParseLog(BackupConfig.SqlLogFile, "SQL");

                var sorted = entries.OrderByDescending(e => e.ts).Take(10).ToList();

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var list = this.FindControl<StackPanel>("ActivityList");
                    if (list == null) return;
                    list.Children.Clear();

                    if (sorted.Count == 0)
                    {
                        list.Children.Add(new TextBlock
                        {
                            Text = "No activity found.",
                            Foreground = Brush.Parse("#6C7086"),
                            FontSize = 11
                        });
                        return;
                    }

                    foreach (var (ts, service, level, msg) in sorted)
                    {
                        var svcColor = service switch
                        {
                            "FTP" => "#588157",
                            "MC" => "#00b4d8",
                            "SQL" => "#fad643",
                            _ => "#6C7086"
                        };
                        var lvlColor = level switch
                        {
                            "ERROR" => "#F38BA8",
                            "WARNING" => "#dad7cd",
                            _ => "#6C7086"
                        };

                        var row = new Grid();
                        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                        var svcBadge = new Border
                        {
                            Background = Brush.Parse("#11111B"),
                            CornerRadius = new Avalonia.CornerRadius(4),
                            Padding = new Avalonia.Thickness(6, 2),
                            Margin = new Avalonia.Thickness(0, 0, 8, 0),
                            Child = new TextBlock
                            {
                                Text = service,
                                FontSize = 9,
                                FontWeight = Avalonia.Media.FontWeight.Bold,
                                Foreground = Brush.Parse(svcColor)
                            }
                        };
                        Grid.SetColumn(svcBadge, 0);

                        var lvlDot = new Ellipse
                        {
                            Width = 6, Height = 6,
                            Fill = Brush.Parse(lvlColor),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(0, 0, 8, 0)
                        };
                        Grid.SetColumn(lvlDot, 1);

                        var msgTxt = new TextBlock
                        {
                            Text = msg,
                            FontSize = 10,
                            Foreground = Brush.Parse("#6C7086"),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                        };
                        Grid.SetColumn(msgTxt, 2);

                        var timeTxt = new TextBlock
                        {
                            Text = ts.ToString("HH:mm"),
                            FontSize = 9,
                            Foreground = Brush.Parse("#6C7086"),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(8, 0, 0, 0)
                        };
                        Grid.SetColumn(timeTxt, 3);

                        row.Children.Add(svcBadge);
                        row.Children.Add(lvlDot);
                        row.Children.Add(msgTxt);
                        row.Children.Add(timeTxt);
                        list.Children.Add(row);
                    }
                });
            });
        }

        private static bool TryParseLogLine(string line, out DateTime ts, out string level, out string msg)
        {
            ts = DateTime.MinValue; level = "INFO"; msg = line;
            try
            {
                if (!line.StartsWith("[")) return false;
                var p1 = line.IndexOf(']');
                if (p1 < 0) return false;
                if (!DateTime.TryParse(line.Substring(1, p1 - 1), out ts)) return false;

                var rest = line.Substring(p1 + 1).TrimStart();
                if (rest.StartsWith("["))
                {
                    var p2 = rest.IndexOf(']');
                    if (p2 >= 0) { level = rest.Substring(1, p2 - 1); rest = rest.Substring(p2 + 1).TrimStart(); }
                }
                if (rest.StartsWith("["))
                {
                    var p3 = rest.IndexOf(']');
                    if (p3 >= 0) rest = rest.Substring(p3 + 1).TrimStart();
                }
                msg = rest.Trim();
                return true;
            }
            catch { return false; }
        }

        private void Set(string name, string value)
        {
            var tb = this.FindControl<TextBlock>(name);
            if (tb != null) tb.Text = value;
        }

        private void SetBar(string name, long value, long max)
        {
            var pb = this.FindControl<ProgressBar>(name);
            if (pb == null) return;
            pb.Maximum = 100;
            pb.Value = max > 0 ? (double)value / max * 100 : 0;
        }

        private static long GetFolderSize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }

        private static int GetFileCount(string path)
        {
            if (!Directory.Exists(path)) return 0;
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Count(f => !f.Name.Equals("backup_log.txt", StringComparison.OrdinalIgnoreCase));
        }

        private static string FriendlyServiceName(string service) => service switch
        {
            "Website" => "FTP",
            "Database" => "SQL",
            _ => service
        };

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static IBrush ColorFromReport(string color) => color switch
        {
            "LimeGreen" => Brush.Parse("#588157"),
            "Orange" => Brush.Parse("#dad7cd"),
            "Red" => Brush.Parse("#F38BA8"),
            _ => Brush.Parse("#6C7086")
        };

        public void IncrementActiveOperations() => _activeOperations++;
        public void DecrementActiveOperations() => _activeOperations = Math.Max(0, _activeOperations - 1);
        public void SetActiveOperations(int count) => _activeOperations = count;

        public void SetMaximizedLayout(bool isMaximized)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var quickStatsGrid = this.FindControl<Grid>("QuickStatsGrid");
                var serviceCardsGrid = this.FindControl<Grid>("ServiceCardsSection");
                
                if (quickStatsGrid != null)
                {
                    quickStatsGrid.MaxWidth = isMaximized ? double.PositiveInfinity : 1200;
                }
                
                if (serviceCardsGrid != null)
                {
                    serviceCardsGrid.MaxWidth = isMaximized ? double.PositiveInfinity : 1200;
                }
            });
        }

        private void UpdateActiveProcessDisplay()
        {
            // Check if global backup progress should be reset (no activity for 10 seconds)
            ResetGlobalBackupProgressIfIdle();
            
            var activeProcessesText = _manager.IsPaused ? "Paused" : $"{_activeOperations} active";
            Dispatcher.UIThread.Post(() =>
            {
                var processesTextBlock = this.FindControl<TextBlock>("ActiveProcesses");
                if (processesTextBlock != null) processesTextBlock.Text = activeProcessesText;
            });
        }

        // ── Schedule Adjustment ──────────────────────────────────────────────

        private void UpdateSchedSummary()
        {
            var s = ConfigService.Current.Schedule;
            string Fmt(int h, int m) { bool pm = h >= 12; int h12 = h % 12; if (h12 == 0) h12 = 12; return $"{h12}:{m:D2} {(pm ? "PM" : "AM")}"; }
            var summary = $"FTP {Fmt(s.FtpDailySyncHourMnl, s.FtpDailySyncMinuteMnl)}  ·  MC {Fmt(s.MailchimpDailySyncHourMnl, s.MailchimpDailySyncMinuteMnl)}  ·  SQL {Fmt(s.SqlDailySyncHourMnl, s.SqlDailySyncMinuteMnl)}  (MNL)";
            var tb = this.FindControl<TextBlock>("SchedSummaryText");
            if (tb != null) tb.Text = summary;
        }

        private async Task OpenScheduleDialogAsync()
        {
            var parentWindow = this.VisualRoot as Avalonia.Controls.Window;
            if (parentWindow == null) return;
            var dialog = new ScheduleDialog();
            var saved = await dialog.ShowDialog<bool?>(parentWindow);
            if (saved == true)
            {
                UpdateSchedSummary();
                UpdateDailySchedule();
                NotificationService.ShowBackupToast("Schedule", "Schedule updated — timers now active.", "Success");
            }
        }

        private async Task RunAllBackupsAsync()
        {
            var startTime = DateTime.Now;
            SetOpStatus("Running backup checks on all services...", "#dad7cd");
            NotificationService.ShowBackupToast("Dashboard", "Running backup checks on all services...", "Info");
            
            // Trigger the checks via event
            OnRunAllChecks?.Invoke();
            
            // Wait for checks to complete (approximate wait)
            await Task.Delay(3000);
            
            // Run health check to get current status
            await _manager.RunHealthCheckAsync();
            
            var endTime = DateTime.Now;
            var duration = endTime - startTime;
            var durationMinutes = duration.TotalMinutes.ToString("F1");
            
            // Determine which services were updated by checking logs
            var completedServices = new List<string>();
            var failedServices = new List<string>();
            var recentLogs = LogService.ImportLatestLogs(AppDataPaths.SystemLogPath, 100);
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            
            foreach (var log in recentLogs.Where(l => l.Contains(today)))
            {
                // Check for completed backups
                if (log.Contains("FTP") && (log.Contains("COMPLETE") || log.Contains("SUCCESS") || log.Contains("Backup complete") || log.Contains("SYNC COMPLETE")))
                {
                    if (!completedServices.Contains("FTP")) completedServices.Add("FTP");
                }
                if (log.Contains("Mailchimp") && (log.Contains("COMPLETE") || log.Contains("SUCCESS") || log.Contains("Backup complete") || log.Contains("SYNC COMPLETE")))
                {
                    if (!completedServices.Contains("Mailchimp")) completedServices.Add("Mailchimp");
                }
                if (log.Contains("SQL") && (log.Contains("COMPLETE") || log.Contains("SUCCESS") || log.Contains("Backup complete") || log.Contains("SYNC COMPLETE")))
                {
                    if (!completedServices.Contains("SQL")) completedServices.Add("SQL");
                }
                
                // Check for failed backups
                if (log.Contains("FTP") && (log.Contains("FAILED") || log.Contains("ERROR") || log.Contains("Exception")))
                {
                    if (!failedServices.Contains("FTP") && !completedServices.Contains("FTP")) failedServices.Add("FTP");
                }
                if (log.Contains("Mailchimp") && (log.Contains("FAILED") || log.Contains("ERROR") || log.Contains("Exception")))
                {
                    if (!failedServices.Contains("Mailchimp") && !completedServices.Contains("Mailchimp")) failedServices.Add("Mailchimp");
                }
                if (log.Contains("SQL") && (log.Contains("FAILED") || log.Contains("ERROR") || log.Contains("Exception")))
                {
                    if (!failedServices.Contains("SQL") && !completedServices.Contains("SQL")) failedServices.Add("SQL");
                }
            }
            
            // Build detailed status message
            string statusMessage;
            string toastMessage;
            string color = "#588157";
            
            if (completedServices.Count == 0 && failedServices.Count == 0)
            {
                statusMessage = "All backups are up to date.";
                toastMessage = "All backups are up to date.";
            }
            else if (completedServices.Count == 3 && failedServices.Count == 0)
            {
                statusMessage = $"All backups completed successfully ({durationMinutes}m)";
                toastMessage = $"All backups completed successfully ({durationMinutes}m)";
            }
            else if (failedServices.Count > 0)
            {
                var completedList = completedServices.Count > 0 ? string.Join(", ", completedServices) : "none";
                var failedList = string.Join(", ", failedServices);
                statusMessage = $"Completed: {completedList} | Failed: {failedList} ({durationMinutes}m)";
                toastMessage = $"Completed: {completedList} | Failed: {failedList} ({durationMinutes}m)";
                color = "#F38BA8";
            }
            else
            {
                var servicesList = string.Join(", ", completedServices);
                statusMessage = $"Backup complete: {servicesList} ({durationMinutes}m)";
                toastMessage = $"Backup complete: {servicesList} ({durationMinutes}m)";
            }
            
            SetOpStatus(statusMessage, color);
            NotificationService.ShowBackupToast("Backup Complete", toastMessage, failedServices.Count > 0 ? "Error" : "Success");
        }

        private void ToggleCompactMode()
        {
            _compactMode = !_compactMode;
            ApplyCompactMode(_compactMode);
            var btn = this.FindControl<Button>("BtnCompactToggle");
            if (btn != null) 
            {
                btn.Content = _compactMode ? "⊞ Expand" : "⊟ Compact";
                btn.Foreground = _compactMode ? Brush.Parse("#588157") : Brushes.Gray;
            }
            
            // Save the compact mode setting
            var settings = DashboardCustomization.Load();
            settings.CompactMode = _compactMode;
            DashboardCustomization.Save(settings);
        }

        // ── Connectivity ─────────────────────────────────────────────────────

        private async Task PingAllAsync()
        {
            SetPing("Ftp", "#dad7cd", "Checking...");
            SetPing("Sql", "#dad7cd", "Checking...");
            SetPing("Mc",  "#dad7cd", "Checking...");
            SetOpStatus("Testing all connections...", "#dad7cd");

            await Task.WhenAll(
                TcpCheckAsync("Ftp", BackupConfig.FtpHost,                    BackupConfig.FtpPort),
                TcpCheckAsync("Sql", ConfigService.Current.Sql.Host,          22),
                TcpCheckAsync("Mc",  "api.mailchimp.com",                      443)
            );

            SetOpStatus("Connection test complete.", "#588157");
        }

        private async Task TcpCheckAsync(string prefix, string host, int port)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(host)) { SetPing(prefix, "#F38BA8", "Not configured"); return; }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var client = new System.Net.Sockets.TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask && client.Connected)
                {
                    sw.Stop();
                    SetPing(prefix, "#588157", $"{sw.ElapsedMilliseconds} ms");
                }
                else
                {
                    SetPing(prefix, "#F38BA8", "Unreachable");
                }
            }
            catch { SetPing(prefix, "#F38BA8", "Unreachable"); }
        }

        private void SetPing(string prefix, string color, string text)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var dot = this.FindControl<Ellipse>($"Ping{prefix}Dot");
                var txt = this.FindControl<TextBlock>($"Ping{prefix}Text");
                if (dot != null) dot.Fill = Brush.Parse(color);
                if (txt != null) { txt.Text = text; txt.Foreground = Brush.Parse(color); }
            });
        }

        // ── Stats & Reporting ─────────────────────────────────────────────────

        private async Task LoadWeeklyStatsAsync()
        {
            await Task.Run(() =>
            {
                var now    = DateTime.Now;
                var cutoff = now.AddDays(-7);
                int total = 0, success = 0;
                DateTime? lastFailure = null;
                var dayHasActivity = new bool[7];
                var dayHasError    = new bool[7];
                var durations      = new List<double>();
                DateTime? sessionStart = null;

                void ParseLogs(string path)
                {
                    try
                    {
                        var lines = LogService.ImportLatestLogs(path, 500);
                        foreach (var line in lines)
                        {
                            if (!TryParseLogLine(line, out var ts, out var level, out var msg)) continue;
                            if (ts < cutoff) continue;

                            int dayIdx = (int)(now.Date - ts.Date).TotalDays;
                            if (dayIdx >= 0 && dayIdx < 7) { dayHasActivity[dayIdx] = true; if (level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) dayHasError[dayIdx] = true; }

                            if (msg.Contains("SESSION: Starting", StringComparison.OrdinalIgnoreCase)) sessionStart = ts;
                            if ((msg.Contains("SESSION: Finished", StringComparison.OrdinalIgnoreCase) || msg.Contains("completed", StringComparison.OrdinalIgnoreCase) || msg.Contains("SUCCESS:", StringComparison.OrdinalIgnoreCase)) && sessionStart != null)
                            {
                                durations.Add((ts - sessionStart.Value).TotalSeconds);
                                sessionStart = null;
                                total++;
                                if (!level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) success++;
                            }
                            if (level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
                                if (lastFailure == null || ts > lastFailure) lastFailure = ts;
                        }
                    }
                    catch { }
                }

                // Per-service stats
                int ftpOk = 0, ftpTotal = 0, mcOk = 0, mcTotal = 0, sqlOk = 0, sqlTotal = 0;

                void ParseSvc(string path, ref int ok, ref int tot)
                {
                    try
                    {
                        foreach (var line in LogService.ImportLatestLogs(path, 300))
                        {
                            if (!TryParseLogLine(line, out var ts, out var lv, out var msg)) continue;
                            if (ts < cutoff) continue;
                            bool isResult = msg.Contains("completed", StringComparison.OrdinalIgnoreCase)
                                         || msg.Contains("complete", StringComparison.OrdinalIgnoreCase)
                                         || msg.Contains("SUCCESS:", StringComparison.OrdinalIgnoreCase)
                                         || msg.Contains("COMPLETE:", StringComparison.OrdinalIgnoreCase)
                                         || msg.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase);
                            if (isResult) { tot++; if (!lv.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) ok++; }
                        }
                    }
                    catch { }
                }

                ParseSvc(BackupConfig.FtpLogFile, ref ftpOk, ref ftpTotal);
                ParseSvc(BackupConfig.McLogFile,  ref mcOk,  ref mcTotal);
                ParseSvc(BackupConfig.SqlLogFile, ref sqlOk, ref sqlTotal);

                ParseLogs(BackupConfig.FtpLogFile);
                ParseLogs(BackupConfig.McLogFile);
                ParseLogs(BackupConfig.SqlLogFile);

                int streakDays = lastFailure == null ? 7 : Math.Max(0, (int)(now - lastFailure.Value).TotalDays);
                double avgSec  = durations.Count > 0 ? durations.Average() : 0;
                string avgText = avgSec < 60 ? $"{avgSec:F0}s" : $"{avgSec / 60:F1}m";

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var streak = this.FindControl<TextBlock>("TxtStreak");
                    if (streak != null) streak.Text = $"🔥 {streakDays}d";

                    var lastFail = this.FindControl<TextBlock>("TxtLastFailure");
                    if (lastFail != null)
                        lastFail.Text = lastFailure == null ? "No failures recorded" : $"Last failure: {lastFailure.Value:MMM d, HH:mm}";

                    var weekBk = this.FindControl<TextBlock>("TxtWeekBackups");
                    if (weekBk != null) weekBk.Text = total.ToString();

                    var avgTb = this.FindControl<TextBlock>("TxtAvgDuration");
                    if (avgTb != null) avgTb.Text = durations.Count > 0 ? avgText : "—";

                    // Per-service rates
                    var ftpRate = this.FindControl<TextBlock>("StatFtpRate");
                    var mcRate  = this.FindControl<TextBlock>("StatMcRate");
                    var sqlRate = this.FindControl<TextBlock>("StatSqlRate");
                    if (ftpRate != null) ftpRate.Text = ftpTotal > 0 ? $"{ftpOk * 100 / ftpTotal}%" : "—";
                    if (mcRate  != null) mcRate.Text  = mcTotal  > 0 ? $"{mcOk * 100 / mcTotal}%" : "—";
                    if (sqlRate != null) sqlRate.Text = sqlTotal > 0 ? $"{sqlOk * 100 / sqlTotal}%" : "—";

                    // Build 7-day heatmap (index 6=oldest day, 0=today)
                    var heatmap = this.FindControl<Avalonia.Controls.StackPanel>("HeatmapRow");
                    if (heatmap != null)
                    {
                        heatmap.Children.Clear();
                        for (int i = 6; i >= 0; i--)
                        {
                            string color = !dayHasActivity[i] ? "#6C7086" : dayHasError[i] ? "#F38BA8" : "#588157";
                            string label = i == 0 ? "T" : now.AddDays(-i).ToString("ddd")[..1];
                            var col = new Avalonia.Controls.StackPanel { Spacing = 3, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
                            col.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Brush.Parse(color) });
                            col.Children.Add(new TextBlock { Text = label, FontSize = 8, Foreground = Brush.Parse("#6C7086"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
                            heatmap.Children.Add(col);
                        }
                    }
                });
            });
        }

        // ── File Browser & Last Backup Summary ───────────────────────────────

        private async Task LoadLastBackupSummariesAsync()
        {
            await Task.Run(() =>
            {
                string GetSummary(string folder, string logPath)
                {
                    try
                    {
                        if (!Directory.Exists(folder)) return "No local folder";
                        var newest = new DirectoryInfo(folder)
                            .EnumerateFiles("*", SearchOption.AllDirectories)
                            .Where(f => f.Name != "backuplog.txt" && f.Name != "backup_log.txt")
                            .OrderByDescending(f => f.LastWriteTime)
                            .FirstOrDefault();
                        if (newest == null) return "No files found";
                        var ago = DateTime.Now - newest.LastWriteTime;
                        string timeAgo = ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes}m ago"
                                       : ago.TotalHours   < 24 ? $"{(int)ago.TotalHours}h ago"
                                       :                          $"{(int)ago.TotalDays}d ago";
                        string size = newest.Length >= 1073741824 ? $"{newest.Length / 1073741824.0:F1} GB"
                                    : newest.Length >= 1048576     ? $"{newest.Length / 1048576.0:F1} MB"
                                    :                                 $"{newest.Length / 1024.0:F0} KB";
                        int fileCount = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)
                                            .Count(f => f.Name != "backuplog.txt" && f.Name != "backup_log.txt");
                        return $"Last: {timeAgo} · {fileCount} files · newest {size}";
                    }
                    catch { return ""; }
                }

                var ftpSum = GetSummary(BackupConfig.FtpLocalFolder, BackupConfig.FtpLogFile);
                var mcSum  = GetSummary(BackupConfig.MailchimpFolder,  BackupConfig.McLogFile);
                var sqlSum = GetSummary(BackupConfig.SqlLocalFolder,  BackupConfig.SqlLogFile);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var f = this.FindControl<TextBlock>("FtpLastSummary");  if (f != null) f.Text = ftpSum;
                    var m = this.FindControl<TextBlock>("MailchimpLastSummary"); if (m != null) m.Text = mcSum;
                    var s = this.FindControl<TextBlock>("SqlLastSummary"); if (s != null) s.Text = sqlSum;
                });
            });
        }

        private void ToggleFileBrowser(string prefix, string folder)
        {
            var browser = this.FindControl<Avalonia.Controls.Border>($"{prefix}FileBrowser");
            if (browser == null) return;
            browser.IsVisible = !browser.IsVisible;
            if (!browser.IsVisible) return;

            var list = this.FindControl<Avalonia.Controls.StackPanel>($"{prefix}FileList");
            if (list == null) return;
            list.Children.Clear();

            try
            {
                if (!Directory.Exists(folder)) { list.Children.Add(new TextBlock { Text = "Folder not found.", FontSize = 9, Foreground = Brush.Parse("#6C7086") }); return; }
                var files = new DirectoryInfo(folder)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f => f.Name != "backuplog.txt" && f.Name != "backup_log.txt")
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(5)
                    .ToList();
                if (files.Count == 0) { list.Children.Add(new TextBlock { Text = "No backup files found.", FontSize = 9, Foreground = Brush.Parse("#6C7086") }); return; }
                foreach (var file in files)
                {
                    string size = file.Length >= 1048576 ? $"{file.Length / 1048576.0:F1} MB" : $"{file.Length / 1024.0:F0} KB";
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star)));
                    row.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(Avalonia.Controls.GridLength.Auto));
                    var name = new TextBlock { Text = $"📄 {file.Name}", FontSize = 9, Foreground = Brush.Parse("#6C7086"), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                    var sz   = new TextBlock { Text = size, FontSize = 9, Foreground = Brush.Parse("#6C7086"), Margin = new Avalonia.Thickness(6, 0, 0, 0) };
                    Avalonia.Controls.Grid.SetColumn(name, 0); Avalonia.Controls.Grid.SetColumn(sz, 1);
                    row.Children.Add(name); row.Children.Add(sz);
                    list.Children.Add(row);
                }
            }
            catch { list.Children.Add(new TextBlock { Text = "Error reading folder.", FontSize = 9, Foreground = Brush.Parse("#F38BA8") }); }
        }

        private static void OpenFolder(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) { Directory.CreateDirectory(folder); }
                System.Diagnostics.Process.Start("explorer.exe", folder);
            }
            catch { }
        }

        // ── Operations ────────────────────────────────────────────────────────

        private void ToggleMaintenance()
        {
            _maintenancePaused = !_maintenancePaused;
            _manager.IsPaused = _maintenancePaused;
            var btn = this.FindControl<Button>("BtnMaintenanceToggle");
            if (btn != null)
            {
                btn.Content   = _maintenancePaused ? "▶ Resume" : "⏸ Pause All";
                btn.Foreground = _maintenancePaused ? Brush.Parse("#dad7cd") : null;
            }
            var msg = _maintenancePaused ? "Maintenance mode ON — auto-scans paused." : "Maintenance mode OFF — auto-scans resumed.";
            SetOpStatus(msg, _maintenancePaused ? "#dad7cd" : "#588157");
            NotificationService.ShowBackupToast("Maintenance", msg, _maintenancePaused ? "Warning" : "Info");
        }

        private void ExportActivityCsv()
        {
            try
            {
                var lines = new List<string> { "Timestamp,Level,Source,Message" };
                void AddLogs(string logPath)
                {
                    try
                    {
                        foreach (var line in LogService.ImportLatestLogs(logPath, 500))
                        {
                            if (TryParseLogLine(line, out var ts, out var level, out var msg))
                                lines.Add($"\"{ts:yyyy-MM-dd HH:mm:ss}\",\"{level}\",\"{System.IO.Path.GetFileNameWithoutExtension(logPath)}\",\"{msg.Replace("\"", "'")}\"");
                        }
                    }
                    catch { }
                }
                AddLogs(BackupConfig.FtpLogFile);
                AddLogs(BackupConfig.McLogFile);
                AddLogs(BackupConfig.SqlLogFile);

                var exportPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"PinayPal_Activity_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllLines(exportPath, lines);
                NotificationService.ShowBackupToast("Export", $"Saved to Desktop: {System.IO.Path.GetFileName(exportPath)}", "Success");
                SetOpStatus($"CSV exported → {System.IO.Path.GetFileName(exportPath)}", "#588157");
            }
            catch (Exception ex)
            {
                SetOpStatus($"Export failed: {ex.Message}", "#F38BA8");
            }
        }

        private void SetOpStatus(string text, string color)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var tb = this.FindControl<TextBlock>("TxtOperationsStatus");
                if (tb != null) { tb.Text = text; tb.Foreground = Brush.Parse(color); }
            });
        }

        private void StartHealthAutoRefresh()
        {
            _healthRefreshTimer = new System.Timers.Timer(30000); // 30 seconds
            _healthRefreshTimer.Elapsed += async (sender, e) => await LoadHealthDashboardAsync();
            _healthRefreshTimer.AutoReset = true;
            _healthRefreshTimer.Start();
            
            // Show auto-refresh indicator
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var indicator = this.FindControl<Ellipse>("HealthAutoRefreshIndicator");
                var button = this.FindControl<Button>("BtnRefreshHealth");
                if (indicator != null) indicator.IsVisible = true;
                if (button != null) button.Content = "↻ Auto";
            });
            
            LogService.WriteLiveLog("[HEALTH] Auto-refresh started (30s interval)", "", "Information", "SYSTEM");
        }

        private void StartStatsAutoRefresh()
        {
            _statsRefreshTimer = new System.Timers.Timer(45000); // 45 seconds
            _statsRefreshTimer.Elapsed += async (_, _) => await LoadWeeklyStatsAsync();
            _statsRefreshTimer.AutoReset = true;
            _statsRefreshTimer.Start();
            
            // Show auto-refresh indicator
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var indicator = this.FindControl<Ellipse>("StatsAutoRefreshIndicator");
                if (indicator != null) indicator.IsVisible = true;
            });
            
            LogService.WriteLiveLog("[STATS] Auto-refresh started (45s interval)", "", "Information", "SYSTEM");
        }

        private void StartDashboardAutoRefresh()
        {
            _dashboardRefreshTimer = new System.Timers.Timer(30000); // 30 seconds
            _dashboardRefreshTimer.Elapsed += async (_, _) =>
            {
                await UpdateSystemStatusAsync();
                await UpdateQuickStatsAsync();
                await UpdateTimeSinceLastBackupAsync();
                await LoadRecentErrorsAsync();
            };
            _dashboardRefreshTimer.AutoReset = true;
            _dashboardRefreshTimer.Start();
            
            LogService.WriteLiveLog("[DASHBOARD] Auto-refresh started (30s interval)", "", "Information", "SYSTEM");
        }

        private async Task UpdateSystemStatusAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
                    var uptimeText = uptime.TotalHours < 1 ? $"{uptime.TotalMinutes:F0}m" :
                                     uptime.TotalHours < 24 ? $"{uptime.TotalHours:F1}h" :
                                     $"{uptime.TotalDays:F1}d";

                    var lastHealthCheck = "Never";
                    if (File.Exists(AppDataPaths.SystemLogPath))
                    {
                        var logs = LogService.ImportLatestLogs(AppDataPaths.SystemLogPath, 50);
                        var healthCheckLog = logs.FirstOrDefault(l => l.Contains("HEALTH: Global health check completed"));
                        if (healthCheckLog != null)
                        {
                            // Try 12-hour format first: "[2025-04-04 12:34:56 PM]"
                            var match = System.Text.RegularExpressions.Regex.Match(healthCheckLog, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [AP]M)\]");
                            if (!match.Success)
                            {
                                // Fallback to 24-hour format: "[2025-04-04 12:34:56]" (for old logs)
                                match = System.Text.RegularExpressions.Regex.Match(healthCheckLog, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]");
                            }
                            if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var healthTime))
                            {
                                // The log timestamp is in local time, not UTC
                                var timeDiff = DateTime.Now - healthTime;
                                lastHealthCheck = timeDiff.TotalMinutes < 60 ? $"{timeDiff.TotalMinutes:F0}m ago" :
                                                  timeDiff.TotalHours < 24 ? $"{timeDiff.TotalHours:F1}h ago" :
                                                  $"{timeDiff.TotalDays:F1}d ago";
                            }
                        }
                    }

                    var activeProcesses = _manager.IsPaused ? "Paused" : $"{_activeOperations} active";

                    // Calculate available disk space
                    long freeSpace = 0;
                    try
                    {
                        // Get the drive where the first backup folder is located
                        string backupPath = BackupConfig.FtpLocalFolder;
                        if (string.IsNullOrEmpty(backupPath) || !System.IO.Path.IsPathRooted(backupPath))
                        {
                            backupPath = BackupConfig.MailchimpFolder;
                        }
                        if (string.IsNullOrEmpty(backupPath) || !System.IO.Path.IsPathRooted(backupPath))
                        {
                            backupPath = BackupConfig.SqlLocalFolder;
                        }
                        
                        if (!string.IsNullOrEmpty(backupPath))
                        {
                            var driveRoot = System.IO.Path.GetPathRoot(backupPath);
                            if (!string.IsNullOrEmpty(driveRoot))
                            {
                                var driveInfo = new DriveInfo(driveRoot);
                                freeSpace = driveInfo.AvailableFreeSpace;
                            }
                        }
                    }
                    catch { }

                    string storageText = freeSpace >= 1073741824 ? $"{freeSpace / 1073741824.0:F1} GB free" :
                                     freeSpace >= 1048576 ? $"{freeSpace / 1048576.0:F1} MB free" :
                                     freeSpace >= 1024 ? $"{freeSpace / 1024.0:F0} KB free" : "0 B free";

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var uptimeTextBlock = this.FindControl<TextBlock>("SystemUptime");
                        var healthCheckTextBlock = this.FindControl<TextBlock>("LastHealthCheck");
                        var processesTextBlock = this.FindControl<TextBlock>("ActiveProcesses");
                        var storageTextBlock = this.FindControl<TextBlock>("StorageUsage");

                        if (uptimeTextBlock != null) uptimeTextBlock.Text = uptimeText;
                        if (healthCheckTextBlock != null) healthCheckTextBlock.Text = lastHealthCheck;
                        if (processesTextBlock != null) processesTextBlock.Text = activeProcesses;
                        if (storageTextBlock != null) storageTextBlock.Text = storageText;
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[SYSTEM] Error updating system status: {ex.Message}", "", "Error", "SYSTEM");
                }
            });
        }

        private async Task UpdateQuickStatsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var backupsToday = 0;
                    var failedBackups = 0;
                    var successRate = 100.0;

                    var ftpLogs = LogService.ImportLatestLogs(BackupConfig.FtpLogFile, 100);
                    var mcLogs = LogService.ImportLatestLogs(BackupConfig.McLogFile, 100);
                    var sqlLogs = LogService.ImportLatestLogs(BackupConfig.SqlLogFile, 100);

                    var today = DateTime.Now.ToString("yyyy-MM-dd");
                    var allLogs = ftpLogs.Concat(mcLogs).Concat(sqlLogs);
                    
                    foreach (var log in allLogs.Where(l => l.Contains(today)))
                    {
                        if (log.Contains("COMPLETE") || log.Contains("SUCCESS"))
                            backupsToday++;
                        if (log.Contains("ERROR") || log.Contains("FAILED"))
                            failedBackups++;
                    }

                    if (backupsToday > 0)
                    {
                        successRate = ((double)(backupsToday - failedBackups) / backupsToday) * 100;
                    }
                    else if (failedBackups == 0)
                    {
                        successRate = 100.0; // No backups today but no failures either
                    }

                    // Calculate storage used
                    long totalBytes = 0;
                    try
                    {
                        if (Directory.Exists(BackupConfig.FtpLocalFolder))
                        {
                            totalBytes += new DirectoryInfo(BackupConfig.FtpLocalFolder)
                                .EnumerateFiles("*", SearchOption.AllDirectories)
                                .Sum(f => f.Length);
                        }
                        if (Directory.Exists(BackupConfig.MailchimpFolder))
                        {
                            totalBytes += new DirectoryInfo(BackupConfig.MailchimpFolder)
                                .EnumerateFiles("*", SearchOption.AllDirectories)
                                .Sum(f => f.Length);
                        }
                        if (Directory.Exists(BackupConfig.SqlLocalFolder))
                        {
                            totalBytes += new DirectoryInfo(BackupConfig.SqlLocalFolder)
                                .EnumerateFiles("*", SearchOption.AllDirectories)
                                .Sum(f => f.Length);
                        }
                    }
                    catch { }

                    string storageUsed = totalBytes >= 1073741824 ? $"{totalBytes / 1073741824.0:F1} GB" :
                                     totalBytes >= 1048576 ? $"{totalBytes / 1048576.0:F1} MB" :
                                     totalBytes >= 1024 ? $"{totalBytes / 1024.0:F0} KB" : "0 B";

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var backupsTodayText = this.FindControl<TextBlock>("StatBackupsToday");
                        var successRateText = this.FindControl<TextBlock>("StatSuccessRate");
                        var failedBackupsText = this.FindControl<TextBlock>("StatFailedBackups");
                        var storageUsedText = this.FindControl<TextBlock>("StatStorageUsed");

                        if (backupsTodayText != null) backupsTodayText.Text = backupsToday.ToString();
                        if (successRateText != null) successRateText.Text = $"{successRate:F0}%";
                        if (failedBackupsText != null) failedBackupsText.Text = failedBackups.ToString();
                        if (storageUsedText != null) storageUsedText.Text = storageUsed;
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[SYSTEM] Error updating quick stats: {ex.Message}", "", "Error", "SYSTEM");
                }
            });
        }

        private async Task UpdateTimeSinceLastBackupAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var ftpLogs = LogService.ImportLatestLogs(BackupConfig.FtpLogFile, 50);
                    var mcLogs = LogService.ImportLatestLogs(BackupConfig.McLogFile, 50);
                    var sqlLogs = LogService.ImportLatestLogs(BackupConfig.SqlLogFile, 50);

                    var ftpLastTime = GetLastBackupTime(ftpLogs);
                    var mcLastTime = GetLastBackupTime(mcLogs);
                    var sqlLastTime = GetLastBackupTime(sqlLogs);

                    var ftpTimeText = GetTimeAgoText(ftpLastTime);
                    var mcTimeText = GetTimeAgoText(mcLastTime);
                    var sqlTimeText = GetTimeAgoText(sqlLastTime);

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var ftpTimeTextBlock = this.FindControl<TextBlock>("TimeSinceFtp");
                        var mcTimeTextBlock = this.FindControl<TextBlock>("TimeSinceMc");
                        var sqlTimeTextBlock = this.FindControl<TextBlock>("TimeSinceSql");

                        if (ftpTimeTextBlock != null)
                        {
                            ftpTimeTextBlock.Text = ftpTimeText;
                            ftpTimeTextBlock.Foreground = GetTimeAgoColor(ftpLastTime);
                        }
                        if (mcTimeTextBlock != null)
                        {
                            mcTimeTextBlock.Text = mcTimeText;
                            mcTimeTextBlock.Foreground = GetTimeAgoColor(mcLastTime);
                        }
                        if (sqlTimeTextBlock != null)
                        {
                            sqlTimeTextBlock.Text = sqlTimeText;
                            sqlTimeTextBlock.Foreground = GetTimeAgoColor(sqlLastTime);
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[SYSTEM] Error updating time since last backup: {ex.Message}", "", "Error", "SYSTEM");
                }
            });
        }

        private DateTime? GetLastBackupTime(List<string> logs)
        {
            foreach (var log in logs)
            {
                // Case-insensitive check for completion keywords
                var logUpper = log.ToUpperInvariant();
                if (logUpper.Contains("COMPLETE") || logUpper.Contains("COMPLETE:") || 
                    logUpper.Contains("SUCCESS") || logUpper.Contains("SUCCESS:") || 
                    logUpper.Contains("DOWNLOAD COMPLETE"))
                {
                    // Try 12-hour format first: "[2025-04-04 12:34:56 PM]"
                    var match = System.Text.RegularExpressions.Regex.Match(log, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [AP]M)\]");
                    if (!match.Success)
                    {
                        // Fallback to 24-hour format: "[2025-04-04 12:34:56]" (for old logs)
                        match = System.Text.RegularExpressions.Regex.Match(log, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]");
                    }
                    if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var time))
                    {
                        // The log timestamp is in local time, not UTC
                        return time;
                    }
                }
            }
            return null;
        }

        private static DateTime GetManilaNow() => DateTime.UtcNow.AddHours(8);

        private static DateTime ToManilaTime(DateTime localTime)
        {
            // Log timestamps are in local time (UTC-7 from GetTzDate)
            // Convert to UTC first, then to Manila (UTC+8)
            // UTC-7 to UTC = +7, UTC to UTC+8 = +8, total = +15
            return localTime.AddHours(15);
        }

        private string GetTimeAgoText(DateTime? time)
        {
            if (!time.HasValue) return "Never";
            
            var manilaNow = GetManilaNow();
            var manilaBackupTime = ToManilaTime(time.Value);
            
            // Check if same day in Manila time
            if (manilaBackupTime.Date == manilaNow.Date)
                return "Today";
            
            // Check if yesterday
            if (manilaBackupTime.Date == manilaNow.Date.AddDays(-1))
                return "Yesterday";
            
            var diff = manilaNow - manilaBackupTime;
            if (diff.TotalHours < 24) return $"{diff.TotalHours:F0}h ago";
            if (diff.TotalDays < 7) return $"{diff.TotalDays:F0}d ago";
            return $"{diff.TotalDays / 7:F0}w ago";
        }

        private IBrush GetTimeAgoColor(DateTime? time)
        {
            if (!time.HasValue) return Brush.Parse("#6C7086");
            
            var manilaNow = GetManilaNow();
            var manilaBackupTime = ToManilaTime(time.Value);
            
            // Green for today, warning for yesterday, red for older
            if (manilaBackupTime.Date == manilaNow.Date)
                return Brush.Parse("#588157"); // Green - today
            if (manilaBackupTime.Date == manilaNow.Date.AddDays(-1))
                return Brush.Parse("#dad7cd"); // Light gray - yesterday
            return Brush.Parse("#F38BA8"); // Red - older
        }

        private void UpdateServicesStatusSummary(Dictionary<string, int> serviceScores)
        {
            if (serviceScores == null)
            {
                // Don't set default values - leave as "SCANNING..." until actual data arrives
                return;
            }

            int healthyCount = 0;
            
            // Get last backup times to check freshness
            var ftpLogs = LogService.ImportLatestLogs(BackupConfig.FtpLogFile, 50);
            var mcLogs = LogService.ImportLatestLogs(BackupConfig.McLogFile, 50);
            var sqlLogs = LogService.ImportLatestLogs(BackupConfig.SqlLogFile, 50);
            
            var ftpLastTime = GetLastBackupTime(ftpLogs);
            var mcLastTime = GetLastBackupTime(mcLogs);
            var sqlLastTime = GetLastBackupTime(sqlLogs);
            
            // FTP - check both health score AND freshness
            int ftpScore = serviceScores.GetValueOrDefault("FTP", 0);
            bool ftpIsStale = IsBackupStale(ftpLastTime);
            string ftpStatus = ftpIsStale ? "Outdated" : ftpScore >= 80 ? "Healthy" : ftpScore >= 50 ? "Warning" : ftpScore > 0 ? "Critical" : "No Data";
            string ftpColor = ftpIsStale ? "#e6c55c" : ftpScore >= 80 ? "#588157" : ftpScore >= 50 ? "#dad7cd" : ftpScore > 0 ? "#F38BA8" : "#6C7086";
            Set("FtpStatusText", ftpStatus);
            SetDot("FtpStatusDot", ftpColor);
            if (ftpScore >= 80 && !ftpIsStale) healthyCount++;

            // Mailchimp - check both health score AND freshness
            int mcScore = serviceScores.GetValueOrDefault("Mailchimp", 0);
            bool mcIsStale = IsBackupStale(mcLastTime);
            string mcStatus = mcIsStale ? "Outdated" : mcScore >= 80 ? "Healthy" : mcScore >= 50 ? "Warning" : mcScore > 0 ? "Critical" : "No Data";
            string mcColor = mcIsStale ? "#e6c55c" : mcScore >= 80 ? "#00b4d8" : mcScore >= 50 ? "#caf0f8" : mcScore > 0 ? "#F38BA8" : "#6C7086";
            Set("MailchimpStatusText", mcStatus);
            SetDot("MailchimpStatusDot", mcColor);
            if (mcScore >= 80 && !mcIsStale) healthyCount++;

            // SQL - check both health score AND freshness
            int sqlScore = serviceScores.GetValueOrDefault("SQL", 0);
            bool sqlIsStale = IsBackupStale(sqlLastTime);
            string sqlStatus = sqlIsStale ? "Outdated" : sqlScore >= 80 ? "Healthy" : sqlScore >= 50 ? "Warning" : sqlScore > 0 ? "Critical" : "No Data";
            string sqlColor = sqlIsStale ? "#e6c55c" : sqlScore >= 80 ? "#fad643" : sqlScore >= 50 ? "#ffe169" : sqlScore > 0 ? "#F38BA8" : "#6C7086";
            Set("SqlStatusText", sqlStatus);
            SetDot("SqlStatusDot", sqlColor);
            if (sqlScore >= 80 && !sqlIsStale) healthyCount++;

            // Update services OK text
            Set("ServicesHealthText", $"{healthyCount}/3 healthy");
            Set("StatServicesOk", healthyCount.ToString());
        }

        private bool IsBackupStale(DateTime? lastBackupTime, double thresholdHours = 48)
        {
            if (!lastBackupTime.HasValue) return true;
            var manilaNow = GetManilaNow();
            var manilaBackupTime = ToManilaTime(lastBackupTime.Value);
            var diff = manilaNow - manilaBackupTime;
            return diff.TotalHours > thresholdHours;
        }

        private void SetDot(string controlName, string color)
        {
            var dot = this.FindControl<Ellipse>(controlName);
            if (dot != null) dot.Fill = Brush.Parse(color);
        }

        private async Task LoadRecentErrorsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var errors = new List<(string service, string error, DateTime time)>();

                    var ftpLogs = LogService.ImportLatestLogs(BackupConfig.FtpLogFile, 100);
                    var mcLogs = LogService.ImportLatestLogs(BackupConfig.McLogFile, 100);
                    var sqlLogs = LogService.ImportLatestLogs(BackupConfig.SqlLogFile, 100);

                    AddErrorsFromLogs(ftpLogs, "FTP", errors);
                    AddErrorsFromLogs(mcLogs, "Mailchimp", errors);
                    AddErrorsFromLogs(sqlLogs, "SQL", errors);

                    var recentErrors = errors.OrderByDescending(e => e.time).Take(5).ToList();

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var errorsPanel = this.FindControl<Border>("RecentErrorsPanel");
                        var errorsList = this.FindControl<StackPanel>("RecentErrorsList");

                        if (errorsPanel != null && errorsList != null)
                        {
                            errorsPanel.IsVisible = recentErrors.Count > 0;
                            errorsList.Children.Clear();

                            foreach (var error in recentErrors)
                            {
                                var errorBorder = new Border
                                {
                                    Background = Brush.Parse("#3D2020"),
                                    CornerRadius = new Avalonia.CornerRadius(6),
                                    Padding = new Avalonia.Thickness(10, 6)
                                };

                                var errorGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };
                                
                                var serviceIcon = new TextBlock
                                {
                                    Text = error.service switch
                                    {
                                        "FTP" => "FTP",
                                        "Mailchimp" => "MC",
                                        "SQL" => "SQL",
                                        _ => "ERR"
                                    },
                                    FontSize = 12,
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                    Margin = new Avalonia.Thickness(0, 0, 8, 0)
                                };

                                var errorText = new TextBlock
                                {
                                    Text = error.error.Length > 50 ? error.error.Substring(0, 50) + "..." : error.error,
                                    FontSize = 10,
                                    Foreground = Brush.Parse("#F38BA8"),
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                                };

                                var timeText = new TextBlock
                                {
                                    Text = GetTimeAgoText(error.time),
                                    FontSize = 9,
                                    Foreground = Brush.Parse("#6C7086"),
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                                };

                                Grid.SetColumn(serviceIcon, 0);
                                Grid.SetColumn(errorText, 1);
                                Grid.SetColumn(timeText, 2);

                                errorGrid.Children.Add(serviceIcon);
                                errorGrid.Children.Add(errorText);
                                errorGrid.Children.Add(timeText);

                                errorBorder.Child = errorGrid;
                                errorsList.Children.Add(errorBorder);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[SYSTEM] Error loading recent errors: {ex.Message}", "", "Error", "SYSTEM");
                }
            });
        }

        private void AddErrorsFromLogs(List<string> logs, string service, List<(string service, string error, DateTime time)> errors)
        {
            foreach (var log in logs)
            {
                if (log.Contains("ERROR") || log.Contains("FAILED"))
                {
                    // Try 12-hour format first: "[2025-04-04 12:34:56 PM]"
                    var match = System.Text.RegularExpressions.Regex.Match(log, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [AP]M)\]");
                    if (!match.Success)
                    {
                        // Fallback to 24-hour format: "[2025-04-04 12:34:56]" (for old logs)
                        match = System.Text.RegularExpressions.Regex.Match(log, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]");
                    }
                    if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var time))
                    {
                        var errorText = log.Split(new[] { "] " }, StringSplitOptions.None).LastOrDefault() ?? "Unknown error";
                        errors.Add((service, errorText, time));
                    }
                }
            }
        }

        private void ClearRecentErrors()
        {
            var errorsPanel = this.FindControl<Border>("RecentErrorsPanel");
            var errorsList = this.FindControl<StackPanel>("RecentErrorsList");

            if (errorsPanel != null && errorsList != null)
            {
                errorsPanel.IsVisible = false;
                errorsList.Children.Clear();
                NotificationService.ShowBackupToast("Recent Errors", "Errors cleared from dashboard.", "Success");
            }
        }

        private void ShowDashboardCustomization()
        {
            var dialog = new DashboardCustomizationDialog();
            
            Window? dialogWindow = null;
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            
            dialogWindow = new Window
            {
                Title = "Customize Dashboard",
                Width = 500,
                Height = 600,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = Avalonia.Media.Brush.Parse("#1E1E2E"),
                Content = dialog
            };
            
            dialog.OnApply += (settings) =>
            {
                ApplyDashboardCustomization(settings);
                dialogWindow?.Close();
            };
            
            // Minimize dialog when parent is minimized
            if (parentWindow != null)
            {
                parentWindow.PropertyChanged += (_, e) =>
                {
                    if (e.Property == Window.WindowStateProperty)
                    {
                        if (parentWindow.WindowState == WindowState.Minimized)
                        {
                            dialogWindow.WindowState = WindowState.Minimized;
                        }
                        else if (dialogWindow.WindowState == WindowState.Minimized)
                        {
                            dialogWindow.WindowState = WindowState.Normal;
                        }
                    }
                };
                
                dialogWindow.ShowDialog(parentWindow);
            }
        }

        private void ApplyDashboardCustomization(DashboardCustomization settings)
        {
            // Apply visibility settings
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Apply visibility to named controls
                var alertBanner = this.FindControl<Border>("AlertBanner");
                if (alertBanner != null) alertBanner.IsVisible = settings.ShowSystemStatus;

                var quickStats = this.FindControl<Grid>("QuickStatsGrid");
                if (quickStats != null) quickStats.IsVisible = settings.ShowQuickStats;

                var recentErrors = this.FindControl<Border>("RecentErrorsPanel");
                if (recentErrors != null) recentErrors.IsVisible = settings.ShowRecentErrors;

                var serviceCards = this.FindControl<Grid>("ServiceCardsSection");
                if (serviceCards != null) serviceCards.IsVisible = settings.ShowServiceCards;

                var criticalAlerts = this.FindControl<Border>("CriticalAlertsPanel");
                if (criticalAlerts != null) criticalAlerts.IsVisible = settings.ShowHealthDashboard;
                
                // Apply compact mode
                _compactMode = settings.CompactMode;
                ApplyCompactMode(_compactMode);
                
                // Update compact button text
                var btnCompact = this.FindControl<Button>("BtnCompactToggle");
                if (btnCompact != null)
                {
                    btnCompact.Content = _compactMode ? "⊞ Expand" : "⊟ Compact";
                    btnCompact.Foreground = _compactMode ? Brush.Parse("#588157") : Brushes.Gray;
                }
                
                NotificationService.ShowBackupToast("Dashboard", "Customization applied.", "Success");
            });
        }

        private void ApplyCompactMode(bool compact)
        {
            // Toggle compact mode by adjusting margins, spacing, and font sizes
            // Find the main StackPanel inside the ScrollViewer (first child is ScrollViewer)
            var scrollViewer = this.Content as ScrollViewer;
            var mainStackPanel = scrollViewer?.Content as StackPanel;
            if (mainStackPanel != null)
            {
                mainStackPanel.Spacing = compact ? 8 : 16;
                mainStackPanel.Margin = compact ? new Avalonia.Thickness(12, 12, 16, 16) : new Avalonia.Thickness(16, 16, 24, 24);

                // Find all Border elements (cards) in the main panel
                foreach (var border in mainStackPanel.Children.OfType<Border>())
                {
                    if (border.Padding is Avalonia.Thickness padding)
                    {
                        border.Padding = compact ? new Avalonia.Thickness(12) : new Avalonia.Thickness(16);
                    }
                }
            }
        }

        private async Task LoadSystemLogsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var logs = LogService.ImportLatestLogs(AppDataPaths.SystemLogPath, 100);
                    var logText = string.Join("\n", logs);

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var systemLogsText = this.FindControl<TextBlock>("SystemLogsText");
                        if (systemLogsText != null)
                        {
                            systemLogsText.Text = logs.Count > 0 ? logText : "No system logs available.";
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[SYSTEM] Error loading system logs: {ex.Message}", "", "Error", "SYSTEM");
                }
            });
        }

        private async Task LoadFirebaseLogsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var allLogs = LogService.ImportLatestLogs(AppDataPaths.SystemLogPath, 200);
                    var firebaseLogs = allLogs
                        .Where(l => l.Contains("FIREBASE") || l.Contains("CONFIG") || l.Contains("QUICK_ACTIONS"))
                        .Take(50)
                        .ToList();

                    var logText = firebaseLogs.Count > 0 ? string.Join("\n", firebaseLogs) : "No Firebase-related logs available.";

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var firebaseLogsText = this.FindControl<TextBlock>("FirebaseLogsText");
                        if (firebaseLogsText != null)
                        {
                            firebaseLogsText.Text = logText;
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[SYSTEM] Error loading Firebase logs: {ex.Message}", "", "Error", "SYSTEM");
                }
            });
        }

        private void ClearSystemLogs()
        {
            try
            {
                LogService.ClearLogs(AppDataPaths.SystemLogPath);
                _ = LoadSystemLogsAsync();
                NotificationService.ShowBackupToast("System Logs", "System logs cleared successfully.", "Success");
            }
            catch (Exception ex)
            {
                NotificationService.ShowBackupToast("System Logs", $"Failed to clear logs: {ex.Message}", "Error");
            }
        }

        private void ViewLogsInNotepad()
        {
            try
            {
                var logPath = AppDataPaths.SystemLogPath;
                if (File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "notepad.exe",
                        Arguments = logPath,
                        UseShellExecute = true
                    });
                    NotificationService.ShowBackupToast("System Logs", "Opening logs in Notepad...", "Info");
                }
                else
                {
                    NotificationService.ShowBackupToast("System Logs", "Log file not found.", "Error");
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowBackupToast("System Logs", $"Failed to open logs: {ex.Message}", "Error");
            }
        }

        private void OnNewSystemLogEntry(string logEntry, string logFile)
        {
            // Only update if it's a system log
            if (logFile == AppDataPaths.SystemLogPath)
            {
                // Check if log is Firebase-related
                bool isFirebaseLog = logEntry.Contains("FIREBASE") || logEntry.Contains("CONFIG") || logEntry.Contains("QUICK_ACTIONS");

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (isFirebaseLog)
                    {
                        // Update Firebase logs display
                        var firebaseLogsText = this.FindControl<TextBlock>("FirebaseLogsText");
                        if (firebaseLogsText != null)
                        {
                            var currentText = firebaseLogsText.Text;
                            var newText = $"{logEntry}\n{currentText}";
                            // Keep only last 50 lines to prevent memory issues
                            var lines = newText.Split('\n').Take(50);
                            firebaseLogsText.Text = string.Join("\n", lines);
                        }
                    }
                    else
                    {
                        // Update system logs display
                        var systemLogsText = this.FindControl<TextBlock>("SystemLogsText");
                        if (systemLogsText != null)
                        {
                            var currentText = systemLogsText.Text;
                            var newText = $"{logEntry}\n{currentText}";
                            // Keep only last 100 lines to prevent memory issues
                            var lines = newText.Split('\n').Take(100);
                            systemLogsText.Text = string.Join("\n", lines);
                        }
                    }
                });
            }
        }

        private void OnScheduleChangedFromFirebase()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateDailySchedule();
                UpdateSchedSummary();
                LogService.WriteSystemLog("[HOMECTRL] UI refreshed after Firebase schedule change", "Information", "SYSTEM");
            });
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            
            // Unsubscribe from log events
            LogService.OnNewLogEntry -= OnNewSystemLogEntry;
            ConfigService.OnScheduleChanged -= OnScheduleChangedFromFirebase;
            
            // Hide auto-refresh indicators
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var healthIndicator = this.FindControl<Ellipse>("HealthAutoRefreshIndicator");
                var statsIndicator = this.FindControl<Ellipse>("StatsAutoRefreshIndicator");
                var button = this.FindControl<Button>("BtnRefreshHealth");
                if (healthIndicator != null) healthIndicator.IsVisible = false;
                if (statsIndicator != null) statsIndicator.IsVisible = false;
                if (button != null) button.Content = "↻";
            });
            
            // Stop all timers
            _healthRefreshTimer?.Stop();
            _healthRefreshTimer?.Dispose();
            _statsRefreshTimer?.Stop();
            _statsRefreshTimer?.Dispose();
            _dashboardRefreshTimer?.Stop();
            _dashboardRefreshTimer?.Dispose();
            _autoPingTimer?.Stop();
            _autoPingTimer?.Dispose();
            _statsTimer?.Stop();
            _statsTimer?.Dispose();
            _scheduleTimer?.Stop();
            _scheduleTimer?.Dispose();
            _storageTimer?.Stop();
            _storageTimer?.Dispose();
        }
    }
}
