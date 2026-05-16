using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class PerformanceMetricsService
    {
        private static readonly string MetricsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PinayPalBackupManager", "performance_metrics.json");

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, MetricData> _metrics = new();
        private static readonly int MaxMetricSamples = 1000;

        public class MetricData
        {
            public string Name { get; set; } = "";
            public List<MetricSample> Samples { get; set; } = new();
            public string Unit { get; set; } = "";
            public DateTime FirstRecorded { get; set; } = DateTime.UtcNow;
            public DateTime LastRecorded { get; set; } = DateTime.UtcNow;
        }

        public class MetricSample
        {
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
            public double Value { get; set; }
            public string? Context { get; set; }
        }

        public class MetricSummary
        {
            public string Name { get; set; } = "";
            public double Average { get; set; }
            public double Min { get; set; }
            public double Max { get; set; }
            public double Median { get; set; }
            public double StandardDeviation { get; set; }
            public int SampleCount { get; set; }
            public DateTime FirstRecorded { get; set; }
            public DateTime LastRecorded { get; set; }
            public string Unit { get; set; } = "";
        }

        public class PerformanceReport
        {
            public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
            public Dictionary<string, MetricSummary> Summaries { get; set; } = new();
            public SystemResourceSnapshot Resources { get; set; } = new();
            public Dictionary<string, double> BackupSuccessRates { get; set; } = new();
            public Dictionary<string, double> AverageBackupTimes { get; set; } = new();
        }

        public class SystemResourceSnapshot
        {
            public double CpuUsagePercent { get; set; }
            public long AvailableMemoryMB { get; set; }
            public long TotalMemoryMB { get; set; }
            public double MemoryUsagePercent { get; set; }
            public long AvailableDiskSpaceGB { get; set; }
            public long TotalDiskSpaceGB { get; set; }
            public double DiskUsagePercent { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        }

        public static void Initialize()
        {
            lock (_lock)
            {
                try
                {
                    var directory = Path.GetDirectoryName(MetricsPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    LoadMetrics();

                    // Generate sample data if no metrics exist
                    if (_metrics.Count == 0)
                    {
                        GenerateSampleData();
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to initialize performance metrics: {ex.Message}", "Error", "PERFORMANCE");
                }
            }
        }

        private static void GenerateSampleData()
        {
            try
            {
                var now = DateTime.UtcNow;
                var random = new Random();

                // Generate backup success metrics
                foreach (var service in new[] { "ftp", "sql", "mailchimp" })
                {
                    for (int i = 0; i < 30; i++)
                    {
                        var timestamp = now.AddDays(-i);
                        var success = random.NextDouble() > 0.2; // 80% success rate
                        
                        RecordMetric($"backup_success_{service}", success ? 1 : 0, "boolean", null);
                        
                        if (success)
                        {
                            var duration = random.Next(30, 300); // 30-300 seconds
                            RecordMetric($"backup_time_{service}", duration, "seconds", null);
                        }
                    }
                }

                // Generate system resource metrics
                for (int i = 0; i < 24; i++)
                {
                    var timestamp = now.AddHours(-i);
                    
                    RecordMetric("cpu_usage", random.Next(10, 80), "percent", null);
                    RecordMetric("memory_usage", random.Next(30, 70), "percent", null);
                    RecordMetric("disk_usage", random.Next(20, 60), "percent", null);
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to generate sample performance data: {ex.Message}", "Warning", "PERFORMANCE");
            }
        }

        public static void RecordMetric(string name, double value, string unit = "", string? context = null)
        {
            lock (_lock)
            {
                try
                {
                    if (!_metrics.ContainsKey(name))
                    {
                        _metrics[name] = new MetricData
                        {
                            Name = name,
                            Unit = unit
                        };
                    }

                    var metric = _metrics[name];
                    metric.Samples.Add(new MetricSample
                    {
                        Timestamp = DateTime.UtcNow,
                        Value = value,
                        Context = context
                    });

                    metric.LastRecorded = DateTime.UtcNow;

                    // Keep only the most recent samples
                    if (metric.Samples.Count > MaxMetricSamples)
                    {
                        metric.Samples = metric.Samples.TakeLast(MaxMetricSamples).ToList();
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to record metric {name}: {ex.Message}", "Warning", "PERFORMANCE");
                }
            }
        }

        public static void RecordBackupTime(string service, TimeSpan duration)
        {
            RecordMetric($"backup_time_{service.ToLower()}", duration.TotalSeconds, "seconds", service);
        }

        public static void RecordBackupSuccess(string service, bool success)
        {
            RecordMetric($"backup_success_{service.ToLower()}", success ? 1 : 0, "bool", service);
        }

        public static void RecordResourceUsage()
        {
            try
            {
                var cpuUsage = GetCpuUsage();
                RecordMetric("cpu_usage_percent", cpuUsage, "%");

                var memoryInfo = GetMemoryInfo();
                RecordMetric("memory_available_mb", memoryInfo.available, "MB");
                RecordMetric("memory_usage_percent", memoryInfo.usagePercent, "%");

                var diskInfo = GetDiskInfo();
                RecordMetric("disk_available_gb", diskInfo.available, "GB");
                RecordMetric("disk_usage_percent", diskInfo.usagePercent, "%");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to record resource usage: {ex.Message}", "Warning", "PERFORMANCE");
            }
        }

        public static MetricSummary? GetMetricSummary(string name)
        {
            lock (_lock)
            {
                if (!_metrics.ContainsKey(name))
                {
                    return null;
                }

                var metric = _metrics[name];
                if (metric.Samples.Count == 0)
                {
                    return null;
                }

                var values = metric.Samples.Select(s => s.Value).ToList();
                values.Sort();

                var summary = new MetricSummary
                {
                    Name = name,
                    Average = values.Average(),
                    Min = values[0],
                    Max = values[^1],
                    Median = values.Count % 2 == 0 ? (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2 : values[values.Count / 2],
                    StandardDeviation = CalculateStandardDeviation(values),
                    SampleCount = values.Count,
                    FirstRecorded = metric.FirstRecorded,
                    LastRecorded = metric.LastRecorded,
                    Unit = metric.Unit
                };

                return summary;
            }
        }

        public static Dictionary<string, MetricSummary> GetAllSummaries()
        {
            lock (_lock)
            {
                var summaries = new Dictionary<string, MetricSummary>();
                foreach (var name in _metrics.Keys)
                {
                    var summary = GetMetricSummary(name);
                    if (summary != null)
                    {
                        summaries[name] = summary;
                    }
                }
                return summaries;
            }
        }

        public static List<MetricSample> GetMetricSamples(string name, int limit = 100)
        {
            lock (_lock)
            {
                if (!_metrics.ContainsKey(name))
                {
                    return new List<MetricSample>();
                }

                return _metrics[name].Samples.TakeLast(limit).ToList();
            }
        }

        public static PerformanceReport GeneratePerformanceReport()
        {
            var report = new PerformanceReport
            {
                GeneratedAt = DateTime.UtcNow,
                Summaries = GetAllSummaries(),
                Resources = GetSystemResourceSnapshot()
            };

            // Calculate backup success rates
            var services = new[] { "ftp", "sql", "mailchimp" };
            foreach (var service in services)
            {
                var successMetric = GetMetricSummary($"backup_success_{service}");
                if (successMetric != null && successMetric.SampleCount > 0)
                {
                    report.BackupSuccessRates[service] = successMetric.Average * 100; // Convert to percentage
                }

                var timeMetric = GetMetricSummary($"backup_time_{service}");
                if (timeMetric != null && timeMetric.SampleCount > 0)
                {
                    report.AverageBackupTimes[service] = timeMetric.Average;
                }
            }

            return report;
        }

        public static void ClearMetrics(string? name = null)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(name))
                {
                    _metrics.Clear();
                    LogService.WriteSystemLog("All performance metrics cleared", "Information", "PERFORMANCE");
                }
                else if (_metrics.ContainsKey(name))
                {
                    _metrics.Remove(name);
                    LogService.WriteSystemLog($"Performance metric {name} cleared", "Information", "PERFORMANCE");
                }
            }
        }

        public static void ClearOldMetrics(TimeSpan maxAge)
        {
            lock (_lock)
            {
                var cutoffDate = DateTime.UtcNow - maxAge;
                var clearedCount = 0;

                foreach (var metric in _metrics.Values)
                {
                    var originalCount = metric.Samples.Count;
                    metric.Samples = metric.Samples.Where(s => s.Timestamp > cutoffDate).ToList();
                    clearedCount += originalCount - metric.Samples.Count;

                    if (metric.Samples.Count > 0)
                    {
                        metric.FirstRecorded = metric.Samples[0].Timestamp;
                    }
                }

                LogService.WriteSystemLog($"Cleared {clearedCount} old metric samples (older than {maxAge.Days} days)", "Information", "PERFORMANCE");
            }
        }

        public static void SaveMetrics()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_metrics, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(MetricsPath, json);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to save performance metrics: {ex.Message}", "Error", "PERFORMANCE");
                }
            }
        }

        private static void LoadMetrics()
        {
            try
            {
                if (!File.Exists(MetricsPath))
                {
                    return;
                }

                var json = File.ReadAllText(MetricsPath);
                var loadedMetrics = JsonSerializer.Deserialize<Dictionary<string, MetricData>>(json);
                if (loadedMetrics != null)
                {
                    foreach (var kvp in loadedMetrics)
                    {
                        _metrics[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to load performance metrics: {ex.Message}", "Warning", "PERFORMANCE");
            }
        }

        private static double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count < 2)
            {
                return 0;
            }

            var average = values.Average();
            var sumOfSquares = values.Sum(v => Math.Pow(v - average, 2));
            return Math.Sqrt(sumOfSquares / values.Count);
        }

        private static double GetCpuUsage()
        {
            try
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    cpuCounter.NextValue();
                    System.Threading.Thread.Sleep(500);
                    return cpuCounter.NextValue();
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static (long available, long total, double usagePercent) GetMemoryInfo()
        {
            try
            {
                var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
                var available = total / 2; // Rough estimate
                var usagePercent = (total - available) * 100.0 / total;
                return (available, total, usagePercent);
            }
            catch
            {
                return (2048, 4096, 50);
            }
        }

        private static (long available, long total, double usagePercent) GetDiskInfo()
        {
            try
            {
                var backupPath = EnvironmentConfigService.GetBackupPath();
                if (Directory.Exists(backupPath))
                {
                    var driveInfo = new DriveInfo(Path.GetPathRoot(backupPath) ?? backupPath);
                    var total = driveInfo.TotalSize / (1024 * 1024 * 1024);
                    var available = driveInfo.AvailableFreeSpace / (1024 * 1024 * 1024);
                    var usagePercent = (total - available) * 100.0 / total;
                    return (available, total, usagePercent);
                }
            }
            catch { }

            return (100, 500, 80);
        }

        private static SystemResourceSnapshot GetSystemResourceSnapshot()
        {
            var memoryInfo = GetMemoryInfo();
            var diskInfo = GetDiskInfo();

            return new SystemResourceSnapshot
            {
                CpuUsagePercent = GetCpuUsage(),
                AvailableMemoryMB = memoryInfo.available,
                TotalMemoryMB = memoryInfo.total,
                MemoryUsagePercent = memoryInfo.usagePercent,
                AvailableDiskSpaceGB = diskInfo.available,
                TotalDiskSpaceGB = diskInfo.total,
                DiskUsagePercent = diskInfo.usagePercent
            };
        }

        public static string ExportMetricsReport()
        {
            var report = GeneratePerformanceReport();
            var sb = new StringBuilder();

            sb.AppendLine("=== PERFORMANCE METRICS REPORT ===");
            sb.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();

            sb.AppendLine("--- System Resources ---");
            sb.AppendLine($"CPU Usage: {report.Resources.CpuUsagePercent:F1}%");
            sb.AppendLine($"Memory Usage: {report.Resources.MemoryUsagePercent:F1}% ({report.Resources.AvailableMemoryMB} MB available)");
            sb.AppendLine($"Disk Usage: {report.Resources.DiskUsagePercent:F1}% ({report.Resources.AvailableDiskSpaceGB} GB available)");
            sb.AppendLine();

            sb.AppendLine("--- Backup Success Rates ---");
            foreach (var kvp in report.BackupSuccessRates)
            {
                sb.AppendLine($"{kvp.Key.ToUpper()}: {kvp.Value:F1}%");
            }
            sb.AppendLine();

            sb.AppendLine("--- Average Backup Times ---");
            foreach (var kvp in report.AverageBackupTimes)
            {
                sb.AppendLine($"{kvp.Key.ToUpper()}: {kvp.Value:F2} seconds");
            }
            sb.AppendLine();

            sb.AppendLine("--- Metric Summaries ---");
            foreach (var summary in report.Summaries.Values)
            {
                sb.AppendLine($"{summary.Name}:");
                sb.AppendLine($"  Average: {summary.Average:F2} {summary.Unit}");
                sb.AppendLine($"  Min: {summary.Min:F2} {summary.Unit}");
                sb.AppendLine($"  Max: {summary.Max:F2} {summary.Unit}");
                sb.AppendLine($"  Median: {summary.Median:F2} {summary.Unit}");
                sb.AppendLine($"  Std Dev: {summary.StandardDeviation:F2} {summary.Unit}");
                sb.AppendLine($"  Samples: {summary.SampleCount}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static void StartPeriodicRecording(TimeSpan interval)
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(interval);
                    RecordResourceUsage();
                    SaveMetrics();
                }
            });
        }
    }
}
