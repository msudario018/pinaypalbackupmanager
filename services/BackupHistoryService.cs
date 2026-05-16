using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class BackupHistoryService
    {
        private static readonly string HistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PinayPalBackupManager", "backup_history.json");

        private static readonly object _lock = new object();
        private static readonly int MaxHistoryEntries = 500;

        public class BackupHistoryEntry
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
            public string Service { get; set; } = "";
            public string Type { get; set; } = ""; // "Full", "Incremental", "Manual", "Scheduled"
            public string Status { get; set; } = ""; // "Success", "Failed", "InProgress", "Cancelled"
            public TimeSpan Duration { get; set; }
            public long SizeBytes { get; set; }
            public string FilePath { get; set; } = "";
            public string ErrorMessage { get; set; } = "";
            public Dictionary<string, string> Metadata { get; set; } = new();
            public int FilesCount { get; set; }
            public string Checksum { get; set; } = "";
        }

        public class BackupHistoryFilter
        {
            public string? Service { get; set; }
            public string? Status { get; set; }
            public string? Type { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public int? MinDurationSeconds { get; set; }
            public int? MaxDurationSeconds { get; set; }
        }

        public class BackupHistorySummary
        {
            public int TotalBackups { get; set; }
            public int SuccessfulBackups { get; set; }
            public int FailedBackups { get; set; }
            public int InProgressBackups { get; set; }
            public Dictionary<string, int> BackupsByService { get; set; } = new();
            public Dictionary<string, int> BackupsByType { get; set; } = new();
            public Dictionary<string, int> BackupsByStatus { get; set; } = new();
            public DateTime LastBackupTime { get; set; }
            public DateTime LastSuccessfulBackupTime { get; set; }
            public long TotalSizeBytes { get; set; }
            public TimeSpan AverageDuration { get; set; }
            public double SuccessRate { get; set; }
        }

        private static List<BackupHistoryEntry> _history = new();

        public static void Initialize()
        {
            lock (_lock)
            {
                try
                {
                    var directory = Path.GetDirectoryName(HistoryPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    LoadHistory();

                    // Generate sample data if no history exists
                    if (_history.Count == 0)
                    {
                        GenerateSampleData();
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to initialize backup history: {ex.Message}", "Error", "BACKUPHISTORY");
                }
            }
        }

        private static void GenerateSampleData()
        {
            try
            {
                var now = DateTime.UtcNow;
                var random = new Random();

                // Generate sample backup history entries
                for (int i = 0; i < 50; i++)
                {
                    var timestamp = now.AddDays(-random.Next(0, 30)).AddHours(-random.Next(0, 24));
                    var service = new[] { "FTP", "SQL", "Mailchimp" }[random.Next(0, 3)];
                    var type = new[] { "Manual", "Scheduled", "Full", "Incremental" }[random.Next(0, 4)];
                    var status = random.NextDouble() > 0.15 ? "Success" : "Failed"; // 85% success rate
                    
                    var entry = new BackupHistoryEntry
                    {
                        Timestamp = timestamp,
                        Service = service,
                        Type = type,
                        Status = status,
                        Duration = TimeSpan.FromSeconds(random.Next(30, 600)),
                        FilesCount = random.Next(1, 100),
                        Checksum = Guid.NewGuid().ToString("N")[..8]
                    };

                    if (status == "Success")
                    {
                        entry.SizeBytes = random.Next(1024 * 1024, 1024 * 1024 * 100); // 1MB to 100MB
                        entry.FilePath = $"/backups/{service.ToLower()}/{timestamp:yyyy-MM-dd}_backup.zip";
                    }
                    else
                    {
                        entry.ErrorMessage = GetRandomErrorMessage();
                    }

                    _history.Add(entry);
                }

                // Sort by timestamp descending
                _history = _history.OrderByDescending(h => h.Timestamp).ToList();
                TrimHistory();
                SaveHistory();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to generate sample backup history: {ex.Message}", "Warning", "BACKUPHISTORY");
            }
        }

        private static string GetRandomErrorMessage()
        {
            var errors = new[]
            {
                "Connection timeout",
                "Permission denied",
                "Disk space insufficient",
                "Network error",
                "Authentication failed",
                "File not found",
                "Invalid configuration",
                "Service unavailable"
            };
            
            var random = new Random();
            return errors[random.Next(0, errors.Length)];
        }

        public static string RecordBackupStart(string service, string type = "Manual")
        {
            lock (_lock)
            {
                try
                {
                    var entry = new BackupHistoryEntry
                    {
                        Service = service,
                        Type = type,
                        Status = "InProgress",
                        Timestamp = DateTime.UtcNow
                    };

                    _history.Insert(0, entry);
                    TrimHistory();
                    SaveHistory();

                    LogService.WriteSystemLog($"Backup started: {service} ({type})", "Information", "BACKUPHISTORY");
                    return entry.Id;
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to record backup start: {ex.Message}", "Error", "BACKUPHISTORY");
                    return Guid.NewGuid().ToString();
                }
            }
        }

        public static void RecordBackupSuccess(string id, TimeSpan duration, long sizeBytes, string filePath, int filesCount = 0, string checksum = "")
        {
            lock (_lock)
            {
                try
                {
                    var entry = _history.FirstOrDefault(e => e.Id == id);
                    if (entry != null)
                    {
                        entry.Status = "Success";
                        entry.Duration = duration;
                        entry.SizeBytes = sizeBytes;
                        entry.FilePath = filePath;
                        entry.FilesCount = filesCount;
                        entry.Checksum = checksum;

                        SaveHistory();
                        LogService.WriteSystemLog($"Backup completed successfully: {entry.Service} ({duration.TotalSeconds:F2}s, {sizeBytes} bytes)", "Information", "BACKUPHISTORY");

                        // Record performance metrics
                        PerformanceMetricsService.RecordBackupTime(entry.Service, duration);
                        PerformanceMetricsService.RecordBackupSuccess(entry.Service, true);
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to record backup success: {ex.Message}", "Error", "BACKUPHISTORY");
                }
            }
        }

        public static void RecordBackupFailure(string id, TimeSpan duration, string errorMessage)
        {
            lock (_lock)
            {
                try
                {
                    var entry = _history.FirstOrDefault(e => e.Id == id);
                    if (entry != null)
                    {
                        entry.Status = "Failed";
                        entry.Duration = duration;
                        entry.ErrorMessage = errorMessage;

                        SaveHistory();
                        LogService.WriteSystemLog($"Backup failed: {entry.Service} - {errorMessage}", "Error", "BACKUPHISTORY");

                        // Record performance metrics
                        PerformanceMetricsService.RecordBackupTime(entry.Service, duration);
                        PerformanceMetricsService.RecordBackupSuccess(entry.Service, false);
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to record backup failure: {ex.Message}", "Error", "BACKUPHISTORY");
                }
            }
        }

        public static void RecordBackupCancellation(string id, TimeSpan duration)
        {
            lock (_lock)
            {
                try
                {
                    var entry = _history.FirstOrDefault(e => e.Id == id);
                    if (entry != null)
                    {
                        entry.Status = "Cancelled";
                        entry.Duration = duration;

                        SaveHistory();
                        LogService.WriteSystemLog($"Backup cancelled: {entry.Service}", "Warning", "BACKUPHISTORY");
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to record backup cancellation: {ex.Message}", "Error", "BACKUPHISTORY");
                }
            }
        }

        public static List<BackupHistoryEntry> GetHistory(int limit = 100)
        {
            lock (_lock)
            {
                return _history.Take(limit).ToList();
            }
        }

        public static List<BackupHistoryEntry> GetHistory(BackupHistoryFilter filter, int limit = 100)
        {
            lock (_lock)
            {
                var query = _history.AsQueryable();

                if (!string.IsNullOrEmpty(filter.Service))
                {
                    query = query.Where(e => e.Service.Equals(filter.Service, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrEmpty(filter.Status))
                {
                    query = query.Where(e => e.Status.Equals(filter.Status, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrEmpty(filter.Type))
                {
                    query = query.Where(e => e.Type.Equals(filter.Type, StringComparison.OrdinalIgnoreCase));
                }

                if (filter.StartDate.HasValue)
                {
                    query = query.Where(e => e.Timestamp >= filter.StartDate.Value);
                }

                if (filter.EndDate.HasValue)
                {
                    query = query.Where(e => e.Timestamp <= filter.EndDate.Value);
                }

                if (filter.MinDurationSeconds.HasValue)
                {
                    query = query.Where(e => e.Duration.TotalSeconds >= filter.MinDurationSeconds.Value);
                }

                if (filter.MaxDurationSeconds.HasValue)
                {
                    query = query.Where(e => e.Duration.TotalSeconds <= filter.MaxDurationSeconds.Value);
                }

                return query.Take(limit).ToList();
            }
        }

        public static BackupHistoryEntry? GetBackupById(string id)
        {
            lock (_lock)
            {
                return _history.FirstOrDefault(e => e.Id == id);
            }
        }

        public static BackupHistorySummary GetSummary()
        {
            lock (_lock)
            {
                var summary = new BackupHistorySummary
                {
                    TotalBackups = _history.Count,
                    SuccessfulBackups = _history.Count(e => e.Status == "Success"),
                    FailedBackups = _history.Count(e => e.Status == "Failed"),
                    InProgressBackups = _history.Count(e => e.Status == "InProgress")
                };

                // Group by service
                foreach (var entry in _history)
                {
                    if (!summary.BackupsByService.ContainsKey(entry.Service))
                    {
                        summary.BackupsByService[entry.Service] = 0;
                    }
                    summary.BackupsByService[entry.Service]++;

                    if (!summary.BackupsByType.ContainsKey(entry.Type))
                    {
                        summary.BackupsByType[entry.Type] = 0;
                    }
                    summary.BackupsByType[entry.Type]++;

                    if (!summary.BackupsByStatus.ContainsKey(entry.Status))
                    {
                        summary.BackupsByStatus[entry.Status] = 0;
                    }
                    summary.BackupsByStatus[entry.Status]++;
                }

                // Calculate statistics
                if (_history.Count > 0)
                {
                    summary.LastBackupTime = _history[0].Timestamp;
                    summary.LastSuccessfulBackupTime = _history.FirstOrDefault(e => e.Status == "Success")?.Timestamp ?? DateTime.MinValue;
                    summary.TotalSizeBytes = _history.Where(e => e.Status == "Success").Sum(e => e.SizeBytes);
                    
                    var completedBackups = _history.Where(e => e.Status == "Success" || e.Status == "Failed").ToList();
                    if (completedBackups.Count > 0)
                    {
                        summary.AverageDuration = TimeSpan.FromSeconds(completedBackups.Average(e => e.Duration.TotalSeconds));
                    }

                    summary.SuccessRate = summary.TotalBackups > 0 ? (summary.SuccessfulBackups * 100.0 / summary.TotalBackups) : 0;
                }

                return summary;
            }
        }

        public static List<BackupHistoryEntry> GetRecentBackups(string service, int count = 10)
        {
            lock (_lock)
            {
                return _history.Where(e => e.Service.Equals(service, StringComparison.OrdinalIgnoreCase))
                               .Take(count)
                               .ToList();
            }
        }

        public static List<BackupHistoryEntry> GetFailedBackups(int limit = 50)
        {
            lock (_lock)
            {
                return _history.Where(e => e.Status == "Failed").Take(limit).ToList();
            }
        }

        public static void ClearHistory()
        {
            lock (_lock)
            {
                try
                {
                    _history.Clear();
                    SaveHistory();
                    LogService.WriteSystemLog("Backup history cleared", "Information", "BACKUPHISTORY");
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to clear backup history: {ex.Message}", "Error", "BACKUPHISTORY");
                }
            }
        }

        public static void ClearOldHistory(TimeSpan maxAge)
        {
            lock (_lock)
            {
                try
                {
                    var cutoffDate = DateTime.UtcNow - maxAge;
                    var originalCount = _history.Count;
                    _history = _history.Where(e => e.Timestamp > cutoffDate).ToList();
                    var removedCount = originalCount - _history.Count;

                    SaveHistory();
                    LogService.WriteSystemLog($"Cleared {removedCount} old history entries (older than {maxAge.Days} days)", "Information", "BACKUPHISTORY");
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to clear old backup history: {ex.Message}", "Error", "BACKUPHISTORY");
                }
            }
        }

        private static void TrimHistory()
        {
            if (_history.Count > MaxHistoryEntries)
            {
                _history = _history.Take(MaxHistoryEntries).ToList();
            }
        }

        private static void SaveHistory()
        {
            try
            {
                var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(HistoryPath, json);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to save backup history: {ex.Message}", "Error", "BACKUPHISTORY");
            }
        }

        private static void LoadHistory()
        {
            try
            {
                if (!File.Exists(HistoryPath))
                {
                    return;
                }

                var json = File.ReadAllText(HistoryPath);
                var loadedHistory = JsonSerializer.Deserialize<List<BackupHistoryEntry>>(json);
                if (loadedHistory != null)
                {
                    _history = loadedHistory;
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to load backup history: {ex.Message}", "Warning", "BACKUPHISTORY");
            }
        }

        public static string ExportHistoryReport()
        {
            var summary = GetSummary();
            var sb = new StringBuilder();

            sb.AppendLine("=== BACKUP HISTORY REPORT ===");
            sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();

            sb.AppendLine("--- Summary ---");
            sb.AppendLine($"Total Backups: {summary.TotalBackups}");
            sb.AppendLine($"Successful: {summary.SuccessfulBackups}");
            sb.AppendLine($"Failed: {summary.FailedBackups}");
            sb.AppendLine($"In Progress: {summary.InProgressBackups}");
            sb.AppendLine($"Success Rate: {summary.SuccessRate:F1}%");
            sb.AppendLine($"Average Duration: {summary.AverageDuration.TotalSeconds:F2} seconds");
            sb.AppendLine($"Total Size: {FormatBytes(summary.TotalSizeBytes)}");
            sb.AppendLine($"Last Backup: {summary.LastBackupTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Last Successful: {summary.LastSuccessfulBackupTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            sb.AppendLine("--- Backups by Service ---");
            foreach (var kvp in summary.BackupsByService)
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("--- Backups by Type ---");
            foreach (var kvp in summary.BackupsByType)
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("--- Backups by Status ---");
            foreach (var kvp in summary.BackupsByStatus)
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("--- Recent Backups (Last 20) ---");
            foreach (var entry in _history.Take(20))
            {
                sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Service} - {entry.Type} - {entry.Status}");
                if (entry.Status == "Success")
                {
                    sb.AppendLine($"  Duration: {entry.Duration.TotalSeconds:F2}s, Size: {FormatBytes(entry.SizeBytes)}, Files: {entry.FilesCount}");
                }
                else if (entry.Status == "Failed")
                {
                    sb.AppendLine($"  Duration: {entry.Duration.TotalSeconds:F2}s, Error: {entry.ErrorMessage}");
                }
            }

            return sb.ToString();
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
    }
}
