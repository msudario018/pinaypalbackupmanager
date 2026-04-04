using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class SecurityAuditService
    {
        private static readonly string AccessLogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinayPalBackupManager", "access.log");

        private static readonly string CredentialFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinayPalBackupManager", "credentials.json");

        public class AccessLogEntry
        {
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public string Action { get; set; } = "";
            public string Service { get; set; } = "";
            public string User { get; set; } = "";
            public string Details { get; set; } = "";
            public string Source { get; set; } = "Manual"; // "Manual", "Auto", "System"
        }

        public class CredentialRecord
        {
            public string Service { get; set; } = "";
            public string LastUpdated { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            public string LastUpdatedBy { get; set; } = "";
            public int RotationDays { get; set; } = 90;
            public bool RotationEnabled { get; set; } = true;
            public DateTime? LastRotationReminder { get; set; }
            public DateTime NextRotationDue => DateTime.Parse(LastUpdated).AddDays(RotationDays);
            public int DaysUntilRotation => (int)(NextRotationDue - DateTime.Now).TotalDays;
            public bool IsOverdue => DaysUntilRotation < 0;
            public bool IsDueSoon => DaysUntilRotation <= 7;
        }

        public class SecurityStatus
        {
            public List<CredentialRecord> Credentials { get; set; } = new();
            public List<CredentialRecord> OverdueCredentials { get; set; } = new();
            public List<CredentialRecord> DueSoonCredentials { get; set; } = new();
            public int RecentAccessCount { get; set; }
            public DateTime LastAccess { get; set; }
            public List<AccessLogEntry> SuspiciousActivity { get; set; } = new();
        }

        public static void LogAccess(string action, string service, string details = "", string source = "Manual")
        {
            var entry = new AccessLogEntry
            {
                Timestamp = DateTime.Now,
                Action = action,
                Service = service,
                User = Environment.UserName,
                Details = details,
                Source = source
            };

            try
            {
                var logLine = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Source} | {entry.User} | {entry.Service} | {entry.Action} | {details}{Environment.NewLine}";
                File.AppendAllText(AccessLogFile, logLine);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[AUDIT] Failed to log access: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        public static async Task UpdateCredentialRecordAsync(string service, string updatedBy = "")
        {
            var credentials = LoadCredentials();
            var record = credentials.FirstOrDefault(c => c.Service.Equals(service, StringComparison.OrdinalIgnoreCase));

            if (record == null)
            {
                record = new CredentialRecord { Service = service };
                credentials.Add(record);
            }

            record.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            record.LastUpdatedBy = string.IsNullOrEmpty(updatedBy) ? Environment.UserName : updatedBy;
            record.LastRotationReminder = null; // Reset reminder

            await SaveCredentialsAsync(credentials);
            LogAccess("Credential Updated", service, $"Updated by {record.LastUpdatedBy}", "System");
        }

        public static async Task<List<CredentialRecord>> GetCredentialRotationRemindersAsync()
        {
            var credentials = LoadCredentials();
            var reminders = new List<CredentialRecord>();

            foreach (var record in credentials.Where(c => c.RotationEnabled))
            {
                if (record.IsOverdue || (record.IsDueSoon && 
                    (!record.LastRotationReminder.HasValue || 
                     record.LastRotationReminder.Value < DateTime.Now.AddDays(-1))))
                {
                    reminders.Add(record);
                    record.LastRotationReminder = DateTime.Now;
                }
            }

            if (reminders.Any())
            {
                await SaveCredentialsAsync(credentials);
            }

            return reminders;
        }

        public static SecurityStatus GetSecurityStatus()
        {
            var credentials = LoadCredentials();
            var recentAccess = GetRecentAccessLogs(TimeSpan.FromDays(7));
            var suspiciousActivity = DetectSuspiciousActivity(recentAccess);

            return new SecurityStatus
            {
                Credentials = credentials,
                OverdueCredentials = credentials.Where(c => c.IsOverdue).ToList(),
                DueSoonCredentials = credentials.Where(c => c.IsDueSoon && !c.IsOverdue).ToList(),
                RecentAccessCount = recentAccess.Count,
                LastAccess = recentAccess.Any() ? recentAccess.Max(l => l.Timestamp) : DateTime.MinValue,
                SuspiciousActivity = suspiciousActivity
            };
        }

        public static async Task SetCredentialRotationAsync(string service, int days, bool enabled)
        {
            var credentials = LoadCredentials();
            var record = credentials.FirstOrDefault(c => c.Service.Equals(service, StringComparison.OrdinalIgnoreCase));

            if (record != null)
            {
                record.RotationDays = days;
                record.RotationEnabled = enabled;
                await SaveCredentialsAsync(credentials);
                LogAccess("Rotation Policy Updated", service, $"Rotation: {(enabled ? $"{days} days" : "disabled")}", "System");
            }
            else
            {
                throw new ArgumentException($"Credential record not found for service: {service}");
            }
        }

        public static async Task MarkCredentialRotatedAsync(string service)
        {
            var credentials = LoadCredentials();
            var record = credentials.FirstOrDefault(c => c.Service.Equals(service, StringComparison.OrdinalIgnoreCase));

            if (record != null)
            {
                record.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                record.LastUpdatedBy = Environment.UserName;
                record.LastRotationReminder = null;
                await SaveCredentialsAsync(credentials);
                LogAccess("Credential Rotated", service, "Manual rotation completed", "System");
            }
            else
            {
                throw new ArgumentException($"Credential record not found for service: {service}");
            }
        }

        public static List<AccessLogEntry> GetRecentAccessLogs(TimeSpan period)
        {
            try
            {
                if (!File.Exists(AccessLogFile))
                    return new List<AccessLogEntry>();

                var cutoff = DateTime.Now - period;
                var lines = File.ReadAllLines(AccessLogFile);
                var entries = new List<AccessLogEntry>();

                foreach (var line in lines.Reverse()) // Start from newest
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(line, 
                            @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\] (\w+) \| ([^|]+) \| ([^|]+) \| ([^|]+) \| (.*)");
                        
                        if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var timestamp))
                        {
                            if (timestamp < cutoff) break; // Stop when we reach entries older than cutoff

                            entries.Add(new AccessLogEntry
                            {
                                Timestamp = timestamp,
                                Source = match.Groups[2].Value,
                                User = match.Groups[3].Value,
                                Service = match.Groups[4].Value,
                                Action = match.Groups[5].Value,
                                Details = match.Groups[6].Value
                            });
                        }
                    }
                    catch
                    {
                        // Skip malformed log lines
                        continue;
                    }
                }

                return entries.OrderByDescending(e => e.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[AUDIT] Error reading access logs: {ex.Message}", "", "Error", "SYSTEM");
                return new List<AccessLogEntry>();
            }
        }

        private static List<AccessLogEntry> DetectSuspiciousActivity(List<AccessLogEntry> logs)
        {
            var suspicious = new List<AccessLogEntry>();
            var now = DateTime.Now;

            // Check for multiple failed login attempts
            var failedLogins = logs.Where(l => l.Action.Contains("FAILED") || l.Action.Contains("ERROR")).ToList();
            var groupedFailures = failedLogins.GroupBy(l => l.User + "|" + l.Service).ToList();

            foreach (var group in groupedFailures.Where(g => g.Count() >= 3))
            {
                var timeSpan = group.Max(l => l.Timestamp) - group.Min(l => l.Timestamp);
                if (timeSpan.TotalHours < 1) // Multiple failures within 1 hour
                {
                    suspicious.AddRange(group);
                }
            }

            // Check for access at unusual hours (e.g., 2AM-5AM)
            var unusualHours = logs.Where(l => l.Timestamp.Hour >= 2 && l.Timestamp.Hour <= 5 && l.Source == "Manual").ToList();
            suspicious.AddRange(unusualHours);

            // Check for access from different users on same service
            var serviceUsers = logs.GroupBy(l => l.Service).ToList();
            foreach (var service in serviceUsers)
            {
                var uniqueUsers = service.Select(l => l.User).Distinct().Count();
                if (uniqueUsers > 1)
                {
                    suspicious.AddRange(service.Where(l => l.Source == "Manual"));
                }
            }

            return suspicious.Distinct().OrderByDescending(l => l.Timestamp).Take(10).ToList();
        }

        private static List<CredentialRecord> LoadCredentials()
        {
            try
            {
                if (!File.Exists(CredentialFile))
                {
                    // Initialize with default services
                    var defaults = new List<CredentialRecord>
                    {
                        new CredentialRecord { Service = "FTP", LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                        new CredentialRecord { Service = "SQL", LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                        new CredentialRecord { Service = "Mailchimp", LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                    };
                    _ = Task.Run(async () => await SaveCredentialsAsync(defaults));
                    return defaults;
                }

                var json = File.ReadAllText(CredentialFile);
                return JsonSerializer.Deserialize<List<CredentialRecord>>(json) ?? new List<CredentialRecord>();
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[AUDIT] Error loading credentials: {ex.Message}", "", "Error", "SYSTEM");
                return new List<CredentialRecord>();
            }
        }

        private static async Task SaveCredentialsAsync(List<CredentialRecord> credentials)
        {
            try
            {
                var directory = Path.GetDirectoryName(CredentialFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(CredentialFile, json);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[AUDIT] Error saving credentials: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        public static async Task CleanupOldAccessLogsAsync(int daysToKeep = 90)
        {
            try
            {
                if (!File.Exists(AccessLogFile)) return;

                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var lines = File.ReadAllLines(AccessLogFile);
                var filteredLines = new List<string>();

                foreach (var line in lines)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]");
                    if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var timestamp))
                    {
                        if (timestamp >= cutoffDate)
                            filteredLines.Add(line);
                    }
                    else
                    {
                        // Keep lines that can't be parsed (headers, etc.)
                        filteredLines.Add(line);
                    }
                }

                await File.WriteAllTextAsync(AccessLogFile, string.Join(Environment.NewLine, filteredLines));
                LogService.WriteLiveLog($"[AUDIT] Cleaned up access logs older than {daysToKeep} days", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[AUDIT] Error cleaning up access logs: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        public static Dictionary<string, int> GetAccessStatistics()
        {
            var logs = GetRecentAccessLogs(TimeSpan.FromDays(30));
            var stats = new Dictionary<string, int>();

            // By action type
            var actionStats = logs.GroupBy(l => l.Action).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kvp in actionStats)
            {
                stats[$"Action: {kvp.Key}"] = kvp.Value;
            }

            // By service
            var serviceStats = logs.GroupBy(l => l.Service).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kvp in serviceStats)
            {
                stats[$"Service: {kvp.Key}"] = kvp.Value;
            }

            // By source
            var sourceStats = logs.GroupBy(l => l.Source).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kvp in sourceStats)
            {
                stats[$"Source: {kvp.Key}"] = kvp.Value;
            }

            return stats;
        }

        public static async Task InitializeCredentialRecordsAsync()
        {
            var credentials = LoadCredentials();
            var services = new[] { "FTP", "SQL", "Mailchimp" };
            var updated = false;

            foreach (var service in services)
            {
                if (!credentials.Any(c => c.Service.Equals(service, StringComparison.OrdinalIgnoreCase)))
                {
                    credentials.Add(new CredentialRecord 
                    { 
                        Service = service, 
                        LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") 
                    });
                    updated = true;
                }
            }

            if (updated)
            {
                await SaveCredentialsAsync(credentials);
                LogService.WriteLiveLog("[AUDIT] Initialized credential records for all services", "", "Information", "SYSTEM");
            }
        }
    }
}
