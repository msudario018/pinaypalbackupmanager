using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
        private System.Timers.Timer? _healthRefreshTimer;
        private bool _compactMode = false;
        private int _activeOperations = 0;
        private System.Timers.Timer? _activeProcessUpdateTimer;
        private System.Timers.Timer? _statsRefreshTimer;
        private System.Timers.Timer? _dashboardRefreshTimer;
        private int _isHealthRefreshing = 0;
        private int _isStatsRefreshing = 0;
        private int _isDashboardRefreshing = 0;
        private DateTime _lastStorageStatsRefresh = DateTime.MinValue;
        private string _cachedStorageUsed = "0 B";
        private System.Timers.Timer? _errorRefreshTimer;

        // Cached UI controls to avoid repeated FindControl visual-tree walks in timer callbacks
        private TextBlock? _cachedDashHealthText;
        private Ellipse? _cachedDashHealthDotEllipse;
        private Border? _cachedAlertBanner;
        private TextBlock? _cachedAlertText;
        private TextBlock? _cachedStatServicesOk;
        private TextBlock? _cachedSystemUptime;
        private TextBlock? _cachedLastHealthCheck;
        private TextBlock? _cachedActiveProcesses;
        private TextBlock? _cachedStorageUsage;
        private TextBlock? _cachedStatBackupsToday;
        private TextBlock? _cachedStatSuccessRate;
        private TextBlock? _cachedStatFailedBackups;
        private TextBlock? _cachedStatStorageUsed;
        private TextBlock? _cachedTrendBackups;
        private TextBlock? _cachedTrendSuccessRate;
        private TextBlock? _cachedTimeSinceFtp;
        private TextBlock? _cachedTimeSinceMc;
        private TextBlock? _cachedTimeSinceSql;
        private Border? _cachedRetryQueueBadge;
        private TextBlock? _cachedTxtRetryQueue;
        private TextBlock? _cachedHealthScoreText;
        private TextBlock? _cachedHealthTrendText;
        private TextBlock? _cachedHealthFtpScore;
        private TextBlock? _cachedHealthMcScore;
        private TextBlock? _cachedHealthSqlScore;
        private TextBlock? _cachedCriticalAlertsCount;
        private TextBlock? _cachedTimeSinceHealth;
        private ProgressBar? _cachedGlobalBackupProgress;
        private TextBlock? _cachedBackupProgressText;
        private TextBlock? _cachedBackupProgressPercent;
        private Ellipse? _cachedGbpServiceDot;
        private TextBlock? _cachedGbpServiceName;
        private StackPanel? _cachedMirrorProgressSection;
        private ProgressBar? _cachedMirrorProgressBar;
        private TextBlock? _cachedMirrorProgressText;
        private TextBlock? _cachedMirrorProgressPercent;
        private TextBlock? _cachedMirrorServiceName;
        private TextBlock? _cachedMirrorStatusDetail;

        public event Action? OnNavigateFtp;
        public event Action? OnNavigateMailchimp;
        public event Action? OnNavigateSql;
        public event Action? OnNavigateBackupHistory;
        public event Action? OnRunAllChecks;
        public event Action? OnRunAllBackupsParallel;
        public event Action? OnEmergencyStop;
        public event Action? OnFtpSyncCheck;
        public event Action? OnFtpQuickBackup;
        public event Action? OnMailchimpSyncCheck;
        public event Action? OnMailchimpQuickBackup;
        public event Action? OnSqlSyncCheck;
        public event Action? OnSqlQuickBackup;

        private bool _autoPinged;
        private bool _maintenancePaused;
        private DateTime _lastActivityRefresh = DateTime.MinValue;

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
            InitializeCachedControls();

            _manager.OnAutoScanTimersReset += OnAutoScanTimersReset;
            _manager.OnDailyScheduleUpdated += OnDailyScheduleUpdated;

            _manager.OnHealthUpdate += OnHealthUpdate;
            _manager.OnTimeUpdate += OnTimeUpdate;
            _manager.OnBackupProgress += OnBackupProgress;
            NetworkDriveService.OnMirrorProgress += OnMirrorProgressUpdate;

            StartActiveProcessTimer();

            this.FindControl<Button>("BtnGoFtp")!.Click += (_, _) => OnNavigateFtp?.Invoke();
            this.FindControl<Button>("BtnGoMailchimp")!.Click += (_, _) => OnNavigateMailchimp?.Invoke();
            this.FindControl<Button>("BtnGoSql")!.Click += (_, _) => OnNavigateSql?.Invoke();
            var btnHeatmapMore = this.FindControl<Button>("BtnHeatmapMore");
            if (btnHeatmapMore != null)
                btnHeatmapMore.Click += (_, _) => OnNavigateBackupHistory?.Invoke();
            
            // Run All - triggers parallel backup
            this.FindControl<Button>("BtnRunAllChecks")!.Click += (_, _) => OnRunAllBackupsParallel?.Invoke();
            
            this.FindControl<Button>("BtnViewAllBackups")!.Click += (_, _) => OpenFolder(BackupConfig.FtpLocalFolder);

            // Quick action buttons
            this.FindControl<Button>("BtnFtpSyncCheck")!.Click += (_, _) => OnFtpSyncCheck?.Invoke();
            this.FindControl<Button>("BtnFtpQuickBackup")!.Click += (_, _) => OnFtpQuickBackup?.Invoke();
            this.FindControl<Button>("BtnMcSyncCheck")!.Click += (_, _) => OnMailchimpSyncCheck?.Invoke();
            this.FindControl<Button>("BtnMcQuickBackup")!.Click += (_, _) => OnMailchimpQuickBackup?.Invoke();
            this.FindControl<Button>("BtnSqlSyncCheck")!.Click += (_, _) => OnSqlSyncCheck?.Invoke();
            this.FindControl<Button>("BtnSqlQuickBackup")!.Click += (_, _) => OnSqlQuickBackup?.Invoke();

            // Network Drive card
            this.FindControl<Button>("BtnOpenNetworkDrive")!.Click += (_, _) => OpenNetworkDriveFolder();
            this.FindControl<Button>("BtnNdMirrorAll")!.Click += async (_, _) => await MirrorAllToNetworkDriveAsync();

            // Per-service mirror buttons
            this.FindControl<Button>("BtnFtpMirror")!.Click += async (_, _) => await MirrorServiceAsync("FTP", BackupConfig.FtpLocalFolder);
            this.FindControl<Button>("BtnMcMirror")!.Click += async (_, _) => await MirrorServiceAsync("Mailchimp", BackupConfig.MailchimpFolder);
            this.FindControl<Button>("BtnSqlMirror")!.Click += async (_, _) => await MirrorServiceAsync("SQL", BackupConfig.SqlLocalFolder);

            this.FindControl<Button>("BtnPingAll")!.Click += async (_, _) => await PingAllAsync();
            this.FindControl<Button>("BtnOpenSchedule")!.Click += async (_, _) => await OpenScheduleDialogAsync();
            this.FindControl<Button>("BtnBackupAll")!.Click += async (_, _) => OnRunAllBackupsParallel?.Invoke();
            this.FindControl<Button>("BtnTestAllConn")!.Click += async (_, _) => await PingAllAsync();
            this.FindControl<Button>("BtnRetryFailed")!.Click += (_, _) => { SetOpStatus("Retrying all services...", "#dad7cd"); OnRunAllChecks?.Invoke(); SetOpStatus("Retry triggered. Check service tabs for results.", "#588157"); };
            this.FindControl<Button>("BtnEmergencyStop")!.Click += (_, _) => { OnEmergencyStop?.Invoke(); SetOpStatus("Emergency stop sent to all services.", "#F38BA8"); };
            this.FindControl<Button>("BtnMaintenanceToggle")!.Click += (_, _) => ToggleMaintenance();
            this.FindControl<Button>("BtnCustomizeDashboard")!.Click += (_, _) => ShowDashboardCustomization();
            this.FindControl<Button>("BtnClearErrors")!.Click += (_, _) => ClearRecentErrors();
            this.FindControl<Button>("BtnExportCsv")!.Click += (_, _) => ExportActivityCsv();

            this.FindControl<Button>("BtnRefreshHealth")!.Click += (_, _) => _ = LoadHealthDashboardAsync();
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

            // Update greeting when user changes
            AuthService.OnUserChanged += (_) => UpdateGreeting();

            // Load system logs
            FireAndForget(LoadSystemLogsAsync(), nameof(LoadSystemLogsAsync));

            // Load Firebase logs
            FireAndForget(LoadFirebaseLogsAsync(), nameof(LoadFirebaseLogsAsync));

            // Initialize new dashboard features
            FireAndForget(UpdateSystemStatusAsync(), nameof(UpdateSystemStatusAsync));
            FireAndForget(UpdateQuickStatsAsync(), nameof(UpdateQuickStatsAsync));
            FireAndForget(UpdateTimeSinceLastBackupAsync(), nameof(UpdateTimeSinceLastBackupAsync));
            FireAndForget(LoadRecentErrorsAsync(), nameof(LoadRecentErrorsAsync));
            UpdateActivityHeatmap();
            
            // Initialize service status immediately
            UpdateServicesStatusSummary(null);
            
            // Start dashboard auto-refresh (every 30 seconds)
            StartDashboardAutoRefresh();

            // Start error log refresh (every 60 seconds — less expensive than full dashboard)
            StartErrorRefreshTimer();
        }

        private void InitializeCachedControls()
        {
            _cachedDashHealthDotEllipse = this.FindControl<Ellipse>("DashHealthDot");
            _cachedDashHealthText = this.FindControl<TextBlock>("DashHealthText");
            _cachedAlertBanner = this.FindControl<Border>("AlertBanner");
            _cachedAlertText = this.FindControl<TextBlock>("AlertText");
            _cachedStatServicesOk = this.FindControl<TextBlock>("StatServicesOk");
            _cachedSystemUptime = this.FindControl<TextBlock>("SystemUptime");
            _cachedLastHealthCheck = this.FindControl<TextBlock>("LastHealthCheck");
            _cachedActiveProcesses = this.FindControl<TextBlock>("ActiveProcesses");
            _cachedStorageUsage = this.FindControl<TextBlock>("StorageUsage");
            _cachedStatBackupsToday = this.FindControl<TextBlock>("StatBackupsToday");
            _cachedStatSuccessRate = this.FindControl<TextBlock>("StatSuccessRate");
            _cachedStatFailedBackups = this.FindControl<TextBlock>("StatFailedBackups");
            _cachedStatStorageUsed = this.FindControl<TextBlock>("StatStorageUsed");
            _cachedTrendBackups = this.FindControl<TextBlock>("TrendBackups");
            _cachedTrendSuccessRate = this.FindControl<TextBlock>("TrendSuccessRate");
            _cachedTimeSinceFtp = this.FindControl<TextBlock>("TimeSinceFtp");
            _cachedTimeSinceMc = this.FindControl<TextBlock>("TimeSinceMc");
            _cachedTimeSinceSql = this.FindControl<TextBlock>("TimeSinceSql");
            _cachedRetryQueueBadge = this.FindControl<Border>("RetryQueueBadge");
            _cachedTxtRetryQueue = this.FindControl<TextBlock>("TxtRetryQueue");
            _cachedHealthScoreText = this.FindControl<TextBlock>("HealthScoreText");
            _cachedHealthTrendText = this.FindControl<TextBlock>("HealthTrendText");
            _cachedHealthFtpScore = this.FindControl<TextBlock>("HealthFtpScore");
            _cachedHealthMcScore = this.FindControl<TextBlock>("HealthMcScore");
            _cachedHealthSqlScore = this.FindControl<TextBlock>("HealthSqlScore");
            _cachedCriticalAlertsCount = this.FindControl<TextBlock>("CriticalAlertsCount");
            _cachedTimeSinceHealth = this.FindControl<TextBlock>("TimeSinceHealth");
            _cachedGlobalBackupProgress = this.FindControl<ProgressBar>("GlobalBackupProgress");
            _cachedBackupProgressText = this.FindControl<TextBlock>("BackupProgressText");
            _cachedBackupProgressPercent = this.FindControl<TextBlock>("BackupProgressPercent");
            _cachedGbpServiceDot = this.FindControl<Ellipse>("GbpServiceDot");
            _cachedGbpServiceName = this.FindControl<TextBlock>("GbpServiceName");
            _cachedMirrorProgressSection = this.FindControl<StackPanel>("MirrorProgressSection");
            _cachedMirrorProgressBar = this.FindControl<ProgressBar>("MirrorProgressBar");
            _cachedMirrorProgressText = this.FindControl<TextBlock>("MirrorProgressText");
            _cachedMirrorProgressPercent = this.FindControl<TextBlock>("MirrorProgressPercent");
            _cachedMirrorServiceName = this.FindControl<TextBlock>("MirrorServiceName");
            _cachedMirrorStatusDetail = this.FindControl<TextBlock>("MirrorStatusDetail");
        }

        private static void FireAndForget(Task task, string context)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    LogService.WriteLiveLog($"[FireAndForget] {context}: {t.Exception.InnerException?.Message ?? t.Exception.Message}", "", "Warning", "SYSTEM");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
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
                        if (_cachedHealthScoreText != null) _cachedHealthScoreText.Text = $"{health.OverallScore}%";
                        if (_cachedHealthTrendText != null) _cachedHealthTrendText.Text = $"{health.Trend} {health.TrendText}";
                        if (_cachedHealthFtpScore != null) _cachedHealthFtpScore.Text = $"{health.ServiceScores.GetValueOrDefault("FTP", 0)}%";
                        if (_cachedHealthMcScore != null) _cachedHealthMcScore.Text = $"{health.ServiceScores.GetValueOrDefault("Mailchimp", 0)}%";
                        if (_cachedHealthSqlScore != null) _cachedHealthSqlScore.Text = $"{health.ServiceScores.GetValueOrDefault("SQL", 0)}%";

                        UpdateServicesStatusSummary(health.ServiceScores);

                        if (_cachedCriticalAlertsCount != null) _cachedCriticalAlertsCount.Text = health.CriticalAlerts.Count.ToString();

                        if (_cachedTimeSinceHealth != null)
                        {
                            var score = health.OverallScore;
                            _cachedTimeSinceHealth.Text = score >= 80 ? "Good" : score >= 50 ? "Fair" : "Poor";
                            _cachedTimeSinceHealth.Foreground = score >= 80 ? new SolidColorBrush(Colors.Green) :
                                                        score >= 50 ? new SolidColorBrush(Colors.Orange) :
                                                        new SolidColorBrush(Colors.Red);
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
            catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] UpdateGreeting error: {ex.Message}", "", "Warning", "SYSTEM"); }
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
        private DateTime? _lastFtpBackupTime;
        private DateTime? _lastMailchimpBackupTime;
        private DateTime? _lastSqlBackupTime;
        private bool _lastBackupWasComplete;
        private int _lastBackupProgressValue = -1;
        private DateTime _lastBackupProgressValueTime = DateTime.MinValue;

        private static string GetServiceColor(string service) => service switch
        {
            "FTP"       => "#52B788",
            "Mailchimp" => "#f0a500",
            "SQL"       => "#fad643",
            _           => "#CDD6F4"
        };

        private void OnBackupProgress(string service, int percent, string status)
        {
            _lastBackupProgressUpdate = DateTime.UtcNow;
            _lastBackupWasComplete = percent >= 100 && status.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase);

            // Stuck-progress detection: if the same percent hasn't changed in 120s, consider it stuck
            if (percent != _lastBackupProgressValue)
            {
                _lastBackupProgressValue = percent;
                _lastBackupProgressValueTime = DateTime.UtcNow;
            }

            // Track in-memory last backup completion time so "time since" updates immediately
            if (_lastBackupWasComplete)
            {
                var now = DateTime.UtcNow;
                if (service.Equals("FTP", StringComparison.OrdinalIgnoreCase)) _lastFtpBackupTime = now;
                else if (service.Equals("Mailchimp", StringComparison.OrdinalIgnoreCase)) _lastMailchimpBackupTime = now;
                else if (service.Equals("SQL", StringComparison.OrdinalIgnoreCase)) _lastSqlBackupTime = now;
                _ = UpdateTimeSinceLastBackupAsync();
            }

            Dispatcher.UIThread.Post(() =>
            {
                var color = Brush.Parse(GetServiceColor(service));

                if (_cachedGlobalBackupProgress != null)  { _cachedGlobalBackupProgress.Value = percent; _cachedGlobalBackupProgress.Foreground = color; }
                if (_cachedBackupProgressText != null)   _cachedBackupProgressText.Text = $"{service}: {status}";
                if (_cachedBackupProgressPercent != null)  { _cachedBackupProgressPercent.Text = $"{percent}%"; _cachedBackupProgressPercent.Foreground = color; }
                if (_cachedGbpServiceDot != null)   _cachedGbpServiceDot.Fill = color;
                if (_cachedGbpServiceName != null)  { _cachedGbpServiceName.Text = service; _cachedGbpServiceName.Foreground = color; }
            });
        }

        private DateTime _lastMirrorProgressUpdate = DateTime.MinValue;
        private bool _lastMirrorWasComplete;

        private void OnMirrorProgressUpdate(string service, int percent, string msg, int currentFile, int totalFiles)
        {
            _lastMirrorProgressUpdate = DateTime.UtcNow;
            _lastMirrorWasComplete = percent >= 100;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_cachedMirrorProgressSection == null) return;

                _cachedMirrorProgressSection.IsVisible = true;

                bool done = percent >= 100 || percent < 0;
                var activeColor = Brush.Parse("#A78BFA");

                if (_cachedMirrorProgressBar != null) { _cachedMirrorProgressBar.Value = percent < 0 ? 0 : percent; _cachedMirrorProgressBar.Foreground = activeColor; }
                if (_cachedMirrorProgressText != null) _cachedMirrorProgressText.Text = done ? (percent < 0 ? "Failed" : "Complete") : "Mirroring...";
                if (_cachedMirrorProgressPercent != null) { _cachedMirrorProgressPercent.Text = percent < 0 ? "✗" : $"{percent}%"; _cachedMirrorProgressPercent.Foreground = activeColor; }
                if (_cachedMirrorServiceName != null) { _cachedMirrorServiceName.Text = service; _cachedMirrorServiceName.Foreground = activeColor; }

                if (_cachedMirrorStatusDetail != null)
                {
                    string fileInfo = totalFiles > 0 && !done && percent >= 0
                        ? $"File {currentFile} of {totalFiles} · {msg}"
                        : msg;
                    _cachedMirrorStatusDetail.Text = fileInfo;
                }
            });
        }

        private void ResetGlobalBackupProgressIfIdle()
        {
            // Use short timeout (5s) for completed backups, longer (60s) for active ones
            var backupTimeout = _lastBackupWasComplete ? 5 : 60;
            bool idleByTimeout = (DateTime.UtcNow - _lastBackupProgressUpdate).TotalSeconds > backupTimeout && _lastBackupProgressUpdate != DateTime.MinValue;
            // Stuck-progress safeguard: if the same non-zero percent hasn't changed in 120s, force reset
            bool stuck = _lastBackupProgressValue > 0 && _lastBackupProgressValue < 100
                         && _lastBackupProgressValueTime != DateTime.MinValue
                         && (DateTime.UtcNow - _lastBackupProgressValueTime).TotalSeconds > 120;
            if (idleByTimeout || stuck)
            {
                var checkedTime = _lastBackupProgressUpdate;
                Dispatcher.UIThread.Post(() =>
                {
                    // Another progress update may have arrived between the check and this callback
                    if (_lastBackupProgressUpdate != checkedTime) return;
                    var idleColor = Brush.Parse("#6C7086");

                    if (_cachedGlobalBackupProgress != null) { _cachedGlobalBackupProgress.Value = 0; _cachedGlobalBackupProgress.Foreground = idleColor; }
                    if (_cachedBackupProgressText != null)   _cachedBackupProgressText.Text = "No active backups";
                    if (_cachedBackupProgressPercent != null){ _cachedBackupProgressPercent.Text = "0%"; _cachedBackupProgressPercent.Foreground = idleColor; }
                    if (_cachedGbpServiceDot != null)            _cachedGbpServiceDot.Fill = idleColor;
                    if (_cachedGbpServiceName != null)        { _cachedGbpServiceName.Text = "Idle"; _cachedGbpServiceName.Foreground = idleColor; }

                    _lastBackupProgressUpdate = DateTime.MinValue;
                    _lastBackupWasComplete = false;
                    _lastBackupProgressValue = -1;
                    _lastBackupProgressValueTime = DateTime.MinValue;
                });
            }

            // Use short timeout (5s) for completed mirrors, longer (60s) for active ones
            var mirrorTimeout = _lastMirrorWasComplete ? 5 : 60;
            if ((DateTime.UtcNow - _lastMirrorProgressUpdate).TotalSeconds > mirrorTimeout && _lastMirrorProgressUpdate != DateTime.MinValue)
            {
                var checkedMirrorTime = _lastMirrorProgressUpdate;
                Dispatcher.UIThread.Post(() =>
                {
                    // Another mirror progress update may have arrived between the check and this callback
                    if (_lastMirrorProgressUpdate != checkedMirrorTime) return;
                    var idleColor = Brush.Parse("#6C7086");

                    if (_cachedMirrorProgressBar != null)  { _cachedMirrorProgressBar.Value = 0; _cachedMirrorProgressBar.Foreground = idleColor; }
                    if (_cachedMirrorProgressText != null)   _cachedMirrorProgressText.Text = "No active mirroring";
                    if (_cachedMirrorProgressPercent != null)    { _cachedMirrorProgressPercent.Text = "0%"; _cachedMirrorProgressPercent.Foreground = idleColor; }
                    if (_cachedMirrorServiceName != null){ _cachedMirrorServiceName.Text = "Idle"; _cachedMirrorServiceName.Foreground = idleColor; }
                    if (_cachedMirrorStatusDetail != null) _cachedMirrorStatusDetail.Text = "";

                    _lastMirrorProgressUpdate = DateTime.MinValue;
                    _lastMirrorWasComplete = false;
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

                if (_cachedDashHealthDotEllipse != null) _cachedDashHealthDotEllipse.Fill = healthBrush;
                if (_cachedDashHealthText != null) { _cachedDashHealthText.Text = allOk ? "ALL SYSTEMS OK" : "ATTENTION REQUIRED"; _cachedDashHealthText.Foreground = healthBrush; }

                if (_cachedAlertBanner != null) _cachedAlertBanner.IsVisible = !allOk;
                if (_cachedAlertText != null && !allOk)
                    _cachedAlertText.Text = $"Sync required: {string.Join(", ", alertServices)}. Open the relevant tab or use Run All Checks.";

                if (_cachedStatServicesOk != null)
                {
                    _cachedStatServicesOk.Text = $"{servicesOk}/3";
                    _cachedStatServicesOk.Foreground = allOk ? Brush.Parse("#588157") : Brush.Parse("#F38BA8");
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
            // Show configured daily schedule times (already in Manila time)
            var ftpTime = new DateTime(1, 1, 1, BackupConfig.FtpDailySyncHourMnl, BackupConfig.FtpDailySyncMinuteMnl, 0);
            var mcTime = new DateTime(1, 1, 1, BackupConfig.MailchimpDailySyncHourMnl, BackupConfig.MailchimpDailySyncMinuteMnl, 0);
            var sqlTime = new DateTime(1, 1, 1, BackupConfig.SqlDailySyncHourMnl, BackupConfig.SqlDailySyncMinuteMnl, 0);

            // Update schedule times for each service (Manila time)
            Set("FtpScheduleTime", ftpTime.ToString("hh:mm tt"));
            Set("McScheduleTime", mcTime.ToString("hh:mm tt"));
            Set("SqlScheduleTime", sqlTime.ToString("hh:mm tt"));

            // Use Manila time for countdown calculation
            var manilaNow = DateTime.UtcNow.AddHours(8);

            // Calculate countdown to next daily backup
            var nextBackup = new[] { BackupManager.NextFtpDailySyncMnl, BackupManager.NextMailchimpDailySyncMnl, BackupManager.NextSqlDailySyncMnl }
                .Where(d => d > manilaNow)
                .OrderBy(d => d)
                .FirstOrDefault();

            if (nextBackup != default)
            {
                var diff = nextBackup - manilaNow;
                string countdown;
                if (diff.TotalHours < 1)
                    countdown = $"{diff.TotalMinutes:F0}m";
                else if (diff.TotalHours < 24)
                    countdown = $"{diff.TotalHours:F1}h";
                else
                    countdown = $"{diff.TotalDays:F1}d";

                Set("NextBackupCountdown", $"Next in: {countdown}");
                Set("TimeSinceNextIn", countdown);
            }
            else
            {
                Set("NextBackupCountdown", "Next in: --");
                Set("TimeSinceNextIn", "--");
            }

            // Status is always "Scheduled" for configured daily times
            Set("FtpScheduleStatus", "Scheduled");
            Set("McScheduleStatus", "Scheduled");
            Set("SqlScheduleStatus", "Scheduled");
            
            // Update upcoming backups preview
            UpdateUpcomingBackupsPreview(manilaNow);
        }
        
        private void UpdateUpcomingBackupsPreview(DateTime manilaNow)
        {
            try
            {
                var upcomingList = this.FindControl<StackPanel>("UpcomingBackupsList");
                if (upcomingList == null) return;
                
                upcomingList.Children.Clear();
                
                // Get next 3 upcoming backups
                var allBackups = new[]
                {
                    (Service: "FTP", Time: BackupManager.NextFtpDailySyncMnl, Color: "#CBA6F7"),
                    (Service: "Mailchimp", Time: BackupManager.NextMailchimpDailySyncMnl, Color: "#F5C2E7"),
                    (Service: "SQL", Time: BackupManager.NextSqlDailySyncMnl, Color: "#94E2D5")
                }
                .Where(b => b.Time > manilaNow)
                .OrderBy(b => b.Time)
                .Take(3)
                .ToList();
                
                foreach (var backup in allBackups)
                {
                    var diff = backup.Time - manilaNow;
                    var timeStr = diff.TotalHours < 1 ? $"in {diff.TotalMinutes:F0}m" :
                                  diff.TotalHours < 24 ? $"in {diff.TotalHours:F1}h" :
                                  $"in {diff.TotalDays:F0}d";
                    
                    var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Avalonia.Media.Brush.Parse(backup.Color), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                    row.Children.Add(new TextBlock { Text = $"{backup.Service} {timeStr}", FontSize = 10, Foreground = Avalonia.Media.Brush.Parse("#A6ADC8") });
                    upcomingList.Children.Add(row);
                }
                
                if (allBackups.Count == 0)
                {
                    upcomingList.Children.Add(new TextBlock { Text = "No upcoming backups scheduled", FontSize = 10, Foreground = Avalonia.Media.Brush.Parse("#6C7086") });
                }
            }
            catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] UpdateUpcomingBackups error: {ex.Message}", "", "Warning", "SYSTEM"); }
        }

        private void UpdateDailySchedule(DateTime? mnlNow = null)
        {
            void SetSched(string ctrl, DateTime next, DateTime now)
            {
                var tb = this.FindControl<TextBlock>(ctrl);
                if (tb == null) return;
                var diff = next - now;
                if (diff.TotalSeconds > 0)
                {
                    // Show next scheduled time in 12-hour AM/PM format
                    tb.Text = next.ToString("h:mm tt");
                }
                else
                {
                    tb.Text = "Due now";
                }
            }

            try
            {
                var now = mnlNow ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

                var ftpNext = BackupManager.NextFtpDailySyncMnl;
                var mcNext = BackupManager.NextMailchimpDailySyncMnl;
                var sqlNext = BackupManager.NextSqlDailySyncMnl;

                SetSched("SchedFtp", ftpNext, now);
                SetSched("SchedMailchimp", mcNext, now);
                SetSched("SchedSql", sqlNext, now);
            }
            catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] UpdateDailySchedule error: {ex.Message}", "", "Warning", "SYSTEM"); }
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
                    catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] ParseLog error: {ex.Message}", "", "Warning", "SYSTEM"); }
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
            
            // Also update backup calendar
            UpdateBackupCalendar();
        }
        
        private void UpdateBackupCalendar()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var calendar = this.FindControl<WrapPanel>("BackupCalendar");
                    if (calendar == null) return;

                    // Use BackupHistoryService for reliable backup records
                    var history = BackupHistoryService.GetHistory(500)
                        .Where(h => h.Timestamp >= DateTime.UtcNow.AddDays(-30))
                        .ToList();

                    // Group by local date
                    var dateStats = new Dictionary<string, (int success, int failed)>();
                    for (int i = 0; i < 30; i++)
                    {
                        var date = DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd");
                        dateStats[date] = (0, 0);
                    }

                    foreach (var entry in history)
                    {
                        var localDate = entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd");
                        if (dateStats.ContainsKey(localDate))
                        {
                            var (s, f) = dateStats[localDate];
                            dateStats[localDate] = entry.Status.Equals("Success", StringComparison.OrdinalIgnoreCase)
                                ? (s + 1, f)
                                : (s, f + 1);
                        }
                    }

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        calendar.Children.Clear();
                        foreach (var date in dateStats.OrderBy(d => d.Key))
                        {
                            var (success, failed) = date.Value;
                            var hasActivity = success > 0 || failed > 0;
                            var color = success > 0 && failed == 0 ? "#52B788" :  // Green - all success
                                        success > 0 && failed > 0 ? "#e6c55c" :   // Yellow - mixed
                                        failed > 0 ? "#F38BA8" :                  // Red - all failed
                                        "#2A2D3E";                                 // Surface - no activity

                            var day = int.Parse(date.Key.Substring(8, 2));
                            var cell = new Border
                            {
                                Width = 26, Height = 26,
                                Background = Brush.Parse(color),
                                CornerRadius = new Avalonia.CornerRadius(6),
                                Margin = new Avalonia.Thickness(2),
                                BorderBrush = hasActivity ? null : Brush.Parse("#3B3E54"),
                                BorderThickness = hasActivity ? new Avalonia.Thickness(0) : new Avalonia.Thickness(1),
                                Child = new TextBlock
                                {
                                    Text = day.ToString(),
                                    FontSize = 10,
                                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                    Foreground = Brush.Parse(hasActivity ? "#11111B" : "#7F849C")
                                }
                            };
                            ToolTip.SetTip(cell, $"{date.Key}: {success} success, {failed} failed");
                            calendar.Children.Add(cell);
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[HomeControl] UpdateBackupCalendar error: {ex.Message}", "Warning", "SYSTEM");
                }
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
            catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] TryParseLogLine error: {ex.Message}", "", "Warning", "SYSTEM"); return false; }
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

        private void StartActiveProcessTimer()
        {
            _activeProcessUpdateTimer?.Stop();
            _activeProcessUpdateTimer?.Dispose();
            _activeProcessUpdateTimer = new System.Timers.Timer(1000);
            _activeProcessUpdateTimer.Elapsed += (_, _) => UpdateActiveProcessDisplay();
            _activeProcessUpdateTimer.AutoReset = true;
            _activeProcessUpdateTimer.Start();
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
            catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] Ping {prefix} error: {ex.Message}", "", "Warning", "SYSTEM"); SetPing(prefix, "#F38BA8", "Unreachable"); }
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
                    catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] ParseStats error: {ex.Message}", "", "Warning", "SYSTEM"); }
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
                    catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] ParseSvc error: {ex.Message}", "", "Warning", "SYSTEM"); }
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
                    catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] GetSummary error: {ex.Message}", "", "Warning", "SYSTEM"); return ""; }
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
            catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] UpdateRecentBackups error: {ex.Message}", "", "Warning", "SYSTEM"); list.Children.Add(new TextBlock { Text = "Error reading folder.", FontSize = 9, Foreground = Brush.Parse("#F38BA8") }); }
        }

        private static void OpenFolder(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) { Directory.CreateDirectory(folder); }
                System.Diagnostics.Process.Start("explorer.exe", folder);
            }
            catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] OpenFolder error: {ex.Message}", "", "Warning", "SYSTEM"); }
        }

        private void OpenNetworkDriveFolder()
        {
            var path = BackupConfig.NetworkDrivePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                NotificationService.ShowBackupToast("Network Drive", "No network drive path configured.", "Warning");
                return;
            }
            try { System.Diagnostics.Process.Start("explorer.exe", path); }
            catch (Exception ex) { NotificationService.ShowBackupToast("Network Drive", $"Cannot open folder: {ex.Message}", "Warning"); }
        }

        private async Task MirrorAllToNetworkDriveAsync()
        {
            if (!NetworkDriveService.IsNetworkDriveConfigured())
            {
                NotificationService.ShowBackupToast("Network Drive", "Network drive not configured or disabled.", "Warning");
                return;
            }

            var results = new Dictionary<string, bool>();
            Set("NdStatusText", "MIRRORING...");
            SetDot("NdStatusDot", "#A78BFA");

            async Task MirrorOne(string folder, string serviceName)
            {
                try
                {
                    if (!Directory.Exists(folder))
                    {
                        LogService.WriteSystemLog($"[NETWORKDRIVE] Skipped {serviceName}: local folder not found ({folder})", "Warning", "NETWORKDRIVE");
                        results[serviceName] = false;
                        return;
                    }
                    var validation = await ValidateMirrorAsync(folder, serviceName);
                    LogService.WriteSystemLog($"[NETWORKDRIVE] {serviceName} mirror starting — {validation}", "Information", "NETWORKDRIVE");
                    await NetworkDriveService.MirrorToNetworkDriveAsync(folder, serviceName);
                    results[serviceName] = true;
                    LogService.WriteSystemLog($"[NETWORKDRIVE] {serviceName} mirror finished", "Information", "NETWORKDRIVE");
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[NETWORKDRIVE] {serviceName} mirror error: {ex.Message}", "Error", "NETWORKDRIVE");
                    results[serviceName] = false;
                }
            }

            NotificationService.ShowBackupToast("Network Drive", "Mirroring all backups...", "Info");

            await MirrorOne(BackupConfig.FtpLocalFolder, "FTP");
            await MirrorOne(BackupConfig.MailchimpFolder, "Mailchimp");
            await MirrorOne(BackupConfig.SqlLocalFolder, "SQL");

            int ok = results.Count(r => r.Value);
            int fail = results.Count(r => !r.Value);
            string summary = string.Join(", ", results.Select(r => $"{r.Key}: {(r.Value ? "OK" : "FAIL")}"));

            NotificationService.ShowBackupToast("Network Drive", $"Mirror complete — {ok} OK, {fail} failed ({summary})", fail == 0 ? "Success" : "Warning");
            UpdateNetworkDriveCard();
        }

        private async Task MirrorServiceAsync(string serviceName, string folder)
        {
            if (!NetworkDriveService.IsNetworkDriveConfigured())
            {
                NotificationService.ShowBackupToast("Network Drive", "Network drive not configured or disabled.", "Warning");
                return;
            }
            if (!Directory.Exists(folder))
            {
                NotificationService.ShowBackupToast("Network Drive", $"{serviceName} local folder not found.", "Warning");
                return;
            }

            var validation = await ValidateMirrorAsync(folder, serviceName);
            NotificationService.ShowBackupToast("Network Drive", $"Mirroring {serviceName}... {validation}", "Info");
            try
            {
                await NetworkDriveService.MirrorToNetworkDriveAsync(folder, serviceName);
                UpdateNetworkDriveCard();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[NETWORKDRIVE] {serviceName} mirror error: {ex.Message}", "Error", "NETWORKDRIVE");
                NotificationService.ShowBackupToast("Network Drive", $"{serviceName} mirror failed: {ex.Message}", "Error");
            }
        }

        private static async Task<string> ValidateMirrorAsync(string sourceFolder, string serviceName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var sourceFiles = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
                    int sourceCount = sourceFiles.Length;
                    long sourceSize = sourceFiles.Sum(f => new FileInfo(f).Length);

                    if (sourceCount == 0)
                        return "0 files to mirror.";

                    string sizeText = sourceSize < 1024 * 1024
                        ? $"{sourceSize / 1024.0:F1} KB"
                        : $"{sourceSize / (1024.0 * 1024):F1} MB";

                    // Check destination for existing files
                    var destBase = BackupConfig.NetworkDrivePath;
                    bool isFtp = destBase.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                                 destBase.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) ||
                                 destBase.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase);

                    if (isFtp)
                        return $"{sourceCount} files ({sizeText}) — FTP destination cannot pre-check existing files.";

                    var destFolder = System.IO.Path.Combine(destBase, serviceName);
                    if (!Directory.Exists(destFolder))
                        return $"{sourceCount} files ({sizeText}) — all new (destination folder does not exist).";

                    var destFiles = Directory.GetFiles(destFolder, "*", SearchOption.AllDirectories);
                    int destCount = destFiles.Length;

                    // Compare by relative path
                    var destRelative = destFiles.Select(f => f.Substring(destFolder.Length).TrimStart(System.IO.Path.DirectorySeparatorChar).ToLowerInvariant()).ToHashSet();
                    int missingAtDest = sourceFiles.Count(sf =>
                    {
                        string rel = sf.Substring(sourceFolder.Length).TrimStart(System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
                        return !destRelative.Contains(rel);
                    });

                    if (missingAtDest == 0)
                        return $"{sourceCount} files ({sizeText}) — destination is up to date.";

                    int existing = sourceCount - missingAtDest;
                    return $"{sourceCount} files ({sizeText}) — {missingAtDest} new, {existing} already present.";
                }
                catch (Exception ex)
                {
                    return $"validation error: {ex.Message}";
                }
            });
        }

        private void UpdateNetworkDriveCard()
        {
            bool configured = NetworkDriveService.IsNetworkDriveConfigured();
            var path = BackupConfig.NetworkDrivePath;

            if (!configured)
            {
                Set("NdStatusText", "Disabled");
                SetDot("NdStatusDot", "#6C7086");
                Set("NdPath", string.IsNullOrWhiteSpace(path) ? "Not configured" : path);
                Set("NdLastMirror", "—");
                Set("NdSummary", "Enable network drive in Settings.");
                return;
            }

            Set("NdPath", path);

            // Find last mirror entry in system log
            try
            {
                var logs = LogService.ImportLatestLogs(AppDataPaths.SystemLogPath, 200);
                var lastMirrorLine = logs.FirstOrDefault(l => l.Contains("[NETWORKDRIVE]") && l.Contains("Mirroring"));
                if (!string.IsNullOrEmpty(lastMirrorLine))
                {
                    // Extract timestamp from log line format: [2026-05-16 11:22:51 PM]
                    var start = lastMirrorLine.IndexOf('[') + 1;
                    var end = lastMirrorLine.IndexOf(']');
                    var tsStr = start >= 0 && end > start ? lastMirrorLine[start..end] : "";
                    Set("NdLastMirror", tsStr.Length > 0 ? tsStr : "—");
                    Set("NdStatusText", "Ready");
                    SetDot("NdStatusDot", "#A78BFA");
                    Set("NdSummary", $"FTP · Mailchimp · SQL mirrored");
                }
                else
                {
                    Set("NdStatusText", "No mirrors yet");
                    SetDot("NdStatusDot", "#e6c55c");
                    Set("NdLastMirror", "—");
                    Set("NdSummary", "Click Mirror All Now to sync.");
                }
            }
            catch
            {
                Set("NdStatusText", "Ready");
                SetDot("NdStatusDot", "#A78BFA");
            }
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
                    catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] ExportLogs error: {ex.Message}", "", "Warning", "SYSTEM"); }
                }
                AddLogs(BackupConfig.FtpLogFile);
                AddLogs(BackupConfig.McLogFile);
                AddLogs(BackupConfig.SqlLogFile);

                var exportPath = System.IO.Path.Combine(EnvironmentConfigService.GetBackupPath(),
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
            _healthRefreshTimer?.Stop();
            _healthRefreshTimer?.Dispose();
            _healthRefreshTimer = new System.Timers.Timer(30000); // 30 seconds
            _healthRefreshTimer.Elapsed += async (sender, e) =>
            {
                if (Interlocked.Exchange(ref _isHealthRefreshing, 1) == 1)
                    return;

                try
                {
                    await LoadHealthDashboardAsync();
                }
                finally
                {
                    Interlocked.Exchange(ref _isHealthRefreshing, 0);
                }
            };
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
            _statsRefreshTimer?.Stop();
            _statsRefreshTimer?.Dispose();
            _statsRefreshTimer = new System.Timers.Timer(45000); // 45 seconds
            _statsRefreshTimer.Elapsed += async (_, _) =>
            {
                if (Interlocked.Exchange(ref _isStatsRefreshing, 1) == 1)
                    return;

                try
                {
                    await LoadWeeklyStatsAsync();
                }
                finally
                {
                    Interlocked.Exchange(ref _isStatsRefreshing, 0);
                }
            };
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
            _dashboardRefreshTimer?.Stop();
            _dashboardRefreshTimer?.Dispose();
            _dashboardRefreshTimer = new System.Timers.Timer(30000); // 30 seconds
            _dashboardRefreshTimer.Elapsed += async (_, _) =>
            {
                if (Interlocked.Exchange(ref _isDashboardRefreshing, 1) == 1)
                    return;

                try
                {
                    await UpdateSystemStatusAsync();
                    await UpdateTimeSinceLastBackupAsync();
                    UpdateRetryQueueStatus();
                }
                finally
                {
                    Interlocked.Exchange(ref _isDashboardRefreshing, 0);
                }
            };
            _dashboardRefreshTimer.AutoReset = true;
            _dashboardRefreshTimer.Start();

            // Initial update
            UpdateRetryQueueStatus();

            LogService.WriteLiveLog("[DASHBOARD] Auto-refresh started (30s interval)", "", "Information", "SYSTEM");
        }

        private int _isErrorRefreshing = 0;

        private void StartErrorRefreshTimer()
        {
            _errorRefreshTimer?.Stop();
            _errorRefreshTimer?.Dispose();
            _errorRefreshTimer = new System.Timers.Timer(60000); // 60 seconds
            _errorRefreshTimer.Elapsed += async (_, _) =>
            {
                if (Interlocked.Exchange(ref _isErrorRefreshing, 1) == 1)
                    return;

                try
                {
                    await LoadRecentErrorsAsync();
                }
                finally
                {
                    Interlocked.Exchange(ref _isErrorRefreshing, 0);
                }
            };
            _errorRefreshTimer.AutoReset = true;
            _errorRefreshTimer.Start();

            LogService.WriteLiveLog("[ERRORS] Auto-refresh started (60s interval)", "", "Information", "SYSTEM");
        }
        
        private void UpdateRetryQueueStatus()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var retries = Services.BackupRetryService.GetPendingRetries();
                    if (_cachedRetryQueueBadge != null && _cachedTxtRetryQueue != null)
                    {
                        if (retries.Count > 0)
                        {
                            _cachedRetryQueueBadge.IsVisible = true;
                            _cachedTxtRetryQueue.Text = retries.Count == 1
                                ? $"1 retry: {retries[0].Service} at {retries[0].NextRetry}"
                                : $"{retries.Count} retries pending";
                        }
                        else
                        {
                            _cachedRetryQueueBadge.IsVisible = false;
                        }
                    }
                }
                catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] UpdateBackupHealth error: {ex.Message}", "", "Warning", "SYSTEM"); }
            });
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
                    catch (Exception ex) { LogService.WriteLiveLog($"[HomeControl] UpdateStorageAsync error: {ex.Message}", "", "Warning", "SYSTEM"); }

                    string storageText = freeSpace >= 1073741824 ? $"{freeSpace / 1073741824.0:F1} GB free" :
                                     freeSpace >= 1048576 ? $"{freeSpace / 1048576.0:F1} MB free" :
                                     freeSpace >= 1024 ? $"{freeSpace / 1024.0:F0} KB free" : "0 B free";

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_cachedSystemUptime != null) _cachedSystemUptime.Text = uptimeText;
                        if (_cachedLastHealthCheck != null) _cachedLastHealthCheck.Text = lastHealthCheck;
                        if (_cachedActiveProcesses != null) _cachedActiveProcesses.Text = activeProcesses;
                        if (_cachedStorageUsage != null) _cachedStorageUsage.Text = storageText;
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

                    var storageUsed = GetCachedStorageUsed();

                    // Calculate trends by comparing with yesterday
                    int yesterdayBackups = 0;
                    double yesterdaySuccessRate = 100.0;
                    var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
                    var yesterdayLogs = allLogs.Where(l => l.Contains(yesterday));
                    foreach (var log in yesterdayLogs)
                    {
                        if (log.Contains("COMPLETE") || log.Contains("SUCCESS"))
                            yesterdayBackups++;
                    }
                    if (yesterdayBackups > 0)
                    {
                        var yesterdayFailed = yesterdayLogs.Count(l => l.Contains("ERROR") || l.Contains("FAILED"));
                        yesterdaySuccessRate = ((double)(yesterdayBackups - yesterdayFailed) / yesterdayBackups) * 100;
                    }

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_cachedStatBackupsToday != null) _cachedStatBackupsToday.Text = backupsToday.ToString();
                        if (_cachedStatSuccessRate != null) _cachedStatSuccessRate.Text = $"{successRate:F0}%";
                        if (_cachedStatFailedBackups != null) _cachedStatFailedBackups.Text = failedBackups.ToString();
                        if (_cachedStatStorageUsed != null) _cachedStatStorageUsed.Text = storageUsed;

                        if (_cachedTrendBackups != null)
                        {
                            var diff = backupsToday - yesterdayBackups;
                            if (diff > 0) { _cachedTrendBackups.Text = "↑" + diff; _cachedTrendBackups.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1"); }
                            else if (diff < 0) { _cachedTrendBackups.Text = "↓" + Math.Abs(diff); _cachedTrendBackups.Foreground = Avalonia.Media.Brush.Parse("#F38BA8"); }
                            else { _cachedTrendBackups.Text = "→"; _cachedTrendBackups.Foreground = Avalonia.Media.Brush.Parse("#6C7086"); }
                        }
                        if (_cachedTrendSuccessRate != null)
                        {
                            var diff = successRate - yesterdaySuccessRate;
                            if (diff > 0) { _cachedTrendSuccessRate.Text = "↑" + diff.ToString("F0"); _cachedTrendSuccessRate.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1"); }
                            else if (diff < 0) { _cachedTrendSuccessRate.Text = "↓" + Math.Abs(diff).ToString("F0"); _cachedTrendSuccessRate.Foreground = Avalonia.Media.Brush.Parse("#F38BA8"); }
                            else { _cachedTrendSuccessRate.Text = "→"; _cachedTrendSuccessRate.Foreground = Avalonia.Media.Brush.Parse("#6C7086"); }
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[SYSTEM] Error updating quick stats: {ex.Message}", "", "Error", "SYSTEM");
                }
            });
        }

        private string GetCachedStorageUsed()
        {
            if (DateTime.UtcNow - _lastStorageStatsRefresh < TimeSpan.FromMinutes(5))
            {
                return _cachedStorageUsed;
            }

            long totalBytes = 0;
            try
            {
                totalBytes += GetFolderSize(BackupConfig.FtpLocalFolder);
                totalBytes += GetFolderSize(BackupConfig.MailchimpFolder);
                totalBytes += GetFolderSize(BackupConfig.SqlLocalFolder);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[HomeControl] GetFolderSize error: {ex.Message}", "", "Warning", "SYSTEM");
            }

            _cachedStorageUsed = totalBytes >= 1073741824 ? $"{totalBytes / 1073741824.0:F1} GB" :
                                 totalBytes >= 1048576 ? $"{totalBytes / 1048576.0:F1} MB" :
                                 totalBytes >= 1024 ? $"{totalBytes / 1024.0:F0} KB" : "0 B";
            _lastStorageStatsRefresh = DateTime.UtcNow;
            return _cachedStorageUsed;
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

                    // Use in-memory time if newer than log-parsed time (covers backups done while app is running)
                    var ftpLastTime = GetLastBackupTime(ftpLogs);
                    var mcLastTime = GetLastBackupTime(mcLogs);
                    var sqlLastTime = GetLastBackupTime(sqlLogs);

                    if (_lastFtpBackupTime.HasValue && _lastFtpBackupTime.Value > (ftpLastTime ?? DateTime.MinValue))
                        ftpLastTime = _lastFtpBackupTime;
                    if (_lastMailchimpBackupTime.HasValue && _lastMailchimpBackupTime.Value > (mcLastTime ?? DateTime.MinValue))
                        mcLastTime = _lastMailchimpBackupTime;
                    if (_lastSqlBackupTime.HasValue && _lastSqlBackupTime.Value > (sqlLastTime ?? DateTime.MinValue))
                        sqlLastTime = _lastSqlBackupTime;

                    var ftpTimeText = GetTimeAgoText(ftpLastTime);
                    var mcTimeText = GetTimeAgoText(mcLastTime);
                    var sqlTimeText = GetTimeAgoText(sqlLastTime);

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_cachedTimeSinceFtp != null)
                        {
                            _cachedTimeSinceFtp.Text = ftpTimeText;
                            _cachedTimeSinceFtp.Foreground = GetTimeAgoColor(ftpLastTime);
                        }
                        if (_cachedTimeSinceMc != null)
                        {
                            _cachedTimeSinceMc.Text = mcTimeText;
                            _cachedTimeSinceMc.Foreground = GetTimeAgoColor(mcLastTime);
                        }
                        if (_cachedTimeSinceSql != null)
                        {
                            _cachedTimeSinceSql.Text = sqlTimeText;
                            _cachedTimeSinceSql.Foreground = GetTimeAgoColor(sqlLastTime);
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
            // Use absolute value to handle any timezone/clock issues
            var totalHours = Math.Abs(diff.TotalHours);
            var totalDays = Math.Abs(diff.TotalDays);
            
            if (totalHours < 24) return $"{totalHours:F0}h ago";
            if (totalDays < 7) return $"{totalDays:F0}d ago";
            return $"{totalDays / 7:F0}w ago";
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

        private void UpdateServicesStatusSummary(Dictionary<string, int>? serviceScores)
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
            string ftpColor = ftpIsStale ? "#e6c55c" : ftpScore >= 80 ? "#4ade80" : ftpScore >= 50 ? "#dad7cd" : ftpScore > 0 ? "#F38BA8" : "#6C7086";
            Set("FtpStatusText", ftpStatus);
            SetDot("FtpStatusDot", ftpColor);
            SetTextColor("FtpStatusText", ftpColor);
            if (ftpScore >= 80 && !ftpIsStale) healthyCount++;

            // Mailchimp - check both health score AND freshness
            int mcScore = serviceScores.GetValueOrDefault("Mailchimp", 0);
            bool mcIsStale = IsBackupStale(mcLastTime);
            string mcStatus = mcIsStale ? "Outdated" : mcScore >= 80 ? "Healthy" : mcScore >= 50 ? "Warning" : mcScore > 0 ? "Critical" : "No Data";
            string mcColor = mcIsStale ? "#e6c55c" : mcScore >= 80 ? "#4ade80" : mcScore >= 50 ? "#caf0f8" : mcScore > 0 ? "#F38BA8" : "#6C7086";
            Set("MailchimpStatusText", mcStatus);
            SetDot("MailchimpStatusDot", mcColor);
            SetTextColor("MailchimpStatusText", mcColor);
            if (mcScore >= 80 && !mcIsStale) healthyCount++;

            // SQL - check both health score AND freshness
            int sqlScore = serviceScores.GetValueOrDefault("SQL", 0);
            bool sqlIsStale = IsBackupStale(sqlLastTime);
            string sqlStatus = sqlIsStale ? "Outdated" : sqlScore >= 80 ? "Healthy" : sqlScore >= 50 ? "Warning" : sqlScore > 0 ? "Critical" : "No Data";
            string sqlColor = sqlIsStale ? "#e6c55c" : sqlScore >= 80 ? "#4ade80" : sqlScore >= 50 ? "#ffe169" : sqlScore > 0 ? "#F38BA8" : "#6C7086";
            Set("SqlStatusText", sqlStatus);
            SetDot("SqlStatusDot", sqlColor);
            SetTextColor("SqlStatusText", sqlColor);
            if (sqlScore >= 80 && !sqlIsStale) healthyCount++;

            // Update services OK text
            Set("ServicesHealthText", $"{healthyCount}/3 healthy");
            Set("StatServicesOk", healthyCount.ToString());

            // Update Network Drive card
            UpdateNetworkDriveCard();
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

        private void SetTextColor(string controlName, string color)
        {
            var tb = this.FindControl<TextBlock>(controlName);
            if (tb != null) tb.Foreground = Brush.Parse(color);
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
            }

            // Also clear system log file and UI display
            LogService.ClearLogs(AppDataPaths.SystemLogPath);

            var systemLogsText = this.FindControl<TextBlock>("SystemLogsText");
            if (systemLogsText != null)
                systemLogsText.Text = string.Empty;

            NotificationService.ShowBackupToast("Recent Errors", "Errors and system logs cleared.", "Success");
        }

        private void ShowDashboardCustomization()
        {
            var dialog = new DashboardCustomizationDialog();
            
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            
            var dialogWindow = new Window
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
            
            if (parentWindow != null)
                dialogWindow.ShowDialog(parentWindow);
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

                    // Throttled auto-refresh of Recent Activity panel
                    if (DateTime.Now - _lastActivityRefresh > TimeSpan.FromSeconds(2))
                    {
                        _lastActivityRefresh = DateTime.Now;
                        LoadRecentActivity();
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
        
        private void UpdateActivityHeatmap()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    // Get all backup logs from the last 52 weeks (reduced from 5000 for performance)
                    var ftpLogs = LogService.ImportLatestLogs(BackupConfig.FtpLogFile, 2000);
                    var mcLogs = LogService.ImportLatestLogs(BackupConfig.McLogFile, 2000);
                    var sqlLogs = LogService.ImportLatestLogs(BackupConfig.SqlLogFile, 2000);
                    var allLogs = ftpLogs.Concat(mcLogs).Concat(sqlLogs).ToList();
                    
                    // Calculate date range (52 weeks ago to today)
                    var endDate = DateTime.Today;
                    var startDate = endDate.AddDays(-52 * 7);
                    var totalDays = (endDate - startDate).Days + 1;
                    
                    // Count backups per day
                    var dailyCounts = new Dictionary<DateTime, int>();
                    for (int i = 0; i < totalDays; i++)
                    {
                        var date = startDate.AddDays(i);
                        dailyCounts[date] = 0;
                    }
                    
                    foreach (var log in allLogs)
                    {
                        if (log.Contains("COMPLETE") || log.Contains("SUCCESS"))
                        {
                            if (TryParseLogLine(log, out var timestamp, out _, out _))
                            {
                                var date = timestamp.Date;
                                if (dailyCounts.ContainsKey(date))
                                    dailyCounts[date]++;
                            }
                        }
                    }
                    
                    // Calculate statistics
                    var totalBackups = dailyCounts.Values.Sum();
                    var maxCount = dailyCounts.Values.Max();
                    var currentStreak = CalculateCurrentStreak(dailyCounts, endDate);
                    
                    // Generate heatmap data (52 weeks × 7 days)
                    var heatmapData = new List<List<int>>();
                    for (int week = 0; week < 52; week++)
                    {
                        var weekData = new List<int>();
                        for (int day = 0; day < 7; day++)
                        {
                            var date = startDate.AddDays(week * 7 + day);
                            weekData.Add(dailyCounts.ContainsKey(date) ? dailyCounts[date] : 0);
                        }
                        heatmapData.Add(weekData);
                    }
                    
                    // Update UI with batching for better performance
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var container = this.FindControl<StackPanel>("HeatmapContainer");
                        var monthLabels = this.FindControl<Grid>("MonthLabelsContainer");
                        var summary = this.FindControl<TextBlock>("HeatmapSummary");
                        var streak = this.FindControl<TextBlock>("HeatmapStreak");
                        
                        // Update summary text first
                        if (summary != null)
                            summary.Text = $"{totalBackups} backups in the last year";
                        
                        if (streak != null)
                            streak.Text = $"Current streak: {currentStreak} days";
                        
                        if (container != null)
                        {
                            container.Children.Clear();
                            
                            // Pre-create all cells to minimize UI updates
                            var weekColumns = new List<StackPanel>();
                            for (int week = 0; week < 52; week++)
                            {
                                var weekColumn = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Spacing = 2 };
                                var weekCells = new List<Border>();
                                
                                for (int day = 0; day < 7; day++)
                                {
                                    var count = week < heatmapData.Count && day < heatmapData[week].Count 
                                        ? heatmapData[week][day] : 0;
                                    
                                    var color = GetHeatmapColor(count, maxCount);
                                    var cell = new Border
                                    {
                                        Width = 11,
                                        Height = 11,
                                        Background = new SolidColorBrush(Color.Parse(color)),
                                        CornerRadius = new Avalonia.CornerRadius(2)
                                    };
                                    ToolTip.SetTip(cell, $"Day {week * 7 + day + 1}: {count} backup(s)");
                                    
                                    weekCells.Add(cell);
                                }
                                
                                // Add all cells at once
                                foreach (var cell in weekCells)
                                    weekColumn.Children.Add(cell);
                                    
                                weekColumns.Add(weekColumn);
                            }
                            
                            // Add all week columns at once
                            foreach (var weekColumn in weekColumns)
                                container.Children.Add(weekColumn);
                        }
                        
                        if (monthLabels != null)
                        {
                            monthLabels.Children.Clear();
                            var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
                            var startMonth = startDate.Month;
                            
                            var monthLabelsList = new List<TextBlock>();
                            for (int i = 0; i < 12; i++)
                            {
                                var monthIndex = (startMonth + i - 1) % 12;
                                var label = new TextBlock
                                {
                                    Text = months[monthIndex],
                                    FontSize = 9,
                                    Foreground = new SolidColorBrush(Color.Parse("#6E7681")),
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                                };
                                Grid.SetColumn(label, i);
                                monthLabelsList.Add(label);
                            }
                            
                            foreach (var label in monthLabelsList)
                                monthLabels.Children.Add(label);
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Home] Error updating heatmap: {ex.Message}");
                }
            });
        }
        
        private static int CalculateCurrentStreak(Dictionary<DateTime, int> dailyCounts, DateTime endDate)
        {
            var streak = 0;
            var currentDate = endDate;
            
            while (currentDate >= endDate.AddDays(-52 * 7))
            {
                if (dailyCounts.ContainsKey(currentDate) && dailyCounts[currentDate] > 0)
                    streak++;
                else
                    break;
                    
                currentDate = currentDate.AddDays(-1);
            }
            
            return streak;
        }
        
        private static string GetHeatmapColor(int count, int maxCount)
        {
            if (count == 0) return "#161B22"; // No activity
            if (maxCount == 0) return "#0E4429"; // Default green
            
            var intensity = (double)count / maxCount;
            
            return intensity switch
            {
                < 0.25 => "#0E4429",  // Light green
                < 0.5 => "#006D32",   // Medium green
                < 0.75 => "#26A641",  // Bright green
                _ => "#39D353"       // Brightest green
            };
        }
        
        
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            // Restart timer if control was unloaded and reloaded (e.g., tab switch)
            StartActiveProcessTimer();
            // Force reset any stale progress state on reload
            _lastBackupProgressUpdate = DateTime.MinValue;
            _lastBackupWasComplete = false;
            _lastMirrorProgressUpdate = DateTime.MinValue;
            _lastMirrorWasComplete = false;
            _lastBackupProgressValue = -1;
            _lastBackupProgressValueTime = DateTime.MinValue;

            // Directly reset UI to idle — ResetGlobalBackupProgressIfIdle guards against DateTime.MinValue
            var idleColor = Brush.Parse("#6C7086");
            if (_cachedGlobalBackupProgress != null)  { _cachedGlobalBackupProgress.Value = 0; _cachedGlobalBackupProgress.Foreground = idleColor; }
            if (_cachedBackupProgressText != null)    _cachedBackupProgressText.Text = "No active backups";
            if (_cachedBackupProgressPercent != null) { _cachedBackupProgressPercent.Text = "0%"; _cachedBackupProgressPercent.Foreground = idleColor; }
            if (_cachedGbpServiceDot != null)         _cachedGbpServiceDot.Fill = idleColor;
            if (_cachedGbpServiceName != null)        { _cachedGbpServiceName.Text = "Idle"; _cachedGbpServiceName.Foreground = idleColor; }

            if (_cachedMirrorProgressBar != null)     { _cachedMirrorProgressBar.Value = 0; _cachedMirrorProgressBar.Foreground = idleColor; }
            if (_cachedMirrorProgressText != null)    _cachedMirrorProgressText.Text = "No active mirroring";
            if (_cachedMirrorProgressPercent != null) { _cachedMirrorProgressPercent.Text = "0%"; _cachedMirrorProgressPercent.Foreground = idleColor; }
            if (_cachedMirrorServiceName != null)     { _cachedMirrorServiceName.Text = "Idle"; _cachedMirrorServiceName.Foreground = idleColor; }
            if (_cachedMirrorStatusDetail != null)    _cachedMirrorStatusDetail.Text = "";

            // Hide network drive UI when disabled in settings
            var ndEnabled = BackupConfig.NetworkDriveEnabled;
            var ndCard = this.FindControl<Border>("NetworkDriveCard");
            if (ndCard != null) ndCard.IsVisible = ndEnabled;

            var btnFtpMirror = this.FindControl<Button>("BtnFtpMirror");
            var btnMcMirror = this.FindControl<Button>("BtnMcMirror");
            var btnSqlMirror = this.FindControl<Button>("BtnSqlMirror");
            if (btnFtpMirror != null) btnFtpMirror.IsVisible = ndEnabled;
            if (btnMcMirror != null) btnMcMirror.IsVisible = ndEnabled;
            if (btnSqlMirror != null) btnSqlMirror.IsVisible = ndEnabled;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            
            // Unsubscribe from all events to prevent memory leaks
            LogService.OnNewLogEntry -= OnNewSystemLogEntry;
            ConfigService.OnScheduleChanged -= OnScheduleChangedFromFirebase;
            NetworkDriveService.OnMirrorProgress -= OnMirrorProgressUpdate;
            _manager.OnAutoScanTimersReset -= OnAutoScanTimersReset;
            _manager.OnDailyScheduleUpdated -= OnDailyScheduleUpdated;
            _manager.OnHealthUpdate -= OnHealthUpdate;
            _manager.OnTimeUpdate -= OnTimeUpdate;
            _manager.OnBackupProgress -= OnBackupProgress;
            AuthService.OnUserChanged -= (_) => UpdateGreeting();
            
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
            _errorRefreshTimer?.Stop();
            _errorRefreshTimer?.Dispose();
            _activeProcessUpdateTimer?.Stop();
            _activeProcessUpdateTimer?.Dispose();

            _healthRefreshTimer = null;
            _statsRefreshTimer = null;
            _dashboardRefreshTimer = null;
            _errorRefreshTimer = null;
            _activeProcessUpdateTimer = null;

            Interlocked.Exchange(ref _isHealthRefreshing, 0);
            Interlocked.Exchange(ref _isStatsRefreshing, 0);
            Interlocked.Exchange(ref _isDashboardRefreshing, 0);
            Interlocked.Exchange(ref _isErrorRefreshing, 0);

            // Null cached controls to allow GC
            _cachedDashHealthDotEllipse = null;
            _cachedDashHealthText = null;
            _cachedAlertBanner = null;
            _cachedAlertText = null;
            _cachedStatServicesOk = null;
            _cachedSystemUptime = null;
            _cachedLastHealthCheck = null;
            _cachedActiveProcesses = null;
            _cachedStorageUsage = null;
            _cachedStatBackupsToday = null;
            _cachedStatSuccessRate = null;
            _cachedStatFailedBackups = null;
            _cachedStatStorageUsed = null;
            _cachedTrendBackups = null;
            _cachedTrendSuccessRate = null;
            _cachedTimeSinceFtp = null;
            _cachedTimeSinceMc = null;
            _cachedTimeSinceSql = null;
            _cachedRetryQueueBadge = null;
            _cachedTxtRetryQueue = null;
            _cachedHealthScoreText = null;
            _cachedHealthTrendText = null;
            _cachedHealthFtpScore = null;
            _cachedHealthMcScore = null;
            _cachedHealthSqlScore = null;
            _cachedCriticalAlertsCount = null;
            _cachedTimeSinceHealth = null;
            _cachedGlobalBackupProgress = null;
            _cachedBackupProgressText = null;
            _cachedBackupProgressPercent = null;
            _cachedGbpServiceDot = null;
            _cachedGbpServiceName = null;
            _cachedMirrorProgressSection = null;
            _cachedMirrorProgressBar = null;
            _cachedMirrorProgressText = null;
            _cachedMirrorProgressPercent = null;
            _cachedMirrorServiceName = null;
            _cachedMirrorStatusDetail = null;
        }
    }
}
