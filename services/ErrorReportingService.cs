using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class ErrorReportingService
    {
        private static readonly string ErrorLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PinayPalBackupManager", "error_reports.json");

        private static readonly int MaxErrorReports = 100;
        private static readonly object _lock = new object();

        public class ErrorReport
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
            public string ErrorType { get; set; } = "Exception";
            public string Message { get; set; } = "";
            public string StackTrace { get; set; } = "";
            public string Source { get; set; } = "";
            public Dictionary<string, string> Context { get; set; } = new();
            public bool IsCritical { get; set; }
            public bool IsReported { get; set; }
            public string ApplicationVersion { get; set; } = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            public string OperatingSystem { get; set; } = Environment.OSVersion.ToString();
        }

        public class ErrorReportSummary
        {
            public int TotalErrors { get; set; }
            public int CriticalErrors { get; set; }
            public int ReportedErrors { get; set; }
            public DateTime LastErrorTime { get; set; }
            public Dictionary<string, int> ErrorTypeCounts { get; set; } = new();
            public Dictionary<string, int> SourceCounts { get; set; } = new();
        }

        public static void Initialize()
        {
            try
            {
                var directory = Path.GetDirectoryName(ErrorLogPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Generate sample data if no errors exist
                if (!File.Exists(ErrorLogPath))
                {
                    GenerateSampleData();
                }
            }
            catch (Exception ex)
            {
                // If we can't initialize error reporting, log to system log
                LogService.WriteSystemLog($"Failed to initialize error reporting: {ex.Message}", "Error", "ERRORREPORTING");
            }
        }

        private static void GenerateSampleData()
        {
            try
            {
                var sampleErrors = new[]
                {
                    new ErrorReport
                    {
                        ErrorType = "IOException",
                        Message = "Failed to access backup file: permission denied",
                        Source = "FTP",
                        IsCritical = false,
                        Timestamp = DateTime.UtcNow.AddHours(-2)
                    },
                    new ErrorReport
                    {
                        ErrorType = "TimeoutException",
                        Message = "Connection to SQL server timed out after 30 seconds",
                        Source = "SQL",
                        IsCritical = true,
                        Timestamp = DateTime.UtcNow.AddHours(-5)
                    },
                    new ErrorReport
                    {
                        ErrorType = "ArgumentException",
                        Message = "Invalid API key provided for Mailchimp service",
                        Source = "Mailchimp",
                        IsCritical = false,
                        Timestamp = DateTime.UtcNow.AddHours(-1)
                    },
                    new ErrorReport
                    {
                        ErrorType = "NetworkException",
                        Message = "Unable to reach remote server - network connectivity issue",
                        Source = "System",
                        IsCritical = true,
                        Timestamp = DateTime.UtcNow.AddHours(-3)
                    }
                };

                var reports = LoadErrorReports();
                foreach (var error in sampleErrors)
                {
                    reports.Add(error);
                }
                SaveErrorReports(reports);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to generate sample error data: {ex.Message}", "Warning", "ERRORREPORTING");
            }
        }

        public static void ReportError(Exception exception, string source = "", bool isCritical = false, Dictionary<string, string>? context = null)
        {
            var report = new ErrorReport
            {
                ErrorType = exception.GetType().Name,
                Message = exception.Message,
                StackTrace = exception.StackTrace ?? "",
                Source = source,
                IsCritical = isCritical,
                Context = context ?? new Dictionary<string, string>()
            };

            AddErrorReport(report);
        }

        public static void ReportError(string message, string errorType = "Error", string source = "", bool isCritical = false, Dictionary<string, string>? context = null)
        {
            var report = new ErrorReport
            {
                ErrorType = errorType,
                Message = message,
                Source = source,
                IsCritical = isCritical,
                Context = context ?? new Dictionary<string, string>()
            };

            AddErrorReport(report);
        }

        private static void AddErrorReport(ErrorReport report)
        {
            lock (_lock)
            {
                try
                {
                    var reports = LoadErrorReports();
                    
                    // Add new report
                    reports.Insert(0, report);
                    
                    // Keep only the most recent reports
                    if (reports.Count > MaxErrorReports)
                    {
                        reports = reports.Take(MaxErrorReports).ToList();
                    }
                    
                    // Save to file
                    SaveErrorReports(reports);
                    
                    // Log to system log
                    var logLevel = report.IsCritical ? "Critical" : "Error";
                    LogService.WriteSystemLog($"[{report.ErrorType}] {report.Message}", logLevel, report.Source);
                    
                    // Show notification for critical errors
                    if (report.IsCritical)
                    {
                        NotificationService.ShowBackupToast("Critical Error", report.Message, "Error");
                    }
                }
                catch (Exception ex)
                {
                    // If error reporting fails, at least log to system log
                    LogService.WriteSystemLog($"Failed to report error: {ex.Message}", "Error", "ERRORREPORTING");
                }
            }
        }

        private static List<ErrorReport> LoadErrorReports()
        {
            try
            {
                if (!File.Exists(ErrorLogPath))
                {
                    return new List<ErrorReport>();
                }

                var json = File.ReadAllText(ErrorLogPath);
                return JsonSerializer.Deserialize<List<ErrorReport>>(json) ?? new List<ErrorReport>();
            }
            catch
            {
                return new List<ErrorReport>();
            }
        }

        private static void SaveErrorReports(List<ErrorReport> reports)
        {
            try
            {
                var json = JsonSerializer.Serialize(reports, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(ErrorLogPath, json);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to save error reports: {ex.Message}", "Error", "ERRORREPORTING");
            }
        }

        public static List<ErrorReport> GetErrorReports(int limit = 50)
        {
            lock (_lock)
            {
                try
                {
                    var reports = LoadErrorReports();
                    return reports.Take(limit).ToList();
                }
                catch
                {
                    return new List<ErrorReport>();
                }
            }
        }

        public static List<ErrorReport> GetCriticalErrors(int limit = 50)
        {
            lock (_lock)
            {
                try
                {
                    var reports = LoadErrorReports();
                    return reports.Where(r => r.IsCritical).Take(limit).ToList();
                }
                catch
                {
                    return new List<ErrorReport>();
                }
            }
        }

        public static List<ErrorReport> GetErrorsBySource(string source, int limit = 50)
        {
            lock (_lock)
            {
                try
                {
                    var reports = LoadErrorReports();
                    return reports.Where(r => r.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                                  .Take(limit).ToList();
                }
                catch
                {
                    return new List<ErrorReport>();
                }
            }
        }

        public static ErrorReportSummary GetErrorSummary()
        {
            lock (_lock)
            {
                try
                {
                    var reports = LoadErrorReports();
                    
                    var summary = new ErrorReportSummary
                    {
                        TotalErrors = reports.Count,
                        CriticalErrors = reports.Count(r => r.IsCritical),
                        ReportedErrors = reports.Count(r => r.IsReported),
                        LastErrorTime = reports.Count > 0 ? reports[0].Timestamp : DateTime.MinValue
                    };
                    
                    // Count by error type
                    foreach (var report in reports)
                    {
                        if (!summary.ErrorTypeCounts.ContainsKey(report.ErrorType))
                        {
                            summary.ErrorTypeCounts[report.ErrorType] = 0;
                        }
                        summary.ErrorTypeCounts[report.ErrorType]++;
                        
                        if (!summary.SourceCounts.ContainsKey(report.Source))
                        {
                            summary.SourceCounts[report.Source] = 0;
                        }
                        summary.SourceCounts[report.Source]++;
                    }
                    
                    return summary;
                }
                catch
                {
                    return new ErrorReportSummary();
                }
            }
        }

        public static void ClearErrorReports()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(ErrorLogPath))
                    {
                        File.Delete(ErrorLogPath);
                    }
                    LogService.WriteSystemLog("Error reports cleared", "Information", "ERRORREPORTING");
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to clear error reports: {ex.Message}", "Error", "ERRORREPORTING");
                }
            }
        }

        public static void ClearOldErrorReports(TimeSpan maxAge)
        {
            lock (_lock)
            {
                try
                {
                    var reports = LoadErrorReports();
                    var cutoffDate = DateTime.UtcNow - maxAge;
                    
                    var filteredReports = reports.Where(r => r.Timestamp > cutoffDate).ToList();
                    
                    SaveErrorReports(filteredReports);
                    
                    var removedCount = reports.Count - filteredReports.Count;
                    LogService.WriteSystemLog($"Cleared {removedCount} old error reports (older than {maxAge.Days} days)", "Information", "ERRORREPORTING");
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to clear old error reports: {ex.Message}", "Error", "ERRORREPORTING");
                }
            }
        }

        public static string ExportErrorReports()
        {
            lock (_lock)
            {
                try
                {
                    var reports = LoadErrorReports();
                    var sb = new StringBuilder();
                    
                    sb.AppendLine("=== ERROR REPORT EXPORT ===");
                    sb.AppendLine($"Export Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                    sb.AppendLine($"Total Errors: {reports.Count}");
                    sb.AppendLine();
                    
                    foreach (var report in reports)
                    {
                        sb.AppendLine($"--- Error {report.Id} ---");
                        sb.AppendLine($"Timestamp: {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
                        sb.AppendLine($"Type: {report.ErrorType}");
                        sb.AppendLine($"Source: {report.Source}");
                        sb.AppendLine($"Critical: {report.IsCritical}");
                        sb.AppendLine($"Version: {report.ApplicationVersion}");
                        sb.AppendLine($"OS: {report.OperatingSystem}");
                        sb.AppendLine($"Message: {report.Message}");
                        
                        if (!string.IsNullOrEmpty(report.StackTrace))
                        {
                            sb.AppendLine("Stack Trace:");
                            sb.AppendLine(report.StackTrace);
                        }
                        
                        if (report.Context.Count > 0)
                        {
                            sb.AppendLine("Context:");
                            foreach (var kvp in report.Context)
                            {
                                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
                            }
                        }
                        
                        sb.AppendLine();
                    }
                    
                    return sb.ToString();
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to export error reports: {ex.Message}", "Error", "ERRORREPORTING");
                    return $"Error exporting reports: {ex.Message}";
                }
            }
        }

        public static async Task<bool> ReportToExternalServiceAsync(ErrorReport report)
        {
            // Placeholder for external error reporting (e.g., Sentry, Bugsnag, custom API)
            // This would be implemented when an external error reporting service is available
            
            try
            {
                // Mark as reported
                report.IsReported = true;
                
                // Update the report in storage
                var reports = LoadErrorReports();
                var existingReport = reports.FirstOrDefault(r => r.Id == report.Id);
                if (existingReport != null)
                {
                    existingReport.IsReported = true;
                    SaveErrorReports(reports);
                }
                
                LogService.WriteSystemLog($"Error report {report.Id} marked as reported", "Information", "ERRORREPORTING");
                return true;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to report error to external service: {ex.Message}", "Error", "ERRORREPORTING");
                return false;
            }
        }

        public static void WrapWithErrorReporting(Action action, string source = "")
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                ReportError(ex, source);
                throw; // Re-throw to maintain original behavior
            }
        }

        public static async Task<T> WrapWithErrorReportingAsync<T>(Func<Task<T>> action, string source = "")
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                ReportError(ex, source);
                throw; // Re-throw to maintain original behavior
            }
        }

        public static async Task WrapWithErrorReportingAsync(Func<Task> action, string source = "")
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                ReportError(ex, source);
                throw; // Re-throw to maintain original behavior
            }
        }
    }
}
