using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Interactivity;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class HomeControl : UserControl
    {
        private readonly BackupManager _manager;
        private System.Timers.Timer? _autoPingTimer;
        private System.Timers.Timer? _statsTimer;
        private System.Timers.Timer? _scheduleTimer;
        private System.Timers.Timer? _storageTimer;
        private System.Timers.Timer? _healthRefreshTimer;
        private System.Timers.Timer? _statsRefreshTimer;

        public event Action? OnNavigateFtp;
        public event Action? OnNavigateMailchimp;
        public event Action? OnNavigateSql;
        public event Action? OnRunAllChecks;
        public event Action? OnEmergencyStop;

        private bool _autoPinged;
        private bool _maintenancePaused;

        public HomeControl(BackupManager manager)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            _manager = manager;

            _manager.OnHealthUpdate += OnHealthUpdate;
            _manager.OnTimeUpdate += OnTimeUpdate;

            this.FindControl<Button>("BtnGoFtp")!.Click += (_, _) => OnNavigateFtp?.Invoke();
            this.FindControl<Button>("BtnGoMailchimp")!.Click += (_, _) => OnNavigateMailchimp?.Invoke();
            this.FindControl<Button>("BtnGoSql")!.Click += (_, _) => OnNavigateSql?.Invoke();
            this.FindControl<Button>("BtnRunAllChecks")!.Click += (_, _) => OnRunAllChecks?.Invoke();
            this.FindControl<Button>("BtnRefreshActivity")!.Click += (_, _) => LoadRecentActivity();

            this.FindControl<Button>("BtnPingAll")!.Click += async (_, _) => await PingAllAsync();
            this.FindControl<Button>("BtnOpenSchedule")!.Click += async (_, _) => await OpenScheduleDialogAsync();
            this.FindControl<Button>("BtnBackupAll")!.Click += (_, _) => { SetOpStatus("Running all backup checks...", "#FAB387"); OnRunAllChecks?.Invoke(); SetOpStatus("All checks triggered.", "#A6E3A1"); };
            this.FindControl<Button>("BtnTestAllConn")!.Click += async (_, _) => await PingAllAsync();
            this.FindControl<Button>("BtnRetryFailed")!.Click += (_, _) => { SetOpStatus("Retrying all services...", "#FAB387"); OnRunAllChecks?.Invoke(); SetOpStatus("Retry triggered. Check service tabs for results.", "#A6E3A1"); };
            this.FindControl<Button>("BtnCompactToggle")!.Click += (_, _) => ToggleCompactMode();
            this.FindControl<Button>("BtnEmergencyStop")!.Click += (_, _) => { OnEmergencyStop?.Invoke(); SetOpStatus("Emergency stop sent to all services.", "#F38BA8"); };
            this.FindControl<Button>("BtnMaintenanceToggle")!.Click += (_, _) => ToggleMaintenance();
            this.FindControl<Button>("BtnExportCsv")!.Click += (_, _) => ExportActivityCsv();

            this.FindControl<Button>("BtnFtpFiles")!.Click += (_, _) => ToggleFileBrowser("Ftp", BackupConfig.FtpLocalFolder);
            this.FindControl<Button>("BtnMcFiles")!.Click  += (_, _) => ToggleFileBrowser("Mc",  BackupConfig.MailchimpFolder);
            this.FindControl<Button>("BtnSqlFiles")!.Click += (_, _) => ToggleFileBrowser("Sql", BackupConfig.SqlLocalFolder);
            this.FindControl<Button>("BtnFtpOpenFolder")!.Click += (_, _) => OpenFolder(BackupConfig.FtpLocalFolder);
            this.FindControl<Button>("BtnMcOpenFolder")!.Click  += (_, _) => OpenFolder(BackupConfig.MailchimpFolder);
            this.FindControl<Button>("BtnSqlOpenFolder")!.Click += (_, _) => OpenFolder(BackupConfig.SqlLocalFolder);

            this.FindControl<Button>("BtnRefreshHealth")!.Click += (_, _) => _ = LoadHealthDashboardAsync();

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
                                        "FTP" => "☁",
                                        "Mailchimp" => "✉",
                                        "SQL" => "🗄",
                                        _ => "⚠"
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
                                    Text = "✅ No critical alerts - all systems healthy!",
                                    FontSize = 10,
                                    Foreground = Avalonia.Media.Brush.Parse("#A6E3A1"),
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
                var healthBrush = allOk ? Brush.Parse("#A6E3A1") : Brush.Parse("#F38BA8");

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
                    statOk.Foreground = allOk ? Brush.Parse("#A6E3A1") : Brush.Parse("#F38BA8");
                }

                UpdateGreeting();
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

        private void OnTimeUpdate(DateTime usTime, DateTime mnlTime, DateTime nextAuto, DateTime nextDaily)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetTimer("FtpNextScan", _manager.NextFtpAutoScan, usTime);
                SetTimer("MailchimpNextScan", _manager.NextMailchimpAutoScan, usTime);
                SetTimer("SqlNextScan", _manager.NextSqlAutoScan, usTime);
                UpdateDailySchedule(mnlTime);
            });
        }

        private void SetTimer(string controlName, DateTime next, DateTime now)
        {
            var txt = this.FindControl<TextBlock>(controlName);
            if (txt == null) return;
            var diff = next - now;
            txt.Text = diff.TotalSeconds > 0 ? diff.ToString(@"hh\:mm\:ss") : "Due now";
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
                SetSched("SchedFtp", BackupManager.NextFtpDailySyncMnl, now);
                SetSched("SchedMailchimp", BackupManager.NextMailchimpDailySyncMnl, now);
                SetSched("SchedSql", BackupManager.NextSqlDailySyncMnl, now);
            }
            catch { }
        }

        private async Task UpdateStorageAsync()
        {
            try
            {
                var ftpSize = await Task.Run(() => GetFolderSize(BackupConfig.FtpLocalFolder));
                var mcSize = await Task.Run(() => GetFolderSize(BackupConfig.MailchimpFolder));
                var sqlSize = await Task.Run(() => GetFolderSize(BackupConfig.SqlLocalFolder));

                var ftpCount = await Task.Run(() => GetFileCount(BackupConfig.FtpLocalFolder));
                var mcCount = await Task.Run(() => GetFileCount(BackupConfig.MailchimpFolder));
                var sqlCount = await Task.Run(() => GetFileCount(BackupConfig.SqlLocalFolder));

                long totalSize = ftpSize + mcSize + sqlSize;
                int totalFiles = ftpCount + mcCount + sqlCount;
                long maxSize = Math.Max(1, Math.Max(ftpSize, Math.Max(mcSize, sqlSize)));

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Set("StorageFtp", FormatSize(ftpSize));
                    Set("StorageMailchimp", FormatSize(mcSize));
                    Set("StorageSql", FormatSize(sqlSize));
                    Set("StatStorage", FormatSize(totalSize));
                    Set("StatTotalFiles", totalFiles.ToString("N0"));

                    SetBar("StorageFtpBar", ftpSize, maxSize);
                    SetBar("StorageMailchimpBar", mcSize, maxSize);
                    SetBar("StorageSqlBar", sqlSize, maxSize);
                });
            }
            catch { }
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
                            Foreground = Brush.Parse("#45475A"),
                            FontSize = 11
                        });
                        return;
                    }

                    foreach (var (ts, service, level, msg) in sorted)
                    {
                        var svcColor = service switch
                        {
                            "FTP" => "#A6E3A1",
                            "MC" => "#89DCEB",
                            "SQL" => "#F9E2AF",
                            _ => "#A6ADC8"
                        };
                        var lvlColor = level switch
                        {
                            "ERROR" => "#F38BA8",
                            "WARNING" => "#FAB387",
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
                            Foreground = Brush.Parse("#A6ADC8"),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                        };
                        Grid.SetColumn(msgTxt, 2);

                        var timeTxt = new TextBlock
                        {
                            Text = ts.ToString("HH:mm"),
                            FontSize = 9,
                            Foreground = Brush.Parse("#45475A"),
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
            "LimeGreen" => Brush.Parse("#A6E3A1"),
            "Orange" => Brush.Parse("#FAB387"),
            "Red" => Brush.Parse("#F38BA8"),
            _ => Brush.Parse("#6C7086")
        };

        // ── Schedule Adjustment ──────────────────────────────────────────────

        private bool _compactMode;

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

        private void ToggleCompactMode()
        {
            _compactMode = !_compactMode;
            var section = this.FindControl<Avalonia.Controls.Grid>("ServiceCardsSection");
            if (section != null) section.IsVisible = !_compactMode;
            var btn = this.FindControl<Button>("BtnCompactToggle");
            if (btn != null) btn.Content = _compactMode ? "⊞ Expand" : "⊟ Compact";
        }

        // ── Connectivity ─────────────────────────────────────────────────────

        private async Task PingAllAsync()
        {
            SetPing("Ftp", "#FAB387", "Checking...");
            SetPing("Sql", "#FAB387", "Checking...");
            SetPing("Mc",  "#FAB387", "Checking...");
            SetOpStatus("Testing all connections...", "#FAB387");

            await Task.WhenAll(
                TcpCheckAsync("Ftp", BackupConfig.FtpHost,                    BackupConfig.FtpPort),
                TcpCheckAsync("Sql", ConfigService.Current.Sql.Host,          22),
                TcpCheckAsync("Mc",  "api.mailchimp.com",                      443)
            );

            SetOpStatus("Connection test complete.", "#A6E3A1");
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
                    SetPing(prefix, "#A6E3A1", $"{sw.ElapsedMilliseconds} ms");
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
                            if ((msg.Contains("SESSION: Finished", StringComparison.OrdinalIgnoreCase) || msg.Contains("completed", StringComparison.OrdinalIgnoreCase)) && sessionStart != null)
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
                                         || msg.Contains("SUCCESS:", StringComparison.OrdinalIgnoreCase)
                                         || msg.Contains("COMPLETE:", StringComparison.OrdinalIgnoreCase);
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
                            string color = !dayHasActivity[i] ? "#313244" : dayHasError[i] ? "#F38BA8" : "#A6E3A1";
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
                    var name = new TextBlock { Text = $"📄 {file.Name}", FontSize = 9, Foreground = Brush.Parse(ThemeService.IsDark ? "#A6ADC8" : "#5C5F77"), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
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
                btn.Foreground = _maintenancePaused ? Brush.Parse("#FAB387") : null;
            }
            var msg = _maintenancePaused ? "Maintenance mode ON — auto-scans paused." : "Maintenance mode OFF — auto-scans resumed.";
            SetOpStatus(msg, _maintenancePaused ? "#FAB387" : "#A6E3A1");
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
                SetOpStatus($"CSV exported → {System.IO.Path.GetFileName(exportPath)}", "#A6E3A1");
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
            _statsRefreshTimer = new System.Timers.Timer(45000); // 45 seconds (slightly different from health)
            _statsRefreshTimer.Elapsed += async (sender, e) => 
            {
                await LoadWeeklyStatsAsync();
                await UpdateStorageAsync();
                await LoadLastBackupSummariesAsync();
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

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            
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
