using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public static class BackupHealthService
    {
        public class HealthScore
        {
            public int OverallScore { get; set; } // 0-100
            public string Trend { get; set; } = "→"; // "↗", "↘", "→"
            public string TrendText { get; set; } = "Stable";
            public List<CriticalAlert> CriticalAlerts { get; set; } = new();
            public Dictionary<string, int> ServiceScores { get; set; } = new();
        }

        public class CriticalAlert
        {
            public string Service { get; set; } = "";
            public string Message { get; set; } = "";
            public DateTime LastFailure { get; set; } = DateTime.Now;
            public TimeSpan Age => DateTime.Now - LastFailure;
            public string AgeText => Age.TotalHours < 1 ? $"{Age.TotalMinutes:F0} min ago" : 
                                   Age.TotalHours < 24 ? $"{Age.TotalHours:F1} hours ago" : 
                                   $"{Age.TotalDays:F1} days ago";
        }

        public class AnalyticsData
        {
            public List<SizeTrend> SizeTrends { get; set; } = new();
            public List<FailurePattern> FailurePatterns { get; set; } = new();
            public List<PerformanceMetric> PerformanceMetrics { get; set; } = new();
        }

        public class SizeTrend
        {
            public DateTime Date { get; set; } = DateTime.Now;
            public string Service { get; set; } = "";
            public long SizeBytes { get; set; }
            public double SizeMB => SizeBytes / 1024.0 / 1024.0;
        }

        public class FailurePattern
        {
            public string Service { get; set; } = "";
            public string ErrorType { get; set; } = "";
            public int Count { get; set; }
            public List<DateTime> Occurrences { get; set; } = new();
            public string MostCommonHour => Occurrences.Any() ? Occurrences.GroupBy(h => h.Hour).OrderByDescending(g => g.Count()).First().Key.ToString("00") + ":00" : "00:00";
        }

        public class PerformanceMetric
        {
            public string Service { get; set; } = "";
            public DateTime Date { get; set; } = DateTime.Now;
            public TimeSpan Duration { get; set; }
            public double SpeedMBps => 0; // Would need file size/duration calculation
        }

        public static HealthScore CalculateHealthScore()
        {
            var score = new HealthScore();
            var now = DateTime.Now;
            var last24h = now.AddHours(-24);
            var last7d = now.AddDays(-7);

            // Calculate per-service scores (last 7 days)
            var services = new[] { "FTP", "SQL", "Mailchimp" };
            foreach (var service in services)
            {
                var logFile = GetLogFile(service);
                var logs = GetRecentLogs(logFile, last7d);
                var total = logs.Count;
                var successful = logs.Count(l => l.Contains("SUCCESS") || l.Contains("COMPLETE"));
                var serviceScore = total > 0 ? (successful * 100 / total) : 0;
                score.ServiceScores[service] = serviceScore;
            }

            // Overall score (weighted average)
            score.OverallScore = score.ServiceScores.Values.Count > 0 ? 
                (int)score.ServiceScores.Values.Average() : 0;

            // Calculate trend (compare last 24h vs previous 24h)
            var recentScore = CalculatePeriodScore(last24h, now);
            var previousScore = CalculatePeriodScore(last24h.AddHours(-24), last24h);
            
            if (recentScore > previousScore + 5)
            {
                score.Trend = "↗";
                score.TrendText = "Improving";
            }
            else if (recentScore < previousScore - 5)
            {
                score.Trend = "↘";
                score.TrendText = "Declining";
            }
            else
            {
                score.Trend = "→";
                score.TrendText = "Stable";
            }

            // Find critical alerts (failures older than 2 hours)
            var criticalThreshold = now.AddHours(-2);
            foreach (var service in services)
            {
                var logFile = GetLogFile(service);
                var logs = GetRecentLogs(logFile, criticalThreshold);
                var failures = logs.Where(l => l.Contains("ERROR") || l.Contains("FAILED") || l.Contains("LOGIN FAILED"));
                
                foreach (var failure in failures.Take(3)) // Limit to 3 most recent per service
                {
                    var alert = new CriticalAlert
                    {
                        Service = service,
                        Message = ExtractErrorMessage(failure),
                        LastFailure = ParseLogTime(failure) ?? now
                    };
                    score.CriticalAlerts.Add(alert);
                }
            }

            return score;
        }

        public static AnalyticsData GetAnalyticsData()
        {
            var data = new AnalyticsData();
            var last30d = DateTime.Now.AddDays(-30);

            // Size trends (from backup files)
            data.SizeTrends = GetSizeTrends(last30d);

            // Failure patterns (from logs)
            data.FailurePatterns = GetFailurePatterns(last30d);

            // Performance metrics (from logs with duration info)
            data.PerformanceMetrics = GetPerformanceMetrics(last30d);

            return data;
        }

        private static List<SizeTrend> GetSizeTrends(DateTime since)
        {
            var trends = new List<SizeTrend>();
            var services = new[] { 
                (BackupConfig.FtpLocalFolder, "FTP"),
                (BackupConfig.SqlLocalFolder, "SQL"),
                (BackupConfig.MailchimpFolder, "Mailchimp")
            };

            foreach (var (folder, service) in services)
            {
                if (!Directory.Exists(folder)) continue;

                var files = new DirectoryInfo(folder)
                    .GetFiles("*", SearchOption.AllDirectories)
                    .Where(f => f.LastWriteTime >= since && f.Name != "backuplog.txt")
                    .ToList();

                // Group by date and calculate daily totals
                var dailySizes = files
                    .GroupBy(f => f.LastWriteTime.Date)
                    .Select(g => new SizeTrend
                    {
                        Date = g.Key,
                        Service = service,
                        SizeBytes = g.Sum(f => f.Length)
                    })
                    .OrderBy(t => t.Date)
                    .ToList();

                trends.AddRange(dailySizes);
            }

            return trends;
        }

        private static List<FailurePattern> GetFailurePatterns(DateTime since)
        {
            var patterns = new List<FailurePattern>();
            var services = new[] { ("FTP", BackupConfig.FtpLogFile), ("SQL", BackupConfig.SqlLogFile), ("Mailchimp", BackupConfig.McLogFile) };

            foreach (var (service, logFile) in services)
            {
                if (!File.Exists(logFile)) continue;

                var logs = GetRecentLogs(logFile, since);
                var failures = logs.Where(l => l.Contains("ERROR") || l.Contains("FAILED") || l.Contains("LOGIN FAILED"));

                // Group by error type
                var errorGroups = failures
                    .GroupBy(f => ExtractErrorType(f))
                    .Select(g => new FailurePattern
                    {
                        Service = service,
                        ErrorType = g.Key,
                        Count = g.Count(),
                        Occurrences = g.Select(f => ParseLogTime(f) ?? DateTime.Now).ToList()
                    })
                    .OrderByDescending(p => p.Count)
                    .Take(5) // Top 5 error types per service
                    .ToList();

                patterns.AddRange(errorGroups);
            }

            return patterns;
        }

        private static List<PerformanceMetric> GetPerformanceMetrics(DateTime since)
        {
            var metrics = new List<PerformanceMetric>();
            var services = new[] { ("FTP", BackupConfig.FtpLogFile), ("SQL", BackupConfig.SqlLogFile), ("Mailchimp", BackupConfig.McLogFile) };

            foreach (var (service, logFile) in services)
            {
                if (!File.Exists(logFile)) continue;

                var logs = GetRecentLogs(logFile, since);
                var sessions = logs.Where(l => l.Contains("SESSION:"));

                foreach (var session in sessions)
                {
                    // Look for completion logs with timing info
                    var sessionTime = ParseLogTime(session) ?? DateTime.Now;
                    // This would need enhancement to extract actual duration from logs
                    metrics.Add(new PerformanceMetric
                    {
                        Service = service,
                        Date = sessionTime,
                        Duration = TimeSpan.FromMinutes(5) // Placeholder
                    });
                }
            }

            return metrics;
        }

        private static string GetLogFile(string service)
        {
            return service switch
            {
                "FTP" => BackupConfig.FtpLogFile,
                "SQL" => BackupConfig.SqlLogFile,
                "Mailchimp" => BackupConfig.McLogFile,
                _ => ""
            };
        }

        private static List<string> GetRecentLogs(string logFile, DateTime since)
        {
            if (!File.Exists(logFile)) return new List<string>();

            return File.ReadAllLines(logFile)
                .Where(line => ParseLogTime(line) >= since)
                .ToList();
        }

        private static DateTime? ParseLogTime(string logLine)
        {
            // Expected format: "[2025-04-04 12:34:56] MESSAGE"
            var match = System.Text.RegularExpressions.Regex.Match(logLine, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]");
            if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var time))
                return time;
            return null;
        }

        private static string ExtractErrorMessage(string logLine)
        {
            // Extract the actual error message after the timestamp
            var parts = logLine.Split(']', 2);
            if (parts.Length > 1) return parts[1].Trim();
            return logLine;
        }

        private static string ExtractErrorType(string logLine)
        {
            var message = ExtractErrorMessage(logLine).ToLower();
            
            if (message.Contains("authentication") || message.Contains("login")) return "Authentication";
            if (message.Contains("connection") || message.Contains("network")) return "Connection";
            if (message.Contains("timeout")) return "Timeout";
            if (message.Contains("disk") || message.Contains("space")) return "Storage";
            if (message.Contains("permission")) return "Permission";
            if (message.Contains("cancelled")) return "Cancelled";
            
            return "Other";
        }

        private static int CalculatePeriodScore(DateTime start, DateTime end)
        {
            var services = new[] { "FTP", "SQL", "Mailchimp" };
            var scores = new List<int>();

            foreach (var service in services)
            {
                var logFile = GetLogFile(service);
                var logs = GetRecentLogs(logFile, start).Where(l => ParseLogTime(l) < end);
                var total = logs.Count();
                var successful = logs.Count(l => l.Contains("SUCCESS") || l.Contains("COMPLETE"));
                var score = total > 0 ? (successful * 100 / total) : 0;
                scores.Add(score);
            }

            return scores.Count > 0 ? (int)scores.Average() : 0;
        }
    }
}
