using Firebase.Database;
using Firebase.Database.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class SystemStatusService
    {
        private static FirebaseClient? _database;
        private static string? _username;
        private static bool _isInitialized = false;
        private static System.Timers.Timer? _updateTimer;

        public static void Initialize(string databaseUrl, string username)
        {
            try
            {
                _database = new FirebaseClient(databaseUrl);
                _username = username;
                _isInitialized = true;
                
                // Start automatic updates every 30 seconds
                _updateTimer = new System.Timers.Timer(30000); // 30 seconds
                _updateTimer.Elapsed += async (sender, e) => await UpdateSystemStatusAsync();
                _updateTimer.AutoReset = true;
                _updateTimer.Start();
                
                LogService.WriteSystemLog($"[SYSTEM_STATUS] Initialized for user: {username}", "Information", "SYSTEM");
                
                // Initial update
                _ = Task.Run(async () => await UpdateSystemStatusAsync());
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] Initialization failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task<object> GetSystemStatusAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                return null;
            }

            try
            {
                var snapshot = await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .OnceSingleAsync<object>();

                if (snapshot != null)
                {
                    return snapshot;
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] GetSystemStatusAsync error: {ex.Message}", "Error", "SYSTEM");
            }

            return null;
        }

        public static async Task UpdateSystemStatusAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                return;
            }

            try
            {
                // Get current backup times to preserve them
                var currentBackupTimes = await GetCurrentBackupTimesAsync();
                
                var systemData = new
                {
                    uptime = await GetUptimeAsync(),
                    lastHealthCheck = DateTime.UtcNow.ToString("o"),
                    activeProcesses = await GetActiveProcessCountAsync(),
                    diskSpaceAvailable = await GetDiskSpaceAsync(),
                    // Preserve backup times - don't update them here
                    backupsToday = currentBackupTimes.GetValueOrDefault("backupsToday", "0"),
                    successRate = currentBackupTimes.GetValueOrDefault("successRate", "100%"),
                    failedBackups = currentBackupTimes.GetValueOrDefault("failedBackups", "0"),
                    lastFtpBackup = currentBackupTimes.GetValueOrDefault("lastFtpBackup", "Never"),
                    lastMcBackup = currentBackupTimes.GetValueOrDefault("lastMcBackup", "Never"),
                    lastSqlBackup = currentBackupTimes.GetValueOrDefault("lastSqlBackup", "Never"),
                    lastUpdated = DateTime.UtcNow.ToString("o")
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .PutAsync(systemData);

                LogService.WriteSystemLog("[SYSTEM_STATUS] System status updated", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] Update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task UpdateBackupStatsAsync(int backupsToday, double successRate, int failedBackups)
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                return;
            }

            try
            {
                var statsData = new
                {
                    backupsToday = backupsToday.ToString(),
                    successRate = $"{successRate:F1}%",
                    failedBackups = failedBackups.ToString(),
                    lastUpdated = DateTime.UtcNow.ToString("o")
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .PatchAsync(statsData);

                LogService.WriteSystemLog("[SYSTEM_STATUS] Backup stats updated", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] Backup stats update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task UpdateLastBackupTimesAsync(string ftpTime, string mailchimpTime, string sqlTime)
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                return;
            }

            try
            {
                // First, get the current complete system status to preserve everything
                var currentSnapshot = await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .OnceSingleAsync<object>();

                var updateData = new Dictionary<string, object>
                {
                    ["lastFtpBackup"] = ftpTime,
                    ["lastMcBackup"] = mailchimpTime,
                    ["lastSqlBackup"] = sqlTime,
                    ["lastUpdated"] = DateTime.UtcNow.ToString("o")
                };

                // If we have existing data, preserve all other fields
                if (currentSnapshot != null)
                {
                    Dictionary<string, object>? existingData = null;

                    if (currentSnapshot is Newtonsoft.Json.Linq.JObject jObject)
                    {
                        existingData = jObject.ToObject<Dictionary<string, object>>();
                    }
                    else if (currentSnapshot is Dictionary<string, object> dict)
                    {
                        existingData = dict;
                    }

                    if (existingData != null)
                    {
                        foreach (var kvp in existingData)
                        {
                            // Don't overwrite the backup times we're updating
                            if (!updateData.ContainsKey(kvp.Key))
                            {
                                updateData[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .PutAsync(updateData);

                LogService.WriteSystemLog("[SYSTEM_STATUS] Last backup times updated", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] Backup times update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task UpdateFtpBackupTimestampAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                return;
            }

            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                var mnlTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
                var mnlTimestamp = mnlTime.ToString("yyyy-MM-dd HH:mm:ss");

                // First, get the current complete system status to preserve everything
                var currentSnapshot = await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .OnceSingleAsync<object>();

                var updateData = new Dictionary<string, object>
                {
                    ["lastFtpBackup"] = mnlTimestamp,
                    ["lastUpdated"] = DateTime.UtcNow.ToString("o")
                };

                // If we have existing data, preserve all other fields
                if (currentSnapshot != null)
                {
                    Dictionary<string, object>? existingData = null;

                    if (currentSnapshot is Newtonsoft.Json.Linq.JObject jObject)
                    {
                        existingData = jObject.ToObject<Dictionary<string, object>>();
                    }
                    else if (currentSnapshot is Dictionary<string, object> dict)
                    {
                        existingData = dict;
                    }

                    if (existingData != null)
                    {
                        foreach (var kvp in existingData)
                        {
                            // Don't overwrite the backup time we're updating
                            if (!updateData.ContainsKey(kvp.Key))
                            {
                                updateData[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .PutAsync(updateData);

                LogService.WriteSystemLog($"[SYSTEM_STATUS] FTP backup timestamp updated: {mnlTimestamp}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] FTP backup timestamp update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task UpdateSqlBackupTimestampAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                return;
            }

            try
            {
                var mnlTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
                var mnlTimestamp = mnlTime.ToString("yyyy-MM-dd HH:mm:ss");

                // First, get the current complete system status to preserve everything
                var currentSnapshot = await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .OnceSingleAsync<object>();

                var updateData = new Dictionary<string, object>
                {
                    ["lastSqlBackup"] = mnlTimestamp,
                    ["lastUpdated"] = DateTime.UtcNow.ToString("o")
                };

                // If we have existing data, preserve all other fields
                if (currentSnapshot != null)
                {
                    Dictionary<string, object>? existingData = null;

                    if (currentSnapshot is Newtonsoft.Json.Linq.JObject jObject)
                    {
                        existingData = jObject.ToObject<Dictionary<string, object>>();
                    }
                    else if (currentSnapshot is Dictionary<string, object> dict)
                    {
                        existingData = dict;
                    }

                    if (existingData != null)
                    {
                        foreach (var kvp in existingData)
                        {
                            // Don't overwrite the backup time we're updating
                            if (!updateData.ContainsKey(kvp.Key))
                            {
                                updateData[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .PutAsync(updateData);

                LogService.WriteSystemLog($"[SYSTEM_STATUS] SQL backup timestamp updated: {mnlTimestamp}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] SQL backup timestamp update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task UpdateMailchimpBackupTimestampAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                return;
            }

            try
            {
                var mnlTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
                var mnlTimestamp = mnlTime.ToString("yyyy-MM-dd HH:mm:ss");

                // First, get the current complete system status to preserve everything
                var currentSnapshot = await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .OnceSingleAsync<object>();

                var updateData = new Dictionary<string, object>
                {
                    ["lastMcBackup"] = mnlTimestamp,
                    ["lastUpdated"] = DateTime.UtcNow.ToString("o")
                };

                // If we have existing data, preserve all other fields
                if (currentSnapshot != null)
                {
                    Dictionary<string, object>? existingData = null;

                    if (currentSnapshot is Newtonsoft.Json.Linq.JObject jObject)
                    {
                        existingData = jObject.ToObject<Dictionary<string, object>>();
                    }
                    else if (currentSnapshot is Dictionary<string, object> dict)
                    {
                        existingData = dict;
                    }

                    if (existingData != null)
                    {
                        foreach (var kvp in existingData)
                        {
                            // Don't overwrite the backup time we're updating
                            if (!updateData.ContainsKey(kvp.Key))
                            {
                                updateData[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .PutAsync(updateData);

                LogService.WriteSystemLog($"[SYSTEM_STATUS] Mailchimp backup timestamp updated: {mnlTimestamp}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] Mailchimp backup timestamp update failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task WriteBackupHistoryAsync(string type, string status)
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                return;
            }

            try
            {
                var backupRecord = new
                {
                    type = type,
                    status = status,
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("backups")
                    .PostAsync(backupRecord);

                LogService.WriteSystemLog($"[SYSTEM_STATUS] Backup history written: {type} - {status}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] Backup history write failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static async Task<object> GetSystemStatusData()
        {
            try
            {
                // Get current backup times to preserve them
                var currentBackupTimes = await GetCurrentBackupTimesAsync();
                
                return new
                {
                    uptime = await GetUptimeAsync(),
                    lastHealthCheck = DateTime.UtcNow.ToString("o"),
                    activeProcesses = await GetActiveProcessCountAsync(),
                    diskSpaceAvailable = await GetDiskSpaceAsync(),
                    backupsToday = currentBackupTimes.GetValueOrDefault("backupsToday", "0"),
                    successRate = currentBackupTimes.GetValueOrDefault("successRate", "100%"),
                    failedBackups = currentBackupTimes.GetValueOrDefault("failedBackups", "0"),
                    lastFtpBackup = currentBackupTimes.GetValueOrDefault("lastFtpBackup", "Never"),
                    lastMcBackup = currentBackupTimes.GetValueOrDefault("lastMcBackup", "Never"),
                    lastSqlBackup = currentBackupTimes.GetValueOrDefault("lastSqlBackup", "Never"),
                    lastUpdated = DateTime.UtcNow.ToString("o")
                };
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] GetSystemStatusData error: {ex.Message}", "Error", "SYSTEM");
                return _getDefaultSystemStatusData();
            }
        }

        private static async Task<Dictionary<string, string>> GetCurrentBackupTimesAsync()
        {
            var backupTimes = new Dictionary<string, string>
            {
                ["backupsToday"] = "0",
                ["successRate"] = "100%",
                ["failedBackups"] = "0",
                ["lastFtpBackup"] = "Never",
                ["lastMcBackup"] = "Never",
                ["lastSqlBackup"] = "Never"
            };

            try
            {
                if (!_isInitialized || _database == null || _username == null)
                {
                    return backupTimes;
                }

                var snapshot = await _database
                    .Child("users")
                    .Child(_username)
                    .Child("system_status")
                    .OnceSingleAsync<object>();

                if (snapshot != null)
                {
                    Dictionary<string, object>? data = null;

                    if (snapshot is Newtonsoft.Json.Linq.JObject jObject)
                    {
                        data = jObject.ToObject<Dictionary<string, object>>();
                    }
                    else if (snapshot is Dictionary<string, object> dict)
                    {
                        data = dict;
                    }

                    if (data != null)
                    {
                        foreach (var kvp in data)
                        {
                            if (backupTimes.ContainsKey(kvp.Key))
                            {
                                backupTimes[kvp.Key] = kvp.Value?.ToString() ?? backupTimes[kvp.Key];
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SYSTEM_STATUS] GetCurrentBackupTimesAsync error: {ex.Message}", "Error", "SYSTEM");
            }

            return backupTimes;
        }

        private static object _getDefaultSystemStatusData()
        {
            return new
            {
                uptime = "Calculating...",
                lastHealthCheck = DateTime.UtcNow.ToString("o"),
                activeProcesses = "0",
                diskSpaceAvailable = "Unknown",
                backupsToday = "0",
                successRate = "100%",
                failedBackups = "0",
                lastFtpBackup = "Never",
                lastMcBackup = "Never",
                lastSqlBackup = "Never",
                lastUpdated = DateTime.UtcNow.ToString("o")
            };
        }

        private static async Task<string> GetUptimeAsync()
        {
            try
            {
                using var proc = new Process();
                proc.StartInfo.FileName = "cmd";
                proc.StartInfo.Arguments = "/c systeminfo | find \"System Boot Time\"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.CreateNoWindow = true;
                proc.Start();

                string output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (output.Contains("System Boot Time"))
                {
                    var bootTimeString = output.Split(':')[1].Trim();
                    var bootTime = DateTime.Parse(bootTimeString);
                    var uptime = DateTime.Now - bootTime;
                    
                    if (uptime.TotalHours < 1)
                        return $"{uptime.TotalMinutes:F0} min";
                    else if (uptime.TotalHours < 24)
                        return $"{uptime.TotalHours:F0}h {uptime.Minutes}m";
                    else
                        return $"{uptime.Days}d {uptime.Hours}h";
                }
            }
            catch { }

            return "Unknown";
        }

        private static async Task<string> GetActiveProcessCountAsync()
        {
            try
            {
                // Get user processes only (more realistic count)
                using var proc = new Process();
                proc.StartInfo.FileName = "powershell";
                proc.StartInfo.Arguments = "-Command \"Get-Process | Where-Object {$_.ProcessName -notlike '*svchost*' -and $_.ProcessName -notlike '*csrss*' -and $_.ProcessName -notlike '*wininit*' -and $_.ProcessName -notlike '*lsass*' -and $_.ProcessName -notlike '*services*' -and $_.ProcessName -notlike '*dwm*'} | Measure-Object | Select-Object -ExpandProperty Count\"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.CreateNoWindow = true;
                proc.Start();

                string output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (int.TryParse(output.Trim(), out int count))
                    return count.ToString();
            }
            catch (Exception ex)
            {
                // Fallback to simple method if PowerShell fails
                try
                {
                    using var proc = new Process();
                    proc.StartInfo.FileName = "cmd";
                    proc.StartInfo.Arguments = "/c tasklist | find /c \".exe\" /v";
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.StartInfo.CreateNoWindow = true;
                    proc.Start();

                    string output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    if (int.TryParse(output.Trim(), out int count))
                    {
                        // Estimate user processes (roughly 1/3 of total)
                        int userProcesses = Math.Max(1, count / 3);
                        return userProcesses.ToString();
                    }
                }
                catch { }
            }

            return "3"; // Default fallback
        }

        private static async Task<string> GetDiskSpaceAsync()
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:");
                long freeSpace = drive.AvailableFreeSpace;
                double freeGB = freeSpace / (1024.0 * 1024.0 * 1024.0);
                return $"{freeGB:F1} GB";
            }
            catch { }

            return "Unknown";
        }

        public static void Stop()
        {
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            _updateTimer = null;
            
            LogService.WriteSystemLog("[SYSTEM_STATUS] Service stopped", "Information", "SYSTEM");
        }
    }
}
