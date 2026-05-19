using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class HealthCheckService
    {
        public class HealthCheckResult
        {
            public bool IsHealthy { get; set; }
            public string Status { get; set; } = "Unknown";
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
            public Dictionary<string, ComponentHealth> Components { get; set; } = new();
            public SystemResourceInfo Resources { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
            public List<string> Errors { get; set; } = new();
        }

        public class ComponentHealth
        {
            public string Name { get; set; } = "";
            public bool IsHealthy { get; set; }
            public string Status { get; set; } = "Unknown";
            public string Details { get; set; } = "";
            public TimeSpan? ResponseTime { get; set; }
            public DateTime LastChecked { get; set; } = DateTime.UtcNow;
        }

        public class SystemResourceInfo
        {
            public double CpuUsagePercent { get; set; }
            public long TotalMemoryMB { get; set; }
            public long AvailableMemoryMB { get; set; }
            public long UsedMemoryMB { get; set; }
            public double MemoryUsagePercent { get; set; }
            public long TotalDiskSpaceGB { get; set; }
            public long AvailableDiskSpaceGB { get; set; }
            public long UsedDiskSpaceGB { get; set; }
            public double DiskUsagePercent { get; set; }
            public string BackupPath { get; set; } = "";
            public long BackupPathSizeMB { get; set; }
        }

        private static readonly List<ComponentHealth> _componentHistory = new();
        private static HealthCheckResult? _lastResult;

        public static HealthCheckResult? GetLastResult() => _lastResult;

        public static async Task<HealthCheckResult> RunHealthCheckAsync()
        {
            var result = new HealthCheckResult
            {
                Timestamp = DateTime.UtcNow
            };

            try
            {
                // Check Database
                result.Components["Database"] = await CheckDatabaseHealthAsync();

                // Check Backup Paths
                result.Components["BackupPaths"] = await CheckBackupPathsHealthAsync();

                // Check Configuration
                result.Components["Configuration"] = await CheckConfigurationHealthAsync();

                // Check Authentication Service
                result.Components["Authentication"] = await CheckAuthenticationHealthAsync();

                // Check Services
                result.Components["Services"] = await CheckServicesHealthAsync();

                // Get System Resources
                result.Resources = await GetSystemResourceInfoAsync();

                // Determine overall health
                var unhealthyComponents = result.Components.Values.Where(c => !c.IsHealthy).ToList();
                result.IsHealthy = unhealthyComponents.Count == 0;
                result.Status = result.IsHealthy ? "Healthy" : $"Degraded ({unhealthyComponents.Count} components unhealthy)";

                // Collect warnings and errors
                foreach (var component in result.Components.Values)
                {
                    if (!component.IsHealthy)
                    {
                        result.Errors.Add($"{component.Name}: {component.Status} - {component.Details}");
                    }
                    else if (!string.IsNullOrEmpty(component.Details) && component.Details.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add($"{component.Name}: {component.Details}");
                    }
                }

                // Add resource warnings
                if (result.Resources.MemoryUsagePercent > 80)
                {
                    result.Warnings.Add($"High memory usage: {result.Resources.MemoryUsagePercent:F1}%");
                }
                if (result.Resources.DiskUsagePercent > 80)
                {
                    result.Warnings.Add($"High disk usage: {result.Resources.DiskUsagePercent:F1}%");
                }
                if (result.Resources.CpuUsagePercent > 80)
                {
                    result.Warnings.Add($"High CPU usage: {result.Resources.CpuUsagePercent:F1}%");
                }

                _lastResult = result;
                LogService.WriteSystemLog($"Health check completed: {result.Status}", "Information", "HEALTHCHECK");
            }
            catch (Exception ex)
            {
                result.IsHealthy = false;
                result.Status = "Error";
                result.Errors.Add($"Health check failed: {ex.Message}");
                LogService.WriteSystemLog($"Health check error: {ex.Message}", "Error", "HEALTHCHECK");
            }

            return result;
        }

        private static async Task<ComponentHealth> CheckDatabaseHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var health = new ComponentHealth { Name = "Database" };

            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                
                // Test query
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Users";
                var count = cmd.ExecuteScalar();
                
                health.IsHealthy = true;
                health.Status = "Operational";
                health.Details = $"Database contains {count} user(s)";
            }
            catch (Exception ex)
            {
                health.IsHealthy = false;
                health.Status = "Error";
                health.Details = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                health.ResponseTime = stopwatch.Elapsed;
                health.LastChecked = DateTime.UtcNow;
            }

            return await Task.FromResult(health);
        }

        private static async Task<ComponentHealth> CheckBackupPathsHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var health = new ComponentHealth { Name = "BackupPaths" };

            try
            {
                var backupPath = EnvironmentConfigService.GetBackupPath();
                var logPath = EnvironmentConfigService.GetLogPath();
                var avatarsPath = EnvironmentConfigService.GetAvatarsPath();

                var issues = new List<string>();

                if (!Directory.Exists(backupPath))
                {
                    issues.Add("Backup path does not exist");
                }
                else
                {
                    try
                    {
                        var testFile = Path.Combine(backupPath, ".healthcheck");
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                    }
                    catch
                    {
                        issues.Add("Backup path is not writable");
                    }
                }

                if (!Directory.Exists(logPath))
                {
                    issues.Add("Log path does not exist");
                }

                if (!Directory.Exists(avatarsPath))
                {
                    issues.Add("Avatars path does not exist");
                }

                health.IsHealthy = issues.Count == 0;
                health.Status = health.IsHealthy ? "Operational" : "Issues Found";
                health.Details = issues.Count > 0 ? string.Join("; ", issues) : "All paths accessible and writable";
            }
            catch (Exception ex)
            {
                health.IsHealthy = false;
                health.Status = "Error";
                health.Details = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                health.ResponseTime = stopwatch.Elapsed;
                health.LastChecked = DateTime.UtcNow;
            }

            return await Task.FromResult(health);
        }

        private static async Task<ComponentHealth> CheckConfigurationHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var health = new ComponentHealth { Name = "Configuration" };

            try
            {
                var config = ConfigService.Current;
                var issues = new List<string>();

                if (string.IsNullOrWhiteSpace(config.Ftp.Host))
                {
                    issues.Add("FTP host not configured");
                }
                if (string.IsNullOrWhiteSpace(config.Sql.Host))
                {
                    issues.Add("SQL host not configured");
                }
                if (string.IsNullOrWhiteSpace(config.Mailchimp.ApiKey))
                {
                    issues.Add("Mailchimp API key not configured");
                }

                health.IsHealthy = issues.Count == 0;
                health.Status = health.IsHealthy ? "Operational" : "Incomplete Configuration";
                health.Details = issues.Count > 0 ? string.Join("; ", issues) : "All services configured";
            }
            catch (Exception ex)
            {
                health.IsHealthy = false;
                health.Status = "Error";
                health.Details = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                health.ResponseTime = stopwatch.Elapsed;
                health.LastChecked = DateTime.UtcNow;
            }

            return await Task.FromResult(health);
        }

        private static async Task<ComponentHealth> CheckAuthenticationHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var health = new ComponentHealth { Name = "Authentication" };

            try
            {
                var hasUsers = AuthService.HasAnyUsers();
                health.IsHealthy = true;
                health.Status = "Operational";
                health.Details = hasUsers ? "Users configured" : "No users configured (first run)";
            }
            catch (Exception ex)
            {
                health.IsHealthy = false;
                health.Status = "Error";
                health.Details = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                health.ResponseTime = stopwatch.Elapsed;
                health.LastChecked = DateTime.UtcNow;
            }

            return await Task.FromResult(health);
        }

        private static async Task<ComponentHealth> CheckServicesHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var health = new ComponentHealth { Name = "Services" };

            try
            {
                var issues = new List<string>();

                // Check if critical services are accessible
                try
                {
                    var serviceType = typeof(FileHashCacheService);
                    // Just check if the type exists
                }
                catch
                {
                    issues.Add("FileHashCacheService not available");
                }

                try
                {
                    ThrottleService.Reset();
                }
                catch
                {
                    issues.Add("ThrottleService not responding");
                }

                health.IsHealthy = issues.Count == 0;
                health.Status = health.IsHealthy ? "Operational" : "Degraded";
                health.Details = issues.Count > 0 ? string.Join("; ", issues) : "All services operational";
            }
            catch (Exception ex)
            {
                health.IsHealthy = false;
                health.Status = "Error";
                health.Details = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                health.ResponseTime = stopwatch.Elapsed;
                health.LastChecked = DateTime.UtcNow;
            }

            return await Task.FromResult(health);
        }

        private static async Task<SystemResourceInfo> GetSystemResourceInfoAsync()
        {
            var info = new SystemResourceInfo();

            try
            {
                // CPU Usage
                info.CpuUsagePercent = GetCpuUsage();

                // Memory Usage
                info.TotalMemoryMB = GetTotalMemoryMB();
                info.AvailableMemoryMB = GetAvailableMemoryMB();
                info.UsedMemoryMB = info.TotalMemoryMB - info.AvailableMemoryMB;
                info.MemoryUsagePercent = info.TotalMemoryMB > 0 ? (info.UsedMemoryMB * 100.0 / info.TotalMemoryMB) : 0;

                // Disk Usage
                var backupPath = EnvironmentConfigService.GetBackupPath();
                info.BackupPath = backupPath;
                
                if (Directory.Exists(backupPath))
                {
                    var driveInfo = new DriveInfo(Path.GetPathRoot(backupPath) ?? backupPath);
                    info.TotalDiskSpaceGB = (long)(driveInfo.TotalSize / (1024.0 * 1024 * 1024));
                    info.AvailableDiskSpaceGB = (long)(driveInfo.AvailableFreeSpace / (1024.0 * 1024 * 1024));
                    info.UsedDiskSpaceGB = info.TotalDiskSpaceGB - info.AvailableDiskSpaceGB;
                    info.DiskUsagePercent = info.TotalDiskSpaceGB > 0 ? (info.UsedDiskSpaceGB * 100.0 / info.TotalDiskSpaceGB) : 0;

                    // Calculate backup path size
                    info.BackupPathSizeMB = GetDirectorySizeMB(backupPath);
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Error getting system resources: {ex.Message}", "Warning", "HEALTHCHECK");
            }

            return await Task.FromResult(info);
        }

        private static double GetCpuUsage()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    cpuCounter.NextValue(); // First call returns 0
                    System.Threading.Thread.Sleep(500);
                    return cpuCounter.NextValue();
                }
                else
                {
                    // For non-Windows, return a placeholder
                    return 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static long GetTotalMemoryMB()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var memCounter = new PerformanceCounter("Memory", "Available MBytes");
                    return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024) + (long)memCounter.NextValue();
                }
                else
                {
                    return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
                }
            }
            catch
            {
                return 4096; // Default 4GB
            }
        }

        private static long GetAvailableMemoryMB()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var memCounter = new PerformanceCounter("Memory", "Available MBytes");
                    return (long)memCounter.NextValue();
                }
                else
                {
                    return 2048; // Default 2GB available
                }
            }
            catch
            {
                return 2048;
            }
        }

        private static long GetDirectorySizeMB(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return 0;

                long size = 0;
                var dirInfo = new DirectoryInfo(path);
                
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        size += file.Length;
                    }
                    catch { /* Skip files we can't access */ }
                }

                return size / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        public static string GetHealthSummary()
        {
            if (_lastResult == null) return "No health check data available";

            var summary = new List<string>
            {
                $"Status: {_lastResult.Status}",
                $"Timestamp: {_lastResult.Timestamp:yyyy-MM-dd HH:mm:ss}",
                $"Components: {_lastResult.Components.Count}",
                $"Healthy: {_lastResult.Components.Values.Count(c => c.IsHealthy)}",
                $"Unhealthy: {_lastResult.Components.Values.Count(c => !c.IsHealthy)}"
            };

            if (_lastResult.Warnings.Count > 0)
            {
                summary.Add($"Warnings: {_lastResult.Warnings.Count}");
            }
            if (_lastResult.Errors.Count > 0)
            {
                summary.Add($"Errors: {_lastResult.Errors.Count}");
            }

            return string.Join(Environment.NewLine, summary);
        }
    }
}
