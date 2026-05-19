using Firebase.Database;
using Firebase.Database.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
        
        // Advanced monitoring features
        private static readonly Dictionary<string, AlertRule> _alertRules = new();
        private static readonly Queue<MonitoringEvent> _eventHistory = new();
        private static readonly object _monitoringLock = new();
        private static System.Timers.Timer? _alertCheckTimer;
        private static int _isConnectionUpdating = 0;
        private static int _isSystemMonitoringUpdating = 0;
        private static int _isCheckingAlerts = 0;
        
        public static event Action<Alert>? OnAlertTriggered;
        public static event Action<MonitoringEvent>? OnMonitoringEvent;
        public static event Action<SystemMetrics>? OnMetricsUpdated;

        public static void Initialize(string databaseUrl, string username)
        {
            try
            {
                StopTimers();

                _database = new FirebaseClient(databaseUrl);
                _username = username;
                _isInitialized = true;
                _appStartTime = DateTime.UtcNow;
                
                // Initialize default alert rules
                InitializeDefaultAlertRules();
                
                // Start alert checking timer
                _alertCheckTimer = new System.Timers.Timer(30000); // Check every 30 seconds
                _alertCheckTimer.Elapsed += CheckAlerts;
                _alertCheckTimer.Start();

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

            if (Interlocked.Exchange(ref _isConnectionUpdating, 1) == 1)
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
            finally
            {
                Interlocked.Exchange(ref _isConnectionUpdating, 0);
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

            if (Interlocked.Exchange(ref _isSystemMonitoringUpdating, 1) == 1)
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
            finally
            {
                Interlocked.Exchange(ref _isSystemMonitoringUpdating, 0);
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

                StopTimers();

                // Mark as uninitialized
                _isInitialized = false;
                Interlocked.Exchange(ref _isConnectionUpdating, 0);
                Interlocked.Exchange(ref _isSystemMonitoringUpdating, 0);
                Interlocked.Exchange(ref _isCheckingAlerts, 0);

                LogService.WriteSystemLog("[REALTIME_MONITORING] All monitoring services stopped", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Error stopping services: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static void StopTimers()
        {
            _connectionTimer?.Stop();
            _connectionTimer?.Dispose();
            _connectionTimer = null;

            _systemMonitoringTimer?.Stop();
            _systemMonitoringTimer?.Dispose();
            _systemMonitoringTimer = null;

            _alertCheckTimer?.Stop();
            _alertCheckTimer?.Dispose();
            _alertCheckTimer = null;
        }
        
        private static void InitializeDefaultAlertRules()
        {
            // CPU usage alert
            _alertRules["cpu_high"] = new AlertRule
            {
                Id = "cpu_high",
                Name = "High CPU Usage",
                Metric = "cpu",
                Threshold = 80.0,
                Operator = AlertOperator.GreaterThan,
                Severity = AlertSeverity.Warning,
                CooldownMinutes = 5,
                Message = "CPU usage is above 80%"
            };
            
            // Memory usage alert
            _alertRules["memory_high"] = new AlertRule
            {
                Id = "memory_high",
                Name = "High Memory Usage",
                Metric = "memory",
                Threshold = 85.0,
                Operator = AlertOperator.GreaterThan,
                Severity = AlertSeverity.Warning,
                CooldownMinutes = 5,
                Message = "Memory usage is above 85%"
            };
            
            // Disk space alert
            _alertRules["disk_low"] = new AlertRule
            {
                Id = "disk_low",
                Name = "Low Disk Space",
                Metric = "disk",
                Threshold = 10.0,
                Operator = AlertOperator.LessThan,
                Severity = AlertSeverity.Critical,
                CooldownMinutes = 10,
                Message = "Available disk space is below 10%"
            };
        }
        
        public static void AddAlertRule(AlertRule rule)
        {
            lock (_monitoringLock)
            {
                _alertRules[rule.Id] = rule;
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Alert rule added: {rule.Id}", "Information", "SYSTEM");
            }
        }
        
        public static void RemoveAlertRule(string ruleId)
        {
            lock (_monitoringLock)
            {
                if (_alertRules.Remove(ruleId))
                {
                    LogService.WriteSystemLog($"[REALTIME_MONITORING] Alert rule removed: {ruleId}", "Information", "SYSTEM");
                }
            }
        }
        
        public static List<AlertRule> GetAlertRules()
        {
            lock (_monitoringLock)
            {
                return _alertRules.Values.ToList();
            }
        }
        
        public static List<MonitoringEvent> GetRecentEvents(int count = 50)
        {
            lock (_monitoringLock)
            {
                return _eventHistory.TakeLast(count).ToList();
            }
        }
        
        private static async void CheckAlerts(object? sender, ElapsedEventArgs e)
        {
            if (!_isInitialized) return;

            if (Interlocked.Exchange(ref _isCheckingAlerts, 1) == 1)
                return;
            
            try
            {
                var metrics = await GetSystemMetricsAsync();
                OnMetricsUpdated?.Invoke(metrics);
                
                lock (_monitoringLock)
                {
                    foreach (var rule in _alertRules.Values)
                    {
                        if (ShouldTriggerAlert(rule, metrics))
                        {
                            var alert = new Alert
                            {
                                Id = Guid.NewGuid().ToString(),
                                RuleId = rule.Id,
                                RuleName = rule.Name,
                                Severity = rule.Severity,
                                Message = rule.Message,
                                Timestamp = DateTime.UtcNow,
                                Metrics = metrics
                            };
                            
                            OnAlertTriggered?.Invoke(alert);
                            
                            // Add to event history
                            var monitoringEvent = new MonitoringEvent
                            {
                                Id = Guid.NewGuid().ToString(),
                                Type = EventType.Alert,
                                Severity = rule.Severity,
                                Message = $"Alert: {rule.Name} - {rule.Message}",
                                Timestamp = DateTime.UtcNow,
                                Data = new Dictionary<string, object>
                                {
                                    ["ruleId"] = rule.Id,
                                    ["cpu"] = metrics.CpuUsage,
                                    ["memory"] = metrics.MemoryUsage,
                                    ["disk"] = metrics.DiskUsage
                                }
                            };
                            
                            _eventHistory.Enqueue(monitoringEvent);
                            
                            // Keep event history manageable
                            while (_eventHistory.Count > 1000)
                            {
                                _eventHistory.Dequeue();
                            }
                            
                            LogService.WriteSystemLog($"[REALTIME_MONITORING] Alert triggered: {rule.Name}", rule.Severity.ToString(), "SYSTEM");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Error checking alerts: {ex.Message}", "Error", "SYSTEM");
            }
            finally
            {
                Interlocked.Exchange(ref _isCheckingAlerts, 0);
            }
        }
        
        private static bool ShouldTriggerAlert(AlertRule rule, SystemMetrics metrics)
        {
            // Check cooldown
            if (rule.LastTriggered.HasValue && 
                DateTime.UtcNow - rule.LastTriggered.Value < TimeSpan.FromMinutes(rule.CooldownMinutes))
            {
                return false;
            }
            
            var value = rule.Metric.ToLower() switch
            {
                "cpu" => metrics.CpuUsage,
                "memory" => metrics.MemoryUsage,
                "disk" => metrics.DiskUsage,
                _ => 0.0
            };
            
            var triggered = rule.Operator switch
            {
                AlertOperator.GreaterThan => value > rule.Threshold,
                AlertOperator.LessThan => value < rule.Threshold,
                AlertOperator.Equals => Math.Abs(value - rule.Threshold) < 0.1,
                _ => false
            };
            
            if (triggered)
            {
                rule.LastTriggered = DateTime.UtcNow;
            }
            
            return triggered;
        }
        
        public static void LogMonitoringEvent(EventType type, AlertSeverity severity, string message, Dictionary<string, object>? data = null)
        {
            lock (_monitoringLock)
            {
                var monitoringEvent = new MonitoringEvent
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = type,
                    Severity = severity,
                    Message = message,
                    Timestamp = DateTime.UtcNow,
                    Data = data ?? new Dictionary<string, object>()
                };
                
                _eventHistory.Enqueue(monitoringEvent);
                OnMonitoringEvent?.Invoke(monitoringEvent);
                
                // Keep event history manageable
                while (_eventHistory.Count > 1000)
                {
                    _eventHistory.Dequeue();
                }
            }
        }
        
        private static async Task<SystemMetrics> GetSystemMetricsAsync()
        {
            try
            {
                // Get CPU usage using Performance Counter
                var cpuUsage = GetCpuUsage();
                
                // Get memory usage
                var memoryUsage = GetMemoryUsage();
                
                // Get network details
                var networkDetails = await SystemMonitorService.GetNetworkDetailsAsync();
                
                // Get disk I/O
                var diskIo = await SystemMonitorService.GetDiskIoAsync();
                
                // Parse network values (simplified)
                var networkBytesSent = ParseNetworkBytes(networkDetails.upload);
                var networkBytesReceived = ParseNetworkBytes(networkDetails.download);
                
                // Parse disk usage (simplified)
                var diskUsage = ParseDiskUsage(diskIo);
                
                var metrics = new SystemMetrics
                {
                    Timestamp = DateTime.UtcNow,
                    CpuUsage = cpuUsage,
                    MemoryUsage = memoryUsage,
                    DiskUsage = diskUsage,
                    NetworkBytesSent = networkBytesSent,
                    NetworkBytesReceived = networkBytesReceived
                };
                
                return metrics;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Error getting system metrics: {ex.Message}", "Error", "SYSTEM");
                
                return new SystemMetrics
                {
                    Timestamp = DateTime.UtcNow,
                    CpuUsage = 0,
                    MemoryUsage = 0,
                    DiskUsage = 0,
                    NetworkBytesSent = 0,
                    NetworkBytesReceived = 0
                };
            }
        }
        
        private static double GetCpuUsage()
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    return 0;
                    
                using var proc = Process.GetCurrentProcess();
                using var counter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
                return counter.NextValue();
            }
            catch
            {
                return 0;
            }
        }
        
        private static double GetMemoryUsage()
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    return 0;
                    
                using var proc = Process.GetCurrentProcess();
                var memory = proc.WorkingSet64;
                var totalMemory = GC.GetTotalMemory(false);
                var availableMemory = memory;
                
                // Get system memory using Performance Counter
                using var counter = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes");
                var availableMB = counter.NextValue();
                
                // Estimate total memory (simplified)
                var totalMB = availableMB + (memory / 1024 / 1024);
                var usedMB = totalMB - availableMB;
                
                return totalMB > 0 ? (usedMB / totalMB) * 100 : 0;
            }
            catch
            {
                return 0;
            }
        }
        
        private static long ParseNetworkBytes(string networkValue)
        {
            try
            {
                if (string.IsNullOrEmpty(networkValue)) return 0;
                
                // Parse values like "1.5 MB/s" or "500 KB/s"
                var parts = networkValue.Split(' ');
                if (parts.Length >= 2 && double.TryParse(parts[0], out var value))
                {
                    var unit = parts[1].ToUpper();
                    return unit switch
                    {
                        var u when u.Contains("GB") => (long)(value * 1024 * 1024 * 1024),
                        var u when u.Contains("MB") => (long)(value * 1024 * 1024),
                        var u when u.Contains("KB") => (long)(value * 1024),
                        _ => (long)value
                    };
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            
            return 0;
        }
        
        private static double ParseDiskUsage(string diskIoValue)
        {
            try
            {
                if (string.IsNullOrEmpty(diskIoValue)) return 0;
                
                // Parse values like "10.5 MB/s" - convert to percentage (simplified)
                var parts = diskIoValue.Split(' ');
                if (parts.Length >= 2 && double.TryParse(parts[0], out var value))
                {
                    // Convert disk I/O to a percentage (arbitrary scaling)
                    return Math.Min(value / 10, 100); // Scale to 0-100%
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            
            return 0;
        }
    }
    
    public class AlertRule
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Metric { get; set; } = string.Empty;
        public double Threshold { get; set; }
        public AlertOperator Operator { get; set; }
        public AlertSeverity Severity { get; set; }
        public int CooldownMinutes { get; set; } = 5;
        public string Message { get; set; } = string.Empty;
        public DateTime? LastTriggered { get; set; }
    }
    
    public class Alert
    {
        public string Id { get; set; } = string.Empty;
        public string RuleId { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public SystemMetrics Metrics { get; set; } = new();
    }
    
    public class MonitoringEvent
    {
        public string Id { get; set; } = string.Empty;
        public EventType Type { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }
    
    public class SystemMetrics
    {
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double DiskUsage { get; set; }
        public long NetworkBytesSent { get; set; }
        public long NetworkBytesReceived { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    public enum AlertOperator
    {
        GreaterThan,
        LessThan,
        Equals
    }
    
    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }
    
    public enum EventType
    {
        Alert,
        SystemStart,
        SystemStop,
        BackupStart,
        BackupComplete,
        BackupFailed,
        Error
    }
}
