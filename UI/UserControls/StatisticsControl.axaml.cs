using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class StatisticsControl : UserControl
    {
        private readonly List<BackupStatistic> _statistics;
        private DateTime _dateRangeStart;
        private DateTime _dateRangeEnd;
        private int _dateRangeDays = 30;

        public StatisticsControl()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            _statistics = new List<BackupStatistic>();
            
            SetupEventHandlers();
            LoadInitialData();
        }

        private void SetupEventHandlers()
        {
            // Button handlers
            this.FindControl<Button>("BtnRefreshStats")!.Click += async (_, _) => await RefreshStatisticsAsync();
            this.FindControl<Button>("BtnExportStats")!.Click += async (_, _) => await ExportStatisticsAsync();
            this.FindControl<Button>("BtnDateRange")!.Click += (_, _) => ShowDateRangeDialog();
        }

        private async void LoadInitialData()
        {
            _dateRangeEnd = DateTime.Now;
            _dateRangeStart = _dateRangeEnd.AddDays(-_dateRangeDays);
            
            await RefreshStatisticsAsync();
        }

        private async Task RefreshStatisticsAsync()
        {
            try
            {
                LogService.WriteLiveLog("[STATISTICS] Loading backup statistics...", "", "Information", "SYSTEM");
                
                // Load statistics data
                await LoadStatisticsData();
                
                // Update overview cards
                UpdateOverviewCards();
                
                // Update charts with error handling
                try
                {
                    UpdateBackupVolumeChart();
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[STATISTICS] Error updating backup volume chart: {ex.Message}", "", "Warning", "SYSTEM");
                }
                
                try
                {
                    UpdateSuccessRateChart();
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[STATISTICS] Error updating success rate chart: {ex.Message}", "", "Warning", "SYSTEM");
                }
                
                try
                {
                    UpdateStorageGrowthChart();
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[STATISTICS] Error updating storage growth chart: {ex.Message}", "", "Warning", "SYSTEM");
                }
                
                try
                {
                    UpdatePerformanceChart();
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[STATISTICS] Error updating performance chart: {ex.Message}", "", "Warning", "SYSTEM");
                }
                
                // Update service breakdown
                UpdateServiceBreakdown();
                
                LogService.WriteLiveLog("[STATISTICS] Statistics loaded successfully", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[STATISTICS] Error loading statistics: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Statistics", $"Error loading statistics: {ex.Message}", "Error");
                
                // Show error message in UI
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShowErrorState("Failed to load statistics data");
                });
            }
        }

        private async Task LoadStatisticsData()
        {
            _statistics.Clear();
            
            // Get logs from all services
            var ftpLogs = LogService.ImportLatestLogs(BackupConfig.FtpLogFile, 1000);
            var mcLogs = LogService.ImportLatestLogs(BackupConfig.McLogFile, 1000);
            var sqlLogs = LogService.ImportLatestLogs(BackupConfig.SqlLogFile, 1000);
            
            LogService.WriteLiveLog($"[STATISTICS] Loaded logs - FTP: {ftpLogs.Count}, MC: {mcLogs.Count}, SQL: {sqlLogs.Count}", "", "Information", "SYSTEM");
            
            // Process FTP logs
            ProcessServiceLogs("FTP", ftpLogs);
            
            // Process Mailchimp logs
            ProcessServiceLogs("Mailchimp", mcLogs);
            
            // Process SQL logs
            ProcessServiceLogs("SQL", sqlLogs);
            
            // Sort by date
            _statistics.Sort((a, b) => a.Date.CompareTo(b.Date));
            
            LogService.WriteLiveLog($"[STATISTICS] Processed {_statistics.Count} actual backup events", "", "Information", "SYSTEM");
        }

        private void ProcessServiceLogs(string service, List<string> logs)
        {
            foreach (var log in logs)
            {
                // Only count actual backup events, not all log lines
                if (!(log.Contains("COMPLETE") || log.Contains("SUCCESS") || log.Contains("ERROR") || log.Contains("FAILED")))
                    continue;
                
                if (TryParseLogLine(log, out var timestamp, out var level, out var message))
                {
                    // Check if within date range
                    if (timestamp < _dateRangeStart || timestamp > _dateRangeEnd)
                        continue;
                    
                    var stat = new BackupStatistic
                    {
                        Date = timestamp.Date,
                        Service = service,
                        Success = level != "ERROR" && level != "FAILED",
                        Duration = ExtractDuration(message),
                        StorageSize = ExtractStorageSize(message)
                    };
                    
                    _statistics.Add(stat);
                }
            }
        }

        private TimeSpan ExtractDuration(string message)
        {
            // Try to extract duration from log message
            var durationKeywords = new[] { "completed in", "took", "duration", "time" };
            
            foreach (var keyword in durationKeywords)
            {
                var index = message.ToLower().IndexOf(keyword);
                if (index >= 0)
                {
                    var afterKeyword = message.Substring(index + keyword.Length);
                    var parts = afterKeyword.Split(new[] { ' ', 's', 'm', 'h' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (parts.Length > 0 && double.TryParse(parts[0], out var value))
                    {
                        if (afterKeyword.Contains('h'))
                            return TimeSpan.FromHours(value);
                        else if (afterKeyword.Contains('m'))
                            return TimeSpan.FromMinutes(value);
                        else
                            return TimeSpan.FromSeconds(value);
                    }
                }
            }
            
            return TimeSpan.Zero;
        }

        private long ExtractStorageSize(string message)
        {
            // Try to extract storage size from log message
            var sizeKeywords = new[] { "MB", "GB", "KB", "bytes" };
            
            foreach (var keyword in sizeKeywords)
            {
                var index = message.ToLower().IndexOf(keyword.ToLower());
                if (index >= 0)
                {
                    var beforeKeyword = message.Substring(0, index);
                    var parts = beforeKeyword.Split(new[] { ' ', ':', '-', '(' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (parts.Length > 0 && double.TryParse(parts.Last(), out var value))
                    {
                        switch (keyword)
                        {
                            case "GB": return (long)(value * 1024 * 1024 * 1024);
                            case "MB": return (long)(value * 1024 * 1024);
                            case "KB": return (long)(value * 1024);
                            case "bytes": return (long)value;
                        }
                    }
                }
            }
            
            return 0;
        }

        private void UpdateOverviewCards()
        {
            var totalBackups = _statistics.Count;
            var successfulBackups = _statistics.Count(s => s.Success);
            var successRate = totalBackups > 0 ? (successfulBackups * 100.0 / totalBackups) : 100.0;
            var avgDuration = _statistics.Count > 0 ? 
                TimeSpan.FromTicks((long)_statistics.Average(s => s.Duration.Ticks)) : 
                TimeSpan.Zero;
            var totalStorage = _statistics.Sum(s => s.StorageSize);
            
            // Calculate trends (compare with previous period)
            var previousPeriodStart = _dateRangeStart.AddDays(-_dateRangeDays);
            var previousPeriodEnd = _dateRangeStart;
            var previousStats = _statistics.Where(s => s.Date >= previousPeriodStart && s.Date < previousPeriodEnd).ToList();
            
            var previousTotal = previousStats.Count;
            var previousSuccessRate = previousTotal > 0 ? (previousStats.Count(s => s.Success) * 100.0 / previousTotal) : 100.0;
            var previousAvgDuration = previousStats.Count > 0 ? 
                TimeSpan.FromTicks((long)previousStats.Average(s => s.Duration.Ticks)) : 
                TimeSpan.Zero;
            var previousStorage = previousStats.Sum(s => s.StorageSize);
            
            // Calculate trends
            var backupsTrend = previousTotal > 0 ? ((totalBackups - previousTotal) * 100.0 / previousTotal) : 0;
            var successTrend = previousSuccessRate > 0 ? (successRate - previousSuccessRate) : 0;
            var durationTrend = previousAvgDuration.TotalSeconds > 0 ? 
                ((avgDuration.TotalSeconds - previousAvgDuration.TotalSeconds) * 100.0 / previousAvgDuration.TotalSeconds) : 0;
            var storageTrend = previousStorage > 0 ? ((totalStorage - previousStorage) * 100.0 / previousStorage) : 0;
            
            // Update UI with null checks
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var txtTotalBackups = this.FindControl<TextBlock>("TxtTotalBackups");
                var txtSuccessRate = this.FindControl<TextBlock>("TxtSuccessRate");
                var txtAvgDuration = this.FindControl<TextBlock>("TxtAvgDuration");
                var txtStorageUsed = this.FindControl<TextBlock>("TxtStorageUsed");
                
                var txtBackupsTrend = this.FindControl<TextBlock>("TxtBackupsTrend");
                var txtSuccessTrend = this.FindControl<TextBlock>("TxtSuccessTrend");
                var txtDurationTrend = this.FindControl<TextBlock>("TxtDurationTrend");
                var txtStorageTrend = this.FindControl<TextBlock>("TxtStorageTrend");
                
                if (txtTotalBackups != null) txtTotalBackups.Text = totalBackups.ToString();
                if (txtSuccessRate != null) txtSuccessRate.Text = $"{successRate:F1}%";
                if (txtAvgDuration != null) txtAvgDuration.Text = FormatDuration(avgDuration);
                if (txtStorageUsed != null) txtStorageUsed.Text = FormatBytes(totalStorage);
                
                if (txtBackupsTrend != null)
                {
                    txtBackupsTrend.Text = $"{(backupsTrend >= 0 ? "↑" : "↓")} {Math.Abs(backupsTrend):F1}%";
                    txtBackupsTrend.Foreground = 
                        backupsTrend >= 0 ? new SolidColorBrush(Color.Parse("#A6E3A1")) : new SolidColorBrush(Color.Parse("#F38BA8"));
                }
                
                if (txtSuccessTrend != null)
                {
                    txtSuccessTrend.Text = $"{(successTrend >= 0 ? "↑" : "↓")} {Math.Abs(successTrend):F1}%";
                    txtSuccessTrend.Foreground = 
                        successTrend >= 0 ? new SolidColorBrush(Color.Parse("#A6E3A1")) : new SolidColorBrush(Color.Parse("#F38BA8"));
                }
                
                if (txtDurationTrend != null)
                {
                    txtDurationTrend.Text = $"{(durationTrend >= 0 ? "↑" : "↓")} {Math.Abs(durationTrend):F1}%";
                    txtDurationTrend.Foreground = 
                        durationTrend <= 0 ? new SolidColorBrush(Color.Parse("#A6E3A1")) : new SolidColorBrush(Color.Parse("#F38BA8"));
                }
                
                if (txtStorageTrend != null)
                {
                    txtStorageTrend.Text = $"{(storageTrend >= 0 ? "↑" : "↓")} {Math.Abs(storageTrend):F1}%";
                    txtStorageTrend.Foreground = 
                        storageTrend >= 0 ? new SolidColorBrush(Color.Parse("#A6E3A1")) : new SolidColorBrush(Color.Parse("#F38BA8"));
                }
            });
        }

        private void ShowErrorState(string errorMessage)
        {
            try
            {
                // Clear all charts and show error message
                var canvases = new[] { "BackupVolumeCanvas", "SuccessRateCanvas", "StorageGrowthCanvas", "PerformanceCanvas" };
                foreach (var canvasName in canvases)
                {
                    var canvas = this.FindControl<Canvas>(canvasName);
                    if (canvas != null)
                    {
                        canvas.Children.Clear();
                        
                        // Add error text
                        var errorText = new TextBlock
                        {
                            Text = errorMessage,
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Color.Parse("#F38BA8")),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };
                        
                        Canvas.SetLeft(errorText, canvas.Width / 2 - 50);
                        Canvas.SetTop(errorText, canvas.Height / 2 - 10);
                        canvas.Children.Add(errorText);
                    }
                }
                
                // Update overview cards with error state
                UpdateOverviewCardsErrorState();
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[STATISTICS] Error showing error state: {ex.Message}", "", "Error", "SYSTEM");
            }
        }
        
        private void UpdateOverviewCardsErrorState()
        {
            try
            {
                this.FindControl<TextBlock>("TxtTotalBackups")!.Text = "Error";
                this.FindControl<TextBlock>("TxtSuccessRate")!.Text = "Error";
                this.FindControl<TextBlock>("TxtAvgDuration")!.Text = "Error";
                this.FindControl<TextBlock>("TxtStorageUsed")!.Text = "Error";
                
                this.FindControl<TextBlock>("TxtBackupsTrend")!.Text = "N/A";
                this.FindControl<TextBlock>("TxtSuccessTrend")!.Text = "N/A";
                this.FindControl<TextBlock>("TxtDurationTrend")!.Text = "N/A";
                this.FindControl<TextBlock>("TxtStorageTrend")!.Text = "N/A";
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[STATISTICS] Error updating overview cards error state: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private void UpdateBackupVolumeChart()
        {
            var canvas = this.FindControl<Canvas>("BackupVolumeCanvas");
            if (canvas == null) return;
            
            canvas.Children.Clear();
            
            // Group statistics by date and service
            var groupedData = _statistics
                .GroupBy(s => new { s.Date, s.Service })
                .Select(g => new
                {
                    Date = g.Key.Date,
                    Service = g.Key.Service,
                    Count = g.Count()
                })
                .ToList();
            
            // Create simple bar chart with safe calculations
            var width = canvas.Width > 0 ? canvas.Width : 400;
            var height = canvas.Height > 0 ? canvas.Height : 250;
            var barWidth = Math.Max(2, (width - 40) / Math.Max(1, _dateRangeDays));
            var maxCount = groupedData.Any() ? groupedData.Max(g => g.Count) : 1;
            
            var serviceColors = new Dictionary<string, Color>
            {
                ["FTP"] = Color.Parse("#588157"),
                ["Mailchimp"] = Color.Parse("#00b4d8"),
                ["SQL"] = Color.Parse("#fad643")
            };
            
            for (int i = 0; i < _dateRangeDays; i++)
            {
                var date = _dateRangeStart.AddDays(i);
                var x = 20 + i * barWidth;
                
                foreach (var service in new[] { "FTP", "Mailchimp", "SQL" })
                {
                    var count = groupedData.FirstOrDefault(g => g.Date == date && g.Service == service)?.Count ?? 0;
                    if (count > 0)
                    {
                        var barHeight = (count * (height - 40)) / maxCount;
                        var y = height - 20 - barHeight;
                        
                        var rect = new Rectangle
                        {
                            Width = barWidth - 1,
                            Height = barHeight,
                            Fill = new SolidColorBrush(serviceColors[service])
                        };
                        
                        Canvas.SetLeft(rect, x);
                        Canvas.SetTop(rect, y);
                        canvas.Children.Add(rect);
                    }
                }
            }
        }

        private void UpdateSuccessRateChart()
        {
            var canvas = this.FindControl<Canvas>("SuccessRateCanvas");
            if (canvas == null) return;
            
            canvas.Children.Clear();
            
            // Group by date
            var dailyData = _statistics
                .GroupBy(s => s.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Total = g.Count(),
                    Success = g.Count(s => s.Success),
                    Failed = g.Count(s => !s.Success)
                })
                .OrderBy(g => g.Date)
                .ToList();
            
            if (!dailyData.Any()) return;
            
            var width = canvas.Width > 0 ? canvas.Width : 400;
            var height = canvas.Height > 0 ? canvas.Height : 250;
            var pointSpacing = Math.Max(5, (width - 40) / Math.Max(1, dailyData.Count));
            
            // Draw success rate line
            var successPoints = new List<Avalonia.Point>();
            var failedPoints = new List<Avalonia.Point>();
            
            for (int i = 0; i < dailyData.Count; i++)
            {
                var data = dailyData[i];
                var x = 20 + i * pointSpacing;
                
                var successRate = data.Total > 0 ? (data.Success * 100.0 / data.Total) : 0;
                var failedRate = data.Total > 0 ? (data.Failed * 100.0 / data.Total) : 0;
                
                successPoints.Add(new Avalonia.Point(x, height - 20 - (successRate * (height - 40) / 100)));
                failedPoints.Add(new Avalonia.Point(x, height - 20 - (failedRate * (height - 40) / 100)));
            }
            
            // Draw lines
            DrawLine(canvas, successPoints, Color.Parse("#A6E3A1"));
            DrawLine(canvas, failedPoints, Color.Parse("#F38BA8"));
        }

        private void UpdateStorageGrowthChart()
        {
            var canvas = this.FindControl<Canvas>("StorageGrowthCanvas");
            if (canvas == null) return;
            
            canvas.Children.Clear();
            
            // Group by date and accumulate storage
            var dailyStorage = _statistics
                .GroupBy(s => s.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Storage = g.Sum(s => s.StorageSize)
                })
                .OrderBy(g => g.Date)
                .ToList();
            
            if (!dailyStorage.Any()) return;
            
            var width = canvas.Width > 0 ? canvas.Width : 400;
            var height = canvas.Height > 0 ? canvas.Height : 250;
            var pointSpacing = Math.Max(5, (width - 40) / Math.Max(1, dailyStorage.Count));
            var maxStorage = Math.Max(1, dailyStorage.Max(g => g.Storage));
            
            var points = new List<Avalonia.Point>();
            
            for (int i = 0; i < dailyStorage.Count; i++)
            {
                var data = dailyStorage[i];
                var x = 20 + i * pointSpacing;
                var y = height - 20 - (data.Storage * (height - 40) / maxStorage);
                
                points.Add(new Avalonia.Point(x, y));
            }
            
            DrawLine(canvas, points, Color.Parse("#fad643"));
        }

        private void UpdatePerformanceChart()
        {
            var canvas = this.FindControl<Canvas>("PerformanceCanvas");
            if (canvas == null) return;
            
            canvas.Children.Clear();
            
            // Group by date
            var dailyData = _statistics
                .GroupBy(s => s.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    AvgDuration = TimeSpan.FromTicks((long)g.Average(s => s.Duration.Ticks)),
                    MaxDuration = g.Max(s => s.Duration)
                })
                .OrderBy(g => g.Date)
                .ToList();
            
            if (!dailyData.Any()) return;
            
            var width = canvas.Width > 0 ? canvas.Width : 400;
            var height = canvas.Height > 0 ? canvas.Height : 250;
            var pointSpacing = Math.Max(5, (width - 40) / Math.Max(1, dailyData.Count));
            var maxDuration = Math.Max(1, dailyData.Max(g => Math.Max(g.AvgDuration.TotalSeconds, g.MaxDuration.TotalSeconds)));
            
            var avgPoints = new List<Avalonia.Point>();
            var maxPoints = new List<Avalonia.Point>();
            
            for (int i = 0; i < dailyData.Count; i++)
            {
                var data = dailyData[i];
                var x = 20 + i * pointSpacing;
                
                var avgY = height - 20 - (data.AvgDuration.TotalSeconds * (height - 40) / maxDuration);
                var maxY = height - 20 - (data.MaxDuration.TotalSeconds * (height - 40) / maxDuration);
                
                avgPoints.Add(new Avalonia.Point(x, avgY));
                maxPoints.Add(new Avalonia.Point(x, maxY));
            }
            
            DrawLine(canvas, avgPoints, Color.Parse("#588157"));
            DrawLine(canvas, maxPoints, Color.Parse("#00b4d8"));
        }

        private void DrawLine(Canvas canvas, List<Avalonia.Point> points, Color color)
        {
            if (points.Count < 2) return;
            
            for (int i = 0; i < points.Count - 1; i++)
            {
                var line = new Line
                {
                    StartPoint = points[i],
                    EndPoint = points[i + 1],
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 2
                };
                
                canvas.Children.Add(line);
                
                // Add point
                var ellipse = new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = new SolidColorBrush(color)
                };
                
                Canvas.SetLeft(ellipse, points[i].X - 2);
                Canvas.SetTop(ellipse, points[i].Y - 2);
                canvas.Children.Add(ellipse);
            }
            
            // Add last point
            var lastEllipse = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = new SolidColorBrush(color)
            };
            
            var lastPoint = points.Last();
            Canvas.SetLeft(lastEllipse, lastPoint.X - 2);
            Canvas.SetTop(lastEllipse, lastPoint.Y - 2);
            canvas.Children.Add(lastEllipse);
        }

        private void UpdateServiceBreakdown()
        {
            var serviceStats = _statistics
                .GroupBy(s => s.Service)
                .Select(g => new
                {
                    Service = g.Key,
                    Total = g.Count(),
                    Success = g.Count(s => s.Success),
                    AvgDuration = TimeSpan.FromTicks((long)g.Average(s => s.Duration.Ticks)),
                    Storage = g.Sum(s => s.StorageSize)
                })
                .ToList();
            
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var stat in serviceStats)
                {
                    var successRate = stat.Total > 0 ? (stat.Success * 100.0 / stat.Total) : 100.0;
                    
                    switch (stat.Service)
                    {
                        case "FTP":
                            var txtFtpTotal = this.FindControl<TextBlock>("TxtFtpTotal");
                            var txtFtpSuccess = this.FindControl<TextBlock>("TxtFtpSuccess");
                            var txtFtpDuration = this.FindControl<TextBlock>("TxtFtpDuration");
                            var txtFtpStorage = this.FindControl<TextBlock>("TxtFtpStorage");
                            
                            if (txtFtpTotal != null) txtFtpTotal.Text = stat.Total.ToString();
                            if (txtFtpSuccess != null) txtFtpSuccess.Text = $"{successRate:F1}%";
                            if (txtFtpDuration != null) txtFtpDuration.Text = FormatDuration(stat.AvgDuration);
                            if (txtFtpStorage != null) txtFtpStorage.Text = FormatBytes(stat.Storage);
                            break;
                            
                        case "Mailchimp":
                            var txtMcTotal = this.FindControl<TextBlock>("TxtMcTotal");
                            var txtMcSuccess = this.FindControl<TextBlock>("TxtMcSuccess");
                            var txtMcDuration = this.FindControl<TextBlock>("TxtMcDuration");
                            var txtMcStorage = this.FindControl<TextBlock>("TxtMcStorage");
                            
                            if (txtMcTotal != null) txtMcTotal.Text = stat.Total.ToString();
                            if (txtMcSuccess != null) txtMcSuccess.Text = $"{successRate:F1}%";
                            if (txtMcDuration != null) txtMcDuration.Text = FormatDuration(stat.AvgDuration);
                            if (txtMcStorage != null) txtMcStorage.Text = FormatBytes(stat.Storage);
                            break;
                            
                        case "SQL":
                            var txtSqlTotal = this.FindControl<TextBlock>("TxtSqlTotal");
                            var txtSqlSuccess = this.FindControl<TextBlock>("TxtSqlSuccess");
                            var txtSqlDuration = this.FindControl<TextBlock>("TxtSqlDuration");
                            var txtSqlStorage = this.FindControl<TextBlock>("TxtSqlStorage");
                            
                            if (txtSqlTotal != null) txtSqlTotal.Text = stat.Total.ToString();
                            if (txtSqlSuccess != null) txtSqlSuccess.Text = $"{successRate:F1}%";
                            if (txtSqlDuration != null) txtSqlDuration.Text = FormatDuration(stat.AvgDuration);
                            if (txtSqlStorage != null) txtSqlStorage.Text = FormatBytes(stat.Storage);
                            break;
                    }
                }
            });
        }

        private void ShowDateRangeDialog()
        {
            // Simple date range selection
            var ranges = new[] { "7 Days", "30 Days", "90 Days", "6 Months", "1 Year" };
            var days = new[] { 7, 30, 90, 180, 365 };
            
            var currentIndex = Array.IndexOf(days, _dateRangeDays);
            currentIndex = (currentIndex + 1) % days.Length;
            
            _dateRangeDays = days[currentIndex];
            _dateRangeStart = _dateRangeEnd.AddDays(-_dateRangeDays);
            
            this.FindControl<Button>("BtnDateRange")!.Content = ranges[currentIndex];
            
            _ = RefreshStatisticsAsync();
        }

        private async Task ExportStatisticsAsync()
        {
            try
            {
                var reportsFolder = System.IO.Path.Combine(BackupConfig.FtpLocalFolder, "Reports");
                Directory.CreateDirectory(reportsFolder);
                
                var fileName = $"statistics_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var filePath = System.IO.Path.Combine(reportsFolder, fileName);
                
                using var writer = new StreamWriter(filePath);
                await writer.WriteLineAsync("Date,Service,Success,Duration (s),Storage (bytes)");
                
                foreach (var stat in _statistics)
                {
                    await writer.WriteLineAsync($"{stat.Date:yyyy-MM-dd},{stat.Service},{stat.Success},{stat.Duration.TotalSeconds},{stat.StorageSize}");
                }
                
                LogService.WriteLiveLog($"[STATISTICS] Report exported to: {filePath}", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Statistics", $"Report exported to {fileName}", "Success");
                
                // Open the file
                System.Diagnostics.Process.Start("notepad.exe", filePath);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[STATISTICS] Error exporting report: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Statistics", $"Error exporting report: {ex.Message}", "Error");
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{duration.Hours}h {duration.Minutes}m";
            else if (duration.TotalMinutes >= 1)
                return $"{duration.Minutes}m {duration.Seconds}s";
            else
                return $"{duration.Seconds}s";
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private static bool TryParseLogLine(string line, out DateTime ts, out string level, out string msg)
        {
            ts = DateTime.MinValue; level = "INFO"; msg = line;
            try
            {
                if (!line.StartsWith("[")) return false;
                var p1 = line.IndexOf(']');
                if (p1 < 0) return false;
                var dateStr = line.Substring(1, p1 - 1);
                if (DateTime.TryParse(dateStr, out ts))
                {
                    var rest = line.Substring(p1 + 1).Trim();
                    var p2 = rest.IndexOf(']');
                    if (p2 > 0)
                    {
                        level = rest.Substring(0, p2);
                        msg = rest.Substring(p2 + 1).Trim();
                    }
                    return true;
                }
            }
            catch { }
            return false;
        }
    }

    public class BackupStatistic
    {
        public DateTime Date { get; set; }
        public string Service { get; set; } = "";
        public bool Success { get; set; }
        public TimeSpan Duration { get; set; }
        public long StorageSize { get; set; }
    }
}
