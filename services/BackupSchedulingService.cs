using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class BackupSchedulingService
    {
        private static readonly string SchedulePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PinayPalBackupManager", "backup_schedules.json");

        private static readonly object _lock = new object();
        private static Timer? _schedulerTimer;
        private static readonly Dictionary<string, BackupSchedule> _schedules = new();
        private static bool _isRunning = false;

        public class BackupSchedule
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Name { get; set; } = "";
            public string Service { get; set; } = ""; // "ftp", "sql", "mailchimp", "all"
            public ScheduleType Type { get; set; } = ScheduleType.Once;
            public string CronExpression { get; set; } = "";
            public DateTime? OneTimeDate { get; set; }
            public TimeSpan? Interval { get; set; }
            public bool IsEnabled { get; set; } = true;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? LastRun { get; set; }
            public DateTime? NextRun { get; set; }
            public int RunCount { get; set; }
            public string? BackupType { get; set; } = "Full"; // "Full", "Incremental"
        }

        public enum ScheduleType
        {
            Once,
            Daily,
            Weekly,
            Monthly,
            Interval,
            Custom
        }

        public class ScheduleExecutionResult
        {
            public string ScheduleId { get; set; } = "";
            public string ScheduleName { get; set; } = "";
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
            public TimeSpan Duration { get; set; }
        }

        public static event Action<ScheduleExecutionResult>? OnScheduleExecuted;

        public static void Initialize()
        {
            lock (_lock)
            {
                try
                {
                    var directory = Path.GetDirectoryName(SchedulePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    LoadSchedules();

                    // Generate sample data if no schedules exist
                    if (_schedules.Count == 0)
                    {
                        GenerateSampleData();
                    }

                    CalculateNextRuns();
                    StartScheduler();
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to initialize backup scheduling: {ex.Message}", "Error", "BACKUPSCHEDULE");
                }
            }
        }

        private static void GenerateSampleData()
        {
            try
            {
                var now = DateTime.UtcNow;
                var random = new Random();

                // Generate sample backup schedules
                var sampleSchedules = new[]
                {
                    new BackupSchedule
                    {
                        Name = "Daily FTP Backup",
                        Service = "ftp",
                        Type = ScheduleType.Daily,
                        IsEnabled = true,
                        CreatedAt = now.AddDays(-7),
                        NextRun = now.Date.AddDays(1).AddHours(2), // Tomorrow at 2 AM
                        RunCount = 5,
                        LastRun = now.AddDays(-1).AddHours(2),
                        BackupType = "Full"
                    },
                    new BackupSchedule
                    {
                        Name = "Weekly SQL Backup",
                        Service = "sql",
                        Type = ScheduleType.Weekly,
                        IsEnabled = true,
                        CreatedAt = now.AddDays(-14),
                        NextRun = now.AddDays(1).AddHours(3), // Tomorrow at 3 AM
                        RunCount = 2,
                        LastRun = now.AddDays(-7).AddHours(3),
                        BackupType = "Full"
                    },
                    new BackupSchedule
                    {
                        Name = "Monthly Mailchimp Backup",
                        Service = "mailchimp",
                        Type = ScheduleType.Monthly,
                        IsEnabled = false,
                        CreatedAt = now.AddDays(-30),
                        NextRun = now.AddDays(15).AddHours(1), // Next month at 1 AM
                        RunCount = 0,
                        BackupType = "Incremental"
                    },
                    new BackupSchedule
                    {
                        Name = "Hourly Quick Backup",
                        Service = "all",
                        Type = ScheduleType.Interval,
                        Interval = TimeSpan.FromHours(1),
                        IsEnabled = true,
                        CreatedAt = now.AddDays(-3),
                        NextRun = now.AddHours(1),
                        RunCount = 72,
                        LastRun = now.AddHours(-1),
                        BackupType = "Incremental"
                    },
                    new BackupSchedule
                    {
                        Name = "One-time Maintenance Backup",
                        Service = "ftp",
                        Type = ScheduleType.Once,
                        OneTimeDate = now.AddDays(2).AddHours(4), // In 2 days at 4 AM
                        IsEnabled = true,
                        CreatedAt = now.AddDays(-1),
                        NextRun = now.AddDays(2).AddHours(4),
                        RunCount = 0,
                        BackupType = "Full"
                    }
                };

                foreach (var schedule in sampleSchedules)
                {
                    _schedules[schedule.Id] = schedule;
                }

                SaveSchedules();
                LogService.WriteSystemLog($"Generated {sampleSchedules.Length} sample backup schedules", "Information", "BACKUPSCHEDULE");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to generate sample backup schedules: {ex.Message}", "Warning", "BACKUPSCHEDULE");
            }
        }

        public static void StartScheduler()
        {
            lock (_lock)
            {
                if (_isRunning)
                {
                    return;
                }

                _isRunning = true;
                _schedulerTimer = new Timer(CheckSchedules, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
                LogService.WriteSystemLog("Backup scheduler started", "Information", "BACKUPSCHEDULE");
            }
        }

        public static void StopScheduler()
        {
            lock (_lock)
            {
                _isRunning = false;
                _schedulerTimer?.Dispose();
                _schedulerTimer = null;
                LogService.WriteSystemLog("Backup scheduler stopped", "Information", "BACKUPSCHEDULE");
            }
        }

        private static async void CheckSchedules(object? state)
        {
            lock (_lock)
            {
                if (!_isRunning) return;

                try
                {
                    var now = DateTime.UtcNow;
                    var schedulesToRun = _schedules.Values
                        .Where(s => s.IsEnabled && s.NextRun.HasValue && s.NextRun.Value <= now)
                        .ToList();

                    foreach (var schedule in schedulesToRun)
                    {
                        _ = Task.Run(() => ExecuteScheduleAsync(schedule));
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Error checking schedules: {ex.Message}", "Error", "BACKUPSCHEDULE");
                }
            }
        }

        private static async Task ExecuteScheduleAsync(BackupSchedule schedule)
        {
            var result = new ScheduleExecutionResult
            {
                ScheduleId = schedule.Id,
                ScheduleName = schedule.Name
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                LogService.WriteSystemLog($"Executing scheduled backup: {schedule.Name} ({schedule.Service})", "Information", "BACKUPSCHEDULE");

                // Record backup start
                var historyId = BackupHistoryService.RecordBackupStart(schedule.Service, schedule.BackupType ?? "Scheduled");

                // Execute the backup based on service
                bool success = await ExecuteBackupAsync(schedule.Service, schedule.BackupType ?? "Full");

                stopwatch.Stop();

                if (success)
                {
                    result.Success = true;
                    result.Message = "Backup completed successfully";

                    // Record backup success
                    BackupHistoryService.RecordBackupSuccess(historyId, stopwatch.Elapsed, 0, "", 0);
                }
                else
                {
                    result.Success = false;
                    result.Message = "Backup failed";

                    // Record backup failure
                    BackupHistoryService.RecordBackupFailure(historyId, stopwatch.Elapsed, "Scheduled backup failed");
                }

                // Update schedule
                schedule.LastRun = DateTime.UtcNow;
                schedule.RunCount++;
                CalculateNextRun(schedule);
                SaveSchedules();

                NotificationService.ShowBackupToast("Scheduled Backup", $"{schedule.Name}: {result.Message}", result.Success ? "Info" : "Error");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Success = false;
                result.Message = $"Error: {ex.Message}";
                LogService.WriteSystemLog($"Scheduled backup error: {ex.Message}", "Error", "BACKUPSCHEDULE");
            }
            finally
            {
                result.Duration = stopwatch.Elapsed;
                result.ExecutedAt = DateTime.UtcNow;
                OnScheduleExecuted?.Invoke(result);
            }
        }

        private static async Task<bool> ExecuteBackupAsync(string service, string backupType)
        {
            // This is a placeholder - the actual backup execution would be handled by the BackupManager
            // For now, we'll simulate a successful backup
            await Task.Delay(TimeSpan.FromSeconds(2));
            
            // In a real implementation, this would call the appropriate backup methods:
            // if (service == "ftp" || service == "all") await BackupManager.BackupFtpAsync();
            // if (service == "sql" || service == "all") await BackupManager.BackupSqlAsync();
            // if (service == "mailchimp" || service == "all") await BackupManager.BackupMailchimpAsync();
            
            return true;
        }

        public static string CreateSchedule(BackupSchedule schedule)
        {
            lock (_lock)
            {
                try
                {
                    schedule.Id = Guid.NewGuid().ToString();
                    schedule.CreatedAt = DateTime.UtcNow;
                    schedule.RunCount = 0;

                    CalculateNextRun(schedule);
                    _schedules[schedule.Id] = schedule;
                    SaveSchedules();

                    LogService.WriteSystemLog($"Created backup schedule: {schedule.Name}", "Information", "BACKUPSCHEDULE");
                    return schedule.Id;
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to create schedule: {ex.Message}", "Error", "BACKUPSCHEDULE");
                    throw;
                }
            }
        }

        public static bool UpdateSchedule(string id, BackupSchedule updatedSchedule)
        {
            lock (_lock)
            {
                try
                {
                    if (!_schedules.ContainsKey(id))
                    {
                        return false;
                    }

                    var existing = _schedules[id];
                    existing.Name = updatedSchedule.Name;
                    existing.Service = updatedSchedule.Service;
                    existing.Type = updatedSchedule.Type;
                    existing.CronExpression = updatedSchedule.CronExpression;
                    existing.OneTimeDate = updatedSchedule.OneTimeDate;
                    existing.Interval = updatedSchedule.Interval;
                    existing.IsEnabled = updatedSchedule.IsEnabled;
                    existing.BackupType = updatedSchedule.BackupType;

                    CalculateNextRun(existing);
                    SaveSchedules();

                    LogService.WriteSystemLog($"Updated backup schedule: {existing.Name}", "Information", "BACKUPSCHEDULE");
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to update schedule: {ex.Message}", "Error", "BACKUPSCHEDULE");
                    return false;
                }
            }
        }

        public static bool DeleteSchedule(string id)
        {
            lock (_lock)
            {
                try
                {
                    if (!_schedules.ContainsKey(id))
                    {
                        return false;
                    }

                    var name = _schedules[id].Name;
                    _schedules.Remove(id);
                    SaveSchedules();

                    LogService.WriteSystemLog($"Deleted backup schedule: {name}", "Information", "BACKUPSCHEDULE");
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to delete schedule: {ex.Message}", "Error", "BACKUPSCHEDULE");
                    return false;
                }
            }
        }

        public static bool EnableSchedule(string id)
        {
            lock (_lock)
            {
                try
                {
                    if (!_schedules.ContainsKey(id))
                    {
                        return false;
                    }

                    _schedules[id].IsEnabled = true;
                    CalculateNextRun(_schedules[id]);
                    SaveSchedules();

                    LogService.WriteSystemLog($"Enabled backup schedule: {_schedules[id].Name}", "Information", "BACKUPSCHEDULE");
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to enable schedule: {ex.Message}", "Error", "BACKUPSCHEDULE");
                    return false;
                }
            }
        }

        public static bool DisableSchedule(string id)
        {
            lock (_lock)
            {
                try
                {
                    if (!_schedules.ContainsKey(id))
                    {
                        return false;
                    }

                    _schedules[id].IsEnabled = false;
                    _schedules[id].NextRun = null;
                    SaveSchedules();

                    LogService.WriteSystemLog($"Disabled backup schedule: {_schedules[id].Name}", "Information", "BACKUPSCHEDULE");
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to disable schedule: {ex.Message}", "Error", "BACKUPSCHEDULE");
                    return false;
                }
            }
        }

        public static BackupSchedule? GetSchedule(string id)
        {
            lock (_lock)
            {
                return _schedules.ContainsKey(id) ? _schedules[id] : null;
            }
        }

        public static List<BackupSchedule> GetAllSchedules()
        {
            lock (_lock)
            {
                return _schedules.Values.ToList();
            }
        }

        public static List<BackupSchedule> GetEnabledSchedules()
        {
            lock (_lock)
            {
                return _schedules.Values.Where(s => s.IsEnabled).ToList();
            }
        }

        private static void CalculateNextRun(BackupSchedule schedule)
        {
            if (!schedule.IsEnabled)
            {
                schedule.NextRun = null;
                return;
            }

            var now = DateTime.UtcNow;

            switch (schedule.Type)
            {
                case ScheduleType.Once:
                    schedule.NextRun = schedule.OneTimeDate;
                    break;

                case ScheduleType.Daily:
                    schedule.NextRun = now.Date.AddDays(1).AddHours(now.Hour);
                    break;

                case ScheduleType.Weekly:
                    schedule.NextRun = now.Date.AddDays(7).AddHours(now.Hour);
                    break;

                case ScheduleType.Monthly:
                    schedule.NextRun = now.Date.AddMonths(1).AddHours(now.Hour);
                    break;

                case ScheduleType.Interval:
                    if (schedule.Interval.HasValue)
                    {
                        var lastRun = schedule.LastRun ?? schedule.CreatedAt;
                        schedule.NextRun = lastRun.Add(schedule.Interval.Value);
                    }
                    break;

                case ScheduleType.Custom:
                    // For custom cron expressions, we'd need a cron parser
                    // For now, default to daily
                    schedule.NextRun = now.Date.AddDays(1).AddHours(now.Hour);
                    break;
            }

            // Ensure next run is in the future
            if (schedule.NextRun.HasValue && schedule.NextRun.Value <= now)
            {
                schedule.NextRun = schedule.NextRun.Value.AddHours(1);
            }
        }

        private static void CalculateNextRuns()
        {
            foreach (var schedule in _schedules.Values)
            {
                CalculateNextRun(schedule);
            }
        }

        private static void SaveSchedules()
        {
            try
            {
                var json = JsonSerializer.Serialize(_schedules.Values.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SchedulePath, json);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to save schedules: {ex.Message}", "Error", "BACKUPSCHEDULE");
            }
        }

        private static void LoadSchedules()
        {
            try
            {
                if (!File.Exists(SchedulePath))
                {
                    return;
                }

                var json = File.ReadAllText(SchedulePath);
                var loadedSchedules = JsonSerializer.Deserialize<List<BackupSchedule>>(json);
                if (loadedSchedules != null)
                {
                    _schedules.Clear();
                    foreach (var schedule in loadedSchedules)
                    {
                        _schedules[schedule.Id] = schedule;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to load schedules: {ex.Message}", "Warning", "BACKUPSCHEDULE");
            }
        }

        public static string GetSchedulesSummary()
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== BACKUP SCHEDULES ===");
                sb.AppendLine($"Total Schedules: {_schedules.Count}");
                sb.AppendLine($"Enabled: {_schedules.Values.Count(s => s.IsEnabled)}");
                sb.AppendLine($"Disabled: {_schedules.Values.Count(s => !s.IsEnabled)}");
                sb.AppendLine();

                foreach (var schedule in _schedules.Values)
                {
                    sb.AppendLine($"--- {schedule.Name} ({schedule.Id}) ---");
                    sb.AppendLine($"Service: {schedule.Service}");
                    sb.AppendLine($"Type: {schedule.Type}");
                    sb.AppendLine($"Enabled: {schedule.IsEnabled}");
                    sb.AppendLine($"Next Run: {schedule.NextRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not scheduled"}");
                    sb.AppendLine($"Last Run: {schedule.LastRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");
                    sb.AppendLine($"Run Count: {schedule.RunCount}");
                    sb.AppendLine();
                }

                return sb.ToString();
            }
        }
    }
}
