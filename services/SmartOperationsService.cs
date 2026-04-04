using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public static class SmartOperationsService
    {
        public class RetryConfig
        {
            public int MaxRetries { get; set; } = 3;
            public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);
            public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
            public double BackoffMultiplier { get; set; } = 2.0;
        }

        public class PreFlightResult
        {
            public bool CanProceed { get; set; }
            public string Service { get; set; } = "";
            public List<string> Issues { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
            public TimeSpan EstimatedDuration { get; set; }
        }

        public class ParallelBackupRequest
        {
            public string Service { get; set; } = "";
            public Func<Task> BackupAction { get; set; } = null!;
            public bool IsSafeToRunInParallel { get; set; }
        }

        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> action,
            RetryConfig? config = null,
            string serviceName = "Unknown")
        {
            config ??= new RetryConfig();
            var attempt = 0;
            var lastException = new Exception();

            while (attempt <= config.MaxRetries)
            {
                try
                {
                    attempt++;
                    LogService.WriteLiveLog($"[RETRY] {serviceName} attempt {attempt}/{config.MaxRetries + 1}", "", "Information", "SYSTEM");
                    
                    var result = await action();
                    
                    if (attempt > 1)
                        LogService.WriteLiveLog($"[RETRY] {serviceName} succeeded on attempt {attempt}", "", "Information", "SYSTEM");
                    
                    return result;
                }
                catch (Exception ex) when (attempt <= config.MaxRetries && IsRetriableException(ex))
                {
                    lastException = ex;
                    LogService.WriteLiveLog($"[RETRY] {serviceName} failed on attempt {attempt}: {ex.Message}", "", "Warning", "SYSTEM");
                    
                    if (attempt <= config.MaxRetries)
                    {
                        var delay = CalculateDelay(attempt, config);
                        LogService.WriteLiveLog($"[RETRY] {serviceName} retrying in {delay.TotalMinutes:F1} minutes...", "", "Information", "SYSTEM");
                        await Task.Delay(delay);
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[RETRY] {serviceName} failed with non-retriable error: {ex.Message}", "", "Error", "SYSTEM");
                    throw;
                }
            }

            LogService.WriteLiveLog($"[RETRY] {serviceName} failed after {config.MaxRetries + 1} attempts", "", "Error", "SYSTEM");
            if (lastException != null)
                throw lastException;
            
            // This should never be reached due to the throw above
            throw lastException;
        }

        public static async Task ExecuteWithRetryAsync(
            Func<Task> action,
            RetryConfig? config = null,
            string serviceName = "Unknown")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await action();
                return true;
            }, config, serviceName);
        }

        public static async Task<List<PreFlightResult>> RunPreFlightChecks()
        {
            var results = new List<PreFlightResult>();
            var services = new[] { "FTP", "SQL", "Mailchimp" };

            foreach (var service in services)
            {
                var result = await RunPreFlightCheck(service);
                results.Add(result);
            }

            return results;
        }

        public static async Task<PreFlightResult> RunPreFlightCheck(string service)
        {
            var result = new PreFlightResult { Service = service };

            try
            {
                LogService.WriteLiveLog($"[PREFLIGHT] Running pre-flight check for {service}...", "", "Information", "SYSTEM");

                switch (service)
                {
                    case "FTP":
                        result = await CheckFtpPreFlight();
                        break;
                    case "SQL":
                        result = await CheckSqlPreFlight();
                        break;
                    case "Mailchimp":
                        result = await CheckMailchimpPreFlight();
                        break;
                }

                result.CanProceed = result.Issues.Count == 0;
                
                if (result.CanProceed)
                    LogService.WriteLiveLog($"[PREFLIGHT] {service} passed all checks", "", "Information", "SYSTEM");
                else
                    LogService.WriteLiveLog($"[PREFLIGHT] {service} has {result.Issues.Count} issue(s)", "", "Warning", "SYSTEM");
            }
            catch (Exception ex)
            {
                result.CanProceed = false;
                result.Issues.Add($"Pre-flight check failed: {ex.Message}");
                LogService.WriteLiveLog($"[PREFLIGHT] {service} pre-flight check error: {ex.Message}", "", "Error", "SYSTEM");
            }

            return result;
        }

        public static async Task<Dictionary<string, bool>> ExecuteParallelBackups(List<ParallelBackupRequest> requests)
        {
            var results = new Dictionary<string, bool>();
            var safeToParallel = requests.Where(r => r.IsSafeToRunInParallel).ToList();
            var sequential = requests.Where(r => !r.IsSafeToRunInParallel).ToList();

            LogService.WriteLiveLog($"[PARALLEL] Executing {safeToParallel.Count} parallel, {sequential.Count} sequential backups", "", "Information", "SYSTEM");

            // Run safe backups in parallel
            if (safeToParallel.Any())
            {
                var parallelTasks = safeToParallel.Select(async req =>
                {
                    try
                    {
                        await req.BackupAction();
                        results[req.Service] = true;
                        LogService.WriteLiveLog($"[PARALLEL] {req.Service} completed successfully", "", "Information", "SYSTEM");
                    }
                    catch (Exception ex)
                    {
                        results[req.Service] = false;
                        LogService.WriteLiveLog($"[PARALLEL] {req.Service} failed: {ex.Message}", "", "Error", "SYSTEM");
                    }
                });

                await Task.WhenAll(parallelTasks);
            }

            // Run sequential backups one by one
            foreach (var req in sequential)
            {
                try
                {
                    await req.BackupAction();
                    results[req.Service] = true;
                    LogService.WriteLiveLog($"[SEQUENTIAL] {req.Service} completed successfully", "", "Information", "SYSTEM");
                }
                catch (Exception ex)
                {
                    results[req.Service] = false;
                    LogService.WriteLiveLog($"[SEQUENTIAL] {req.Service} failed: {ex.Message}", "", "Error", "SYSTEM");
                }
            }

            return results;
        }

        private static async Task<PreFlightResult> CheckFtpPreFlight()
        {
            var result = new PreFlightResult { Service = "FTP" };

            // Check credentials
            try
            {
                var decryptedPass = SecurityService.GetDecryptedFtpPassword();
                if (string.IsNullOrEmpty(decryptedPass))
                    result.Issues.Add("FTP credentials not configured");
            }
            catch
            {
                result.Issues.Add("FTP credentials decryption failed");
            }

            // Check connectivity
            try
            {
                using var ftp = new FtpService();
                ftp.Initialize(BackupConfig.FtpHost, BackupConfig.FtpUser, SecurityService.GetDecryptedFtpPassword(), "", 21);
                var connected = await ftp.ConnectAsync();
                if (!connected)
                    result.Issues.Add("Cannot connect to FTP server");
            }
            catch (Exception ex)
            {
                result.Issues.Add($"FTP connection test failed: {ex.Message}");
            }

            // Check local folder
            if (!System.IO.Directory.Exists(BackupConfig.FtpLocalFolder))
            {
                result.Issues.Add("FTP local folder does not exist");
            }
            else
            {
                try
                {
                    var testFile = System.IO.Path.Combine(BackupConfig.FtpLocalFolder, ".access_test");
                    await System.IO.File.WriteAllTextAsync(testFile, "test");
                    System.IO.File.Delete(testFile);
                }
                catch
                {
                    result.Issues.Add("Cannot write to FTP local folder");
                }
            }

            // Check disk space (warning only)
            try
            {
                var rootPath = System.IO.Path.GetPathRoot(BackupConfig.FtpLocalFolder);
                if (!string.IsNullOrEmpty(rootPath))
                {
                    var drive = new System.IO.DriveInfo(rootPath);
                    var freeGB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                    if (freeGB < 5.0)
                        result.Warnings.Add($"Low disk space: {freeGB:F1} GB free");
                }
            }
            catch { }

            result.EstimatedDuration = TimeSpan.FromMinutes(5);
            return result;
        }

        private static async Task<PreFlightResult> CheckSqlPreFlight()
        {
            var result = new PreFlightResult { Service = "SQL" };

            // Check credentials
            try
            {
                var decryptedPass = SecurityService.GetDecryptedSqlPassword();
                if (string.IsNullOrEmpty(decryptedPass))
                    result.Issues.Add("SQL credentials not configured");
            }
            catch
            {
                result.Issues.Add("SQL credentials decryption failed");
            }

            // Check connectivity
            try
            {
                using var sql = new SqlService();
                sql.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, SecurityService.GetDecryptedSqlPassword(), BackupConfig.SqlTlsFingerprint);
                var connected = await sql.ConnectAsync();
                if (!connected)
                    result.Issues.Add("Cannot connect to SQL FTP server");
            }
            catch (Exception ex)
            {
                result.Issues.Add($"SQL connection test failed: {ex.Message}");
            }

            // Check local folder
            if (!System.IO.Directory.Exists(BackupConfig.SqlLocalFolder))
                result.Issues.Add("SQL local folder does not exist");

            result.EstimatedDuration = TimeSpan.FromMinutes(3);
            return result;
        }

        private static async Task<PreFlightResult> CheckMailchimpPreFlight()
        {
            var result = new PreFlightResult { Service = "Mailchimp" };

            // Check API key
            if (string.IsNullOrEmpty(BackupConfig.McApiKey))
                result.Issues.Add("Mailchimp API key not configured");
            else if (!BackupConfig.McApiKey.StartsWith("us") || !BackupConfig.McApiKey.Contains("-"))
                result.Issues.Add("Mailchimp API key format appears invalid");

            // Test API connectivity
            try
            {
                var mc = new MailchimpService(BackupConfig.McApiKey, BackupConfig.McAudienceId);
                // This would be a lightweight API call to test connectivity
                // For now, just validate the key format
            }
            catch (Exception ex)
            {
                result.Issues.Add($"Mailchimp API test failed: {ex.Message}");
            }

            // Check local folder
            if (!System.IO.Directory.Exists(BackupConfig.MailchimpFolder))
                result.Issues.Add("Mailchimp local folder does not exist");

            result.EstimatedDuration = TimeSpan.FromMinutes(2);
            return result;
        }

        private static bool IsRetriableException(Exception ex)
        {
            var message = ex.Message.ToLower();
            
            // Network-related errors
            if (message.Contains("network") || message.Contains("connection") || 
                message.Contains("timeout") || message.Contains("host") ||
                message.Contains("unreachable") || message.Contains("refused"))
                return true;

            // Temporary FTP issues
            if (message.Contains("ftp") && (message.Contains("temporary") || 
                message.Contains("busy") || message.Contains("quota")))
                return true;

            // API rate limiting
            if (message.Contains("rate limit") || message.Contains("too many requests"))
                return true;

            // Authentication errors are not retriable
            if (message.Contains("authentication") || message.Contains("login") ||
                message.Contains("unauthorized") || message.Contains("forbidden"))
                return false;

            // File system errors are generally not retriable
            if (message.Contains("disk") || message.Contains("space") || 
                message.Contains("permission") || message.Contains("access denied"))
                return false;

            // Default to retriable for unknown errors
            return true;
        }

        private static TimeSpan CalculateDelay(int attempt, RetryConfig config)
        {
            var delay = TimeSpan.FromMilliseconds(
                config.InitialDelay.TotalMilliseconds * Math.Pow(config.BackoffMultiplier, attempt - 1));
            
            // Add jitter to prevent thundering herd
            var jitter = TimeSpan.FromMilliseconds(new Random().Next(0, (int)(delay.TotalMilliseconds * 0.1)));
            
            return TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds + jitter.TotalMilliseconds, 
                config.MaxDelay.TotalMilliseconds));
        }
    }
}
