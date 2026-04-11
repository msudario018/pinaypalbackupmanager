using Firebase.Database;
using Firebase.Database.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace PinayPalBackupManager.Services
{
    public static class RealtimeMonitoringService
    {
        private static FirebaseClient? _database;
        private static string? _username;
        private static bool _isInitialized = false;
        private static System.Timers.Timer? _connectionTimer;
        private static System.Timers.Timer? _systemMonitoringTimer;
        private static TimeSpan _lastCpuTime = TimeSpan.Zero;
        private static DateTime _lastCpuSampleTime = DateTime.MinValue;
        private static DateTime _appStartTime = DateTime.MinValue;

        public static void Initialize(string databaseUrl, string username)
        {
            try
            {
                _database = new FirebaseClient(databaseUrl);
                _username = username;
                _isInitialized = true;
                _appStartTime = DateTime.UtcNow;

                // Start connection status monitoring (heartbeat every 30 seconds as per Flutter requirements)
                _connectionTimer = new System.Timers.Timer(30000); // 30 seconds
                _connectionTimer.Elapsed += async (sender, e) => await UpdateConnectionStatusAsync();
                _connectionTimer.AutoReset = true;
                _connectionTimer.Start();

                // Start system monitoring (every 5 seconds for real-time monitoring)
                _systemMonitoringTimer = new System.Timers.Timer(5000); // 5 seconds
                _systemMonitoringTimer.Elapsed += async (sender, e) => await UpdateSystemMonitoringAsync();
                _systemMonitoringTimer.AutoReset = true;
                _systemMonitoringTimer.Start();

                LogService.WriteSystemLog($"[REALTIME_MONITORING] Initialized for user: {username}", "Information", "SYSTEM");

                // Initial updates
                _ = Task.Run(async () => {
                    await UpdateConnectionStatusAsync();
                    await UpdateSystemMonitoringAsync();
                    await SyncBackupFilesAsync();
                    await AddActivityAsync("info", "Real-time monitoring service started");
                });
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Initialization failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static async Task UpdateConnectionStatusAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var connectionData = new
                {
                    status = "online",
                    lastSeen = DateTime.UtcNow.ToString("o"),
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("connection")
                    .PutAsync(connectionData);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Connection status update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task SetConnectionStatusOfflineAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var connectionData = new
                {
                    status = "offline",
                    lastSeen = DateTime.UtcNow.ToString("o"),
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("connection")
                    .PutAsync(connectionData);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to set connection status to offline: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static async Task UpdateSystemMonitoringAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var cpuUsage = await GetCpuUsageAsync();
                var memoryUsage = await GetMemoryUsageAsync();

                var systemStatsData = new
                {
                    cpu = cpuUsage,
                    memory = memoryUsage,
                    pcAppUptime = await GetPcAppUptimeAsync(),
                    uptime = await GetSystemUptimeAsync()
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_stats")
                    .PutAsync(systemStatsData);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] System monitoring update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static async Task<string> GetCpuUsageAsync()
        {
            try
            {
                // Use improved CPU measurement with minimal delay
                var startTime = DateTime.UtcNow;
                var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                
                await Task.Delay(100); // 100ms delay for accurate measurement
                
                var endTime = DateTime.UtcNow;
                var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                
                // Store for fallback
                _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
                _lastCpuSampleTime = DateTime.UtcNow;
                
                return $"{Math.Min(cpuUsageTotal * 100, 100):F0}%";
            }
            catch
            {
                return "0%";
            }
        }

        private static async Task<string> GetMemoryUsageAsync()
        {
            try
            {
                // Get memory usage using WorkingSet64 (process memory)
                var process = Process.GetCurrentProcess();
                var workingSetMB = process.WorkingSet64 / (1024 * 1024);
                
                // Estimate total system memory using GC
                var totalMemoryMB = GC.GetTotalMemory(true) / (1024 * 1024);
                var systemMemoryMB = Math.Max(workingSetMB, totalMemoryMB) * 4; // Rough estimate
                
                var memoryUsagePercent = (workingSetMB / systemMemoryMB) * 100;
                
                return $"{Math.Min(memoryUsagePercent, 100):F0}%";
            }
            catch
            {
                return "0%";
            }
        }

        private static async Task<string> GetSystemUptimeAsync()
        {
            try
            {
                // Use PerformanceCounter for accurate system uptime
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                var uptimeSeconds = (int)uptime.TotalSeconds;
                var uptimeMinutes = uptimeSeconds / 60;
                var uptimeHours = uptimeMinutes / 60;
                var uptimeDays = uptimeHours / 24;

                if (uptimeDays > 0)
                    return $"{uptimeDays}d {uptimeHours % 24}h";
                else if (uptimeHours > 0)
                    return $"{uptimeHours}h {uptimeMinutes % 60}m";
                else if (uptimeMinutes > 0)
                    return $"{uptimeMinutes}m {uptimeSeconds % 60}s";
                else
                    return $"{uptimeSeconds}s";
            }
            catch
            {
                return "0s";
            }
        }

        private static async Task<string> GetPcAppUptimeAsync()
        {
            try
            {
                if (_appStartTime == DateTime.MinValue)
                    return "0s";

                var uptime = DateTime.UtcNow - _appStartTime;
                var uptimeSeconds = (int)uptime.TotalSeconds;
                var uptimeMinutes = uptimeSeconds / 60;
                var uptimeHours = uptimeMinutes / 60;
                var uptimeDays = uptimeHours / 24;

                if (uptimeDays > 0)
                    return $"{uptimeDays}d {uptimeHours % 24}h";
                else if (uptimeHours > 0)
                    return $"{uptimeHours}h {uptimeMinutes % 60}m";
                else if (uptimeMinutes > 0)
                    return $"{uptimeMinutes}m {uptimeSeconds % 60}s";
                else
                    return $"{uptimeSeconds}s";
            }
            catch
            {
                return "0s";
            }
        }

        public static async Task AddActivityAsync(string type, string message)
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var timestamp = DateTime.UtcNow.Ticks.ToString();
                var activityRef = _database
                    .Child("users")
                    .Child(_username)
                    .Child("activity")
                    .Child(timestamp);

                var activityData = new
                {
                    type = type,
                    message = message,
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                await activityRef.PutAsync(activityData);
                
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Activity added: {message}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to add activity: {ex.Message}", "Error", "SYSTEM");
            }
        }

        // Legacy overload for backward compatibility
        public static async Task AddActivityAsync(string type, string service, string message)
        {
            await AddActivityAsync(type, message);
        }

        public static async Task UpdateBackupProgressAsync(int percentage, string currentFile, int totalFiles, int completedFiles)
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var progressData = new
                {
                    percentage = percentage,
                    currentFile = currentFile,
                    totalFiles = totalFiles,
                    completedFiles = completedFiles
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("backup_progress")
                    .PutAsync(progressData);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to update backup progress: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task ClearBackupProgressAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("backup_progress")
                    .DeleteAsync();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to clear backup progress: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task AddBackupHistoryAsync(string backupId, string date, string size, string duration, string status)
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var backupData = new
                {
                    id = backupId,
                    date = date,
                    size = size,
                    duration = duration,
                    status = status
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("backups")
                    .Child(backupId)
                    .PutAsync(backupData);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to add backup history: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task UpdateStorageUsageAsync(double used, double total, double usedPercentage)
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var storageData = new
                {
                    used = used,
                    total = total,
                    usedPercentage = usedPercentage
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("storage")
                    .PutAsync(storageData);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to update storage usage: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task AddBackupFileAsync(string name, string size, string date, string downloadUrl, string category)
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var fileData = new
                {
                    name = name,
                    size = size,
                    date = date,
                    downloadUrl = downloadUrl,
                    category = category
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("backup_files")
                    .Child(name)
                    .PutAsync(fileData);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to add backup file: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task RemoveBackupFileAsync(string name)
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("backup_files")
                    .Child(name)
                    .DeleteAsync();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to remove backup file: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task SyncBackupFilesAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                LogService.WriteSystemLog("[REALTIME_MONITORING] SyncBackupFilesAsync: Service not initialized or username is null", "Warning", "SYSTEM");
                return;
            }

            try
            {
                var backupFolders = new List<string>();
                
                // Log folder configuration
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Checking backup folders...", "Information", "SYSTEM");
                LogService.WriteSystemLog($"[REALTIME_MONITORING] FTP Folder: {Models.BackupConfig.FtpLocalFolder} (Exists: {System.IO.Directory.Exists(Models.BackupConfig.FtpLocalFolder)})", "Information", "SYSTEM");
                LogService.WriteSystemLog($"[REALTIME_MONITORING] SQL Folder: {Models.BackupConfig.SqlLocalFolder} (Exists: {System.IO.Directory.Exists(Models.BackupConfig.SqlLocalFolder)})", "Information", "SYSTEM");
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Mailchimp Folder: {Models.BackupConfig.MailchimpFolder} (Exists: {System.IO.Directory.Exists(Models.BackupConfig.MailchimpFolder)})", "Information", "SYSTEM");
                
                // Add backup folders if they exist
                if (!string.IsNullOrEmpty(Models.BackupConfig.FtpLocalFolder) && System.IO.Directory.Exists(Models.BackupConfig.FtpLocalFolder))
                    backupFolders.Add(Models.BackupConfig.FtpLocalFolder);
                
                if (!string.IsNullOrEmpty(Models.BackupConfig.SqlLocalFolder) && System.IO.Directory.Exists(Models.BackupConfig.SqlLocalFolder))
                    backupFolders.Add(Models.BackupConfig.SqlLocalFolder);
                
                if (!string.IsNullOrEmpty(Models.BackupConfig.MailchimpFolder) && System.IO.Directory.Exists(Models.BackupConfig.MailchimpFolder))
                    backupFolders.Add(Models.BackupConfig.MailchimpFolder);

                LogService.WriteSystemLog($"[REALTIME_MONITORING] Found {backupFolders.Count} valid backup folders to sync", "Information", "SYSTEM");

                foreach (var folder in backupFolders)
                {
                    try
                    {
                        var files = System.IO.Directory.GetFiles(folder, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                            .Where(f => !System.IO.Path.GetFileName(f).Equals("backuplog.txt", StringComparison.OrdinalIgnoreCase) &&
                                       !System.IO.Path.GetFileName(f).Equals("backup_log.txt", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        LogService.WriteSystemLog($"[REALTIME_MONITORING] Found {files.Count} files in {folder}", "Information", "SYSTEM");

                        // Determine category based on folder
                        string category = "other";
                        if (folder.Equals(Models.BackupConfig.FtpLocalFolder, StringComparison.OrdinalIgnoreCase))
                            category = "website";
                        else if (folder.Equals(Models.BackupConfig.SqlLocalFolder, StringComparison.OrdinalIgnoreCase))
                            category = "sql";
                        else if (folder.Equals(Models.BackupConfig.MailchimpFolder, StringComparison.OrdinalIgnoreCase))
                            category = "mailchimp";

                        foreach (var file in files)
                        {
                            var fileInfo = new System.IO.FileInfo(file);
                            var sizeBytes = fileInfo.Length;
                            var sizeMB = (sizeBytes / (1024.0 * 1024.0)).ToString("F2") + " MB";
                            var date = fileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss");
                            var downloadUrl = ""; // Can be added later if needed

                            // Encode filename to make it Firebase-safe (replace dots with underscores)
                            var firebaseSafeName = fileInfo.Name.Replace(".", "_");
                            await AddBackupFileAsync(firebaseSafeName, sizeMB, date, downloadUrl, category);
                            LogService.WriteSystemLog($"[REALTIME_MONITORING] Synced file: {fileInfo.Name} ({sizeMB}, category: {category})", "Information", "SYSTEM");
                        }

                        LogService.WriteSystemLog($"[REALTIME_MONITORING] Synced {files.Count} files from {folder} (category: {category})", "Information", "SYSTEM");
                    }
                    catch (Exception ex)
                    {
                        LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to sync files from {folder}: {ex.Message}", "Warning", "SYSTEM");
                    }
                }
                
                if (backupFolders.Count == 0)
                {
                    LogService.WriteSystemLog("[REALTIME_MONITORING] No backup folders found - folders may not be configured in settings", "Warning", "SYSTEM");
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to sync backup files: {ex.Message}", "Error", "SYSTEM");
            }
        }

        // Legacy methods for remote control system (backward compatibility)
        public static async Task UpdateCommandStatusAsync(string commandId, string type, string status, int progress = 0, string message = "", string currentFile = "", string transferSpeed = "", string eta = "")
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var commandData = new
                {
                    type = type,
                    status = status,
                    progress = progress,
                    message = message,
                    currentFile = currentFile,
                    transferSpeed = transferSpeed,
                    eta = eta,
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("commands")
                    .Child(commandId)
                    .PutAsync(commandData);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to update command status: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task AddLogAsync(string severity, string message, string category = "SYSTEM")
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var timestamp = DateTime.UtcNow.Ticks.ToString();
                var logRef = _database
                    .Child("users")
                    .Child(_username)
                    .Child("activity")
                    .Child(timestamp);

                var logData = new
                {
                    type = "log",
                    severity = severity,
                    message = message,
                    category = category,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    id = timestamp
                };

                await logRef.PutAsync(logData);
                
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Log added: [{severity}] {message}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to add log: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static void Stop()
        {
            try
            {
                LogService.WriteSystemLog("[REALTIME_MONITORING] Stopping real-time monitoring services...", "Information", "SYSTEM");

                // Update connection status to offline before stopping
                _ = Task.Run(async () =>
                {
                    await SetConnectionStatusOfflineAsync();
                    await AddActivityAsync("info", "Real-time monitoring service stopped");
                });

                // Stop all timers
                _connectionTimer?.Stop();
                _connectionTimer?.Dispose();
                _connectionTimer = null;

                _systemMonitoringTimer?.Stop();
                _systemMonitoringTimer?.Dispose();
                _systemMonitoringTimer = null;

                // Mark as uninitialized
                _isInitialized = false;

                LogService.WriteSystemLog("[REALTIME_MONITORING] All monitoring services stopped", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Error stopping services: {ex.Message}", "Error", "SYSTEM");
            }
        }
    }
}
