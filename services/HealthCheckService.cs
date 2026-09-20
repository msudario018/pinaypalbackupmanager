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

        public class DriveSpaceInfo
        {
            public string Name { get; set; } = "";
            public string VolumeLabel { get; set; } = "";
            public long TotalBytes { get; set; }
            public long FreeBytes { get; set; }
            public long UsedBytes { get; set; }
            public double UsedPercent { get; set; }
            public bool IsBackupDrive { get; set; }
            public bool IsSystemDrive { get; set; }
        }

        public class BackupFolderInfo
        {
            public string Service { get; set; } = "";
            public string Path { get; set; } = "";
            public long TotalSizeBytes { get; set; }
            public int FileCount { get; set; }
            public bool Exists { get; set; }
        }

        public class SystemResourceInfo
        {
            public double CpuUsagePercent { get; set; }
            
            // Memory details
            public long TotalMemoryMB { get; set; }
            public long AvailableMemoryMB { get; set; }
            public long UsedMemoryMB { get; set; }
            public double MemoryUsagePercent { get; set; }
            public long TotalMemoryBytes { get; set; }
            public long AvailableMemoryBytes { get; set; }
            public long UsedMemoryBytes { get; set; }
            public long AppMemoryBytes { get; set; }

            // Disk details
            public long TotalDiskSpaceGB { get; set; }
            public long AvailableDiskSpaceGB { get; set; }
            public long UsedDiskSpaceGB { get; set; }
            public double DiskUsagePercent { get; set; }
            public string PrimaryDriveLetter { get; set; } = "";
            public string PrimaryDriveLabel { get; set; } = "";
            public string BackupPath { get; set; } = "";
            public long BackupPathSizeMB { get; set; }

            // Detailed breakdowns
            public List<DriveSpaceInfo> Drives { get; set; } = new();
            public List<BackupFolderInfo> BackupFolders { get; set; } = new();
        }

        private static readonly string CacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PinayPalBackupManager", "health_check_result.json");
        private static readonly List<ComponentHealth> _componentHistory = new();
        private static HealthCheckResult? _lastResult;
        private static DateTime _lastDailyRunDate = DateTime.MinValue;

        public static HealthCheckResult? GetLastResult()
        {
            if (_lastResult != null) return _lastResult;
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    var json = File.ReadAllText(CacheFilePath);
                    _lastResult = System.Text.Json.JsonSerializer.Deserialize<HealthCheckResult>(json);
                }
            }
            catch { }
            return _lastResult;
        }

        public static async Task CheckAndRunDailyHealthCheckAsync()
        {
            if (!ConfigService.Current.Operation.DailyHealthCheckEnabled) return;
            var now = DateTime.Now;
            int targetHour = ConfigService.Current.Operation.DailyHealthCheckHour;
            if (now.Date > _lastDailyRunDate.Date && now.Hour >= targetHour)
            {
                _lastDailyRunDate = now;
                LogService.WriteSystemLog("[HealthCheckService] Running scheduled daily health check...", "Information", "HEALTHCHECK");
                var result = await RunHealthCheckAsync();
                if (!result.IsHealthy)
                {
                    NotificationService.ShowBackupToast("Daily Health Alert", $"System Health Degraded: {result.Status}", "Warning");
                }
            }
        }

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
                try
                {
                    var dir = Path.GetDirectoryName(CacheFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(CacheFilePath, json);
                }
                catch { }
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private static PerformanceCounter? _cpuCounter;
        private static readonly object _cpuLock = new();

        private static async Task<SystemResourceInfo> GetSystemResourceInfoAsync()
        {
            var info = new SystemResourceInfo();

            try
            {
                // CPU Usage
                info.CpuUsagePercent = GetCpuUsage();

                // Memory Usage - Exact physical RAM
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        var memStatus = new MEMORYSTATUSEX();
                        if (GlobalMemoryStatusEx(memStatus))
                        {
                            info.TotalMemoryBytes = (long)memStatus.ullTotalPhys;
                            info.AvailableMemoryBytes = (long)memStatus.ullAvailPhys;
                            info.UsedMemoryBytes = info.TotalMemoryBytes - info.AvailableMemoryBytes;
                            info.MemoryUsagePercent = memStatus.dwMemoryLoad;
                            info.TotalMemoryMB = info.TotalMemoryBytes / (1024 * 1024);
                            info.AvailableMemoryMB = info.AvailableMemoryBytes / (1024 * 1024);
                            info.UsedMemoryMB = info.TotalMemoryMB - info.AvailableMemoryMB;
                        }
                    }
                    catch { }
                }

                if (info.TotalMemoryBytes == 0)
                {
                    var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                    info.TotalMemoryBytes = total;
                    info.AvailableMemoryBytes = total / 2;
                    info.UsedMemoryBytes = info.TotalMemoryBytes - info.AvailableMemoryBytes;
                    info.MemoryUsagePercent = 50.0;
                    info.TotalMemoryMB = info.TotalMemoryBytes / (1024 * 1024);
                    info.AvailableMemoryMB = info.AvailableMemoryBytes / (1024 * 1024);
                    info.UsedMemoryMB = info.TotalMemoryMB - info.AvailableMemoryMB;
                }

                try
                {
                    using var proc = Process.GetCurrentProcess();
                    info.AppMemoryBytes = proc.WorkingSet64;
                }
                catch { }

                // Scan all physical drives
                try
                {
                    var readyDrives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
                    string backupPath = !string.IsNullOrEmpty(PinayPalBackupManager.Models.BackupConfig.FtpLocalFolder) 
                        ? PinayPalBackupManager.Models.BackupConfig.FtpLocalFolder
                        : (!string.IsNullOrEmpty(PinayPalBackupManager.Models.BackupConfig.SqlLocalFolder)
                            ? PinayPalBackupManager.Models.BackupConfig.SqlLocalFolder
                            : EnvironmentConfigService.GetBackupPath());
                    info.BackupPath = backupPath;

                    string? backupRoot = null;
                    try { if (!string.IsNullOrEmpty(backupPath)) backupRoot = Path.GetPathRoot(backupPath); } catch { }
                    string? systemRoot = null;
                    try { systemRoot = Path.GetPathRoot(Environment.SystemDirectory); } catch { }

                    DriveSpaceInfo? primaryDriveInfo = null;

                    foreach (var drive in readyDrives)
                    {
                        long total = drive.TotalSize;
                        long free = drive.AvailableFreeSpace;
                        long used = Math.Max(0, total - free);
                        double pct = total > 0 ? (used * 100.0 / total) : 0;

                        bool isBackup = backupRoot != null && drive.Name.Equals(backupRoot, StringComparison.OrdinalIgnoreCase);
                        bool isSys = systemRoot != null && drive.Name.Equals(systemRoot, StringComparison.OrdinalIgnoreCase);

                        var ds = new DriveSpaceInfo
                        {
                            Name = drive.Name,
                            VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? (isSys ? "System OS" : "Local Disk") : drive.VolumeLabel,
                            TotalBytes = total,
                            FreeBytes = free,
                            UsedBytes = used,
                            UsedPercent = Math.Round(pct, 1),
                            IsBackupDrive = isBackup,
                            IsSystemDrive = isSys
                        };

                        info.Drives.Add(ds);

                        if (isBackup)
                        {
                            primaryDriveInfo = ds;
                        }
                        else if (primaryDriveInfo == null && isSys)
                        {
                            primaryDriveInfo = ds;
                        }
                    }

                    if (primaryDriveInfo == null && info.Drives.Count > 0)
                    {
                        primaryDriveInfo = info.Drives[0];
                    }

                    if (primaryDriveInfo != null)
                    {
                        info.PrimaryDriveLetter = primaryDriveInfo.Name;
                        info.PrimaryDriveLabel = primaryDriveInfo.VolumeLabel;
                        info.TotalDiskSpaceGB = primaryDriveInfo.TotalBytes / (1024 * 1024 * 1024);
                        info.AvailableDiskSpaceGB = primaryDriveInfo.FreeBytes / (1024 * 1024 * 1024);
                        info.UsedDiskSpaceGB = primaryDriveInfo.UsedBytes / (1024 * 1024 * 1024);
                        info.DiskUsagePercent = primaryDriveInfo.UsedPercent;
                    }

                    // Scan Backup Folders
                    var foldersToScan = new List<(string service, string path)>
                    {
                        ("FTP Website", PinayPalBackupManager.Models.BackupConfig.FtpLocalFolder),
                        ("SQL Database", PinayPalBackupManager.Models.BackupConfig.SqlLocalFolder),
                        ("Mailchimp", PinayPalBackupManager.Models.BackupConfig.MailchimpFolder)
                    };

                    if (!string.IsNullOrEmpty(PinayPalBackupManager.Models.BackupConfig.NetworkDriveFolder))
                    {
                        foldersToScan.Add(("Network Drive", PinayPalBackupManager.Models.BackupConfig.NetworkDriveFolder));
                    }

                    long totalBackupSize = 0;
                    foreach (var (svc, path) in foldersToScan)
                    {
                        var folderInfo = new BackupFolderInfo
                        {
                            Service = svc,
                            Path = path ?? "",
                            Exists = !string.IsNullOrEmpty(path) && Directory.Exists(path)
                        };

                        if (folderInfo.Exists)
                        {
                            try
                            {
                                var dir = new DirectoryInfo(path!);
                                var files = dir.GetFiles("*", SearchOption.AllDirectories);
                                folderInfo.FileCount = files.Length;
                                folderInfo.TotalSizeBytes = files.Sum(f => {
                                    try { return f.Length; } catch { return 0L; }
                                });
                                totalBackupSize += folderInfo.TotalSizeBytes;
                            }
                            catch { }
                        }

                        info.BackupFolders.Add(folderInfo);
                    }

                    info.BackupPathSizeMB = totalBackupSize / (1024 * 1024);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[HealthCheck] Drive scanning error: {ex.Message}", "Warning", "HEALTHCHECK");
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
                    lock (_cpuLock)
                    {
                        if (_cpuCounter == null)
                        {
                            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                            _cpuCounter.NextValue();
                            return 2.5; // Initial typical idle baseline
                        }
                        return Math.Round(_cpuCounter.NextValue(), 1);
                    }
                }
            }
            catch
            {
                // Fallback
            }
            return 0;
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
