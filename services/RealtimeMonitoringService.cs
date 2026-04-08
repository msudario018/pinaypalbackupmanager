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
        private static System.Timers.Timer? _cpuHistoryTimer;
        private static List<double> _cpuHistory = new List<double>();

        public static void Initialize(string databaseUrl, string username)
        {
            try
            {
                _database = new FirebaseClient(databaseUrl);
                _username = username;
                _isInitialized = true;
                
                // Start connection status monitoring
                _connectionTimer = new System.Timers.Timer(10000); // 10 seconds
                _connectionTimer.Elapsed += async (sender, e) => await UpdateConnectionStatusAsync();
                _connectionTimer.AutoReset = true;
                _connectionTimer.Start();
                
                // Start system monitoring
                _systemMonitoringTimer = new System.Timers.Timer(5000); // 5 seconds
                _systemMonitoringTimer.Elapsed += async (sender, e) => await UpdateSystemMonitoringAsync();
                _systemMonitoringTimer.AutoReset = true;
                _systemMonitoringTimer.Start();
                
                // Start CPU history tracking
                _cpuHistoryTimer = new System.Timers.Timer(2000); // 2 seconds
                _cpuHistoryTimer.Elapsed += async (sender, e) => await UpdateCpuHistoryAsync();
                _cpuHistoryTimer.AutoReset = true;
                _cpuHistoryTimer.Start();
                
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Initialized for user: {username}", "Information", "SYSTEM");
                
                // Initial updates
                _ = Task.Run(async () => {
                    await UpdateConnectionStatusAsync();
                    await UpdateSystemMonitoringAsync();
                    await AddLogAsync("Info", "Real-time monitoring service started", "SYSTEM");
                    await AddLogAsync("Info", "PC application services initialized", "MAINWINDOW");
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

        private static async Task UpdateSystemMonitoringAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
                return;

            try
            {
                var monitoringData = new
                {
                    cpu_usage = await GetCpuUsageAsync(),
                    memory_usage = await GetMemoryUsageAsync(),
                    network_usage = await GetNetworkUsageAsync(),
                    disk_io = await GetDiskIoAsync(),
                    timestamp = DateTime.UtcNow.ToString("o"),
                    cpu_history = _cpuHistory.TakeLast(20).ToList(),
                    // Enhanced system info
                    total_memory = await GetTotalMemoryAsync(),
                    available_memory = await GetAvailableMemoryAsync(),
                    active_processes = await GetActiveProcessCountAsync(),
                    uptime = await GetSystemUptimeAsync()
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_monitoring")
                    .PutAsync(monitoringData);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] System monitoring update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static async Task UpdateCpuHistoryAsync()
        {
            try
            {
                var cpuUsage = await GetCpuUsageAsync();
                if (double.TryParse(cpuUsage.Replace("%", ""), out double value))
                {
                    _cpuHistory.Add(value);
                    if (_cpuHistory.Count > 20)
                    {
                        _cpuHistory.RemoveAt(0);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] CPU history update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static async Task<string> GetCpuUsageAsync()
        {
            try
            {
                // Use Process class to get CPU usage as a simpler alternative
                var process = Process.GetCurrentProcess();
                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime.TotalMilliseconds;
                
                await Task.Delay(1000); // Wait 1 second
                
                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime.TotalMilliseconds;
                
                var cpuUsedMs = endCpuUsage - startCpuUsage;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                
                return $"{cpuUsageTotal * 100:F1}%";
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
                var process = Process.GetCurrentProcess();
                var memoryUsage = process.WorkingSet64 / (1024 * 1024); // Convert to MB
                var totalMemory = GC.GetTotalMemory(false) / (1024 * 1024); // Rough estimate in MB
                var memoryUsagePercent = totalMemory > 0 ? (memoryUsage / totalMemory) * 100 : 0;
                return $"{memoryUsagePercent:F1}%";
            }
            catch
            {
                return "0%";
            }
        }

        private static async Task<string> GetNetworkUsageAsync()
        {
            try
            {
                // Simple network monitoring - return placeholder for now
                return "0 KB/s";
            }
            catch
            {
                return "0 KB/s";
            }
        }

        private static async Task<string> GetDiskIoAsync()
        {
            try
            {
                // Simple disk I/O monitoring - return placeholder for now
                return "0 MB/s";
            }
            catch
            {
                return "0 MB/s";
            }
        }

        private static async Task<string> GetTotalMemoryAsync()
        {
            try
            {
                var gc = GC.GetTotalMemory(false) / (1024 * 1024);
                return $"{gc} MB";
            }
            catch
            {
                return "0 MB";
            }
        }

        private static async Task<string> GetAvailableMemoryAsync()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64 / (1024 * 1024);
                var total = GC.GetTotalMemory(false) / (1024 * 1024);
                var available = total > workingSet ? total - workingSet : 0;
                return $"{available} MB";
            }
            catch
            {
                return "0 MB";
            }
        }

        private static async Task<int> GetActiveProcessCountAsync()
        {
            try
            {
                return Process.GetProcesses().Length;
            }
            catch
            {
                return 0;
            }
        }

        private static async Task<string> GetSystemUptimeAsync()
        {
            try
            {
                // Use Environment.TickCount for uptime as a simple alternative
                var uptimeMs = Environment.TickCount;
                var uptimeSeconds = uptimeMs / 1000;
                var uptimeMinutes = uptimeSeconds / 60;
                var uptimeHours = uptimeMinutes / 60;
                
                if (uptimeHours > 0)
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

        public static async Task AddActivityAsync(string type, string service, string message)
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
                    service = service,
                    message = message,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    id = timestamp
                };

                await activityRef.PutAsync(activityData);
                
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Activity added: {message}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to add activity: {ex.Message}", "Error", "SYSTEM");
            }
        }

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
                
                // Send shutdown start log
                _ = Task.Run(async () => {
                    await AddLogAsync("Info", "Starting graceful shutdown of all services", "MAINWINDOW");
                    await AddLogAsync("Info", "Real-time monitoring service stopping", "REALTIME_MONITORING");
                });
                
                // Update connection status to offline before stopping
                _ = Task.Run(async () =>
                {
                    if (_isInitialized && _database != null && _username != null)
                    {
                        try
                        {
                            await _database
                                .Child("users")
                                .Child(_username)
                                .Child("connection")
                                .PatchAsync(new 
                                { 
                                    status = "offline", 
                                    lastSeen = DateTime.UtcNow.ToString("o"),
                                    serviceShutdown = true
                                });
                                
                            LogService.WriteSystemLog("[REALTIME_MONITORING] Connection status updated to offline", "Information", "SYSTEM");
                            await AddLogAsync("Info", "Connection status updated to offline", "REALTIME_MONITORING");
                        }
                        catch (Exception ex)
                        {
                            LogService.WriteSystemLog($"[REALTIME_MONITORING] Failed to update connection status: {ex.Message}", "Error", "SYSTEM");
                            await AddLogAsync("Error", $"Failed to update connection status: {ex.Message}", "REALTIME_MONITORING");
                        }
                    }
                });
                
                // Stop all timers
                _connectionTimer?.Stop();
                _connectionTimer?.Dispose();
                _connectionTimer = null;
                
                _systemMonitoringTimer?.Stop();
                _systemMonitoringTimer?.Dispose();
                _systemMonitoringTimer = null;
                
                _cpuHistoryTimer?.Stop();
                _cpuHistoryTimer?.Dispose();
                _cpuHistoryTimer = null;
                
                // Clear data
                _cpuHistory.Clear();
                
                // Mark as uninitialized
                _isInitialized = false;
                
                LogService.WriteSystemLog("[REALTIME_MONITORING] All monitoring services stopped", "Information", "SYSTEM");
                
                // Send final shutdown log
                _ = Task.Run(async () => {
                    await AddLogAsync("Info", "Real-time monitoring service stopped successfully", "REALTIME_MONITORING");
                    await AddLogAsync("Info", "All PC application services terminated", "MAINWINDOW");
                });
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[REALTIME_MONITORING] Error stopping services: {ex.Message}", "Error", "SYSTEM");
                _ = Task.Run(async () => {
                    await AddLogAsync("Error", $"Error during service shutdown: {ex.Message}", "REALTIME_MONITORING");
                });
            }
        }
    }
}
