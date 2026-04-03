using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class HomeControl : UserControl
    {
        private readonly BackupManager _manager;

        public event Action? OnNavigateFtp;
        public event Action? OnNavigateMailchimp;
        public event Action? OnNavigateSql;
        public event Action? OnRunAllChecks;

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

            UpdateGreeting();
            UpdateDailySchedule();
            LoadRecentActivity();
            _ = UpdateStorageAsync();
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
    }
}
