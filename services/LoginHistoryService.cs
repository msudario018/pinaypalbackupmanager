using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class LoginHistoryService
    {
        private static readonly string HistoryFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinayPalBackupManager", "login_history.json");
        private static readonly string FirebaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public class LoginEntry
        {
            public DateTime Timestamp { get; set; }
            public string Username { get; set; } = "";
            public bool Success { get; set; }
            public string IpAddress { get; set; } = "";
            public string DeviceInfo { get; set; } = "";
            public string Location { get; set; } = "";
            public string FailureReason { get; set; } = "";
        }

        public static async Task AddLoginAsync(string username, bool success, string failureReason = "")
        {
            var entries = LoadHistory();

            var entry = new LoginEntry
            {
                Timestamp = DateTime.Now,
                Username = username,
                Success = success,
                IpAddress = GetLocalIpAddress(),
                DeviceInfo = GetDeviceInfo(),
                Location = "Local", // Could be enhanced with IP geolocation
                FailureReason = failureReason
            };

            entries.Add(entry);

            // Keep only last 100 entries
            if (entries.Count > 100)
                entries = entries.OrderByDescending(e => e.Timestamp).Take(100).ToList();

            await SaveHistoryAsync(entries);

            // Sync to Firebase
            _ = RecordLoginHistoryAsync(username, success, entry.DeviceInfo, entry.IpAddress, failureReason);
        }

        public static async Task RecordLoginHistoryAsync(string username, bool success, string deviceInfo = "", string ipAddress = "", string failureReason = "")
        {
            try
            {
                var timestamp = DateTime.UtcNow.Ticks.ToString();
                var deviceId = GetDeviceId();

                var loginData = new
                {
                    success = success,
                    deviceId = deviceId,
                    deviceInfo = string.IsNullOrEmpty(deviceInfo) ? "PC App" : deviceInfo,
                    ipAddress = ipAddress,
                    userAgent = "PinayPal Backup Manager PC App",
                    failureReason = failureReason,
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                var json = JsonSerializer.Serialize(loginData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                await _httpClient.PutAsync($"{FirebaseUrl}login_history/{username}/{timestamp}.json", content);

                LogService.WriteSystemLog($"[LOGIN_HISTORY] Recorded login history for {username}: success={success}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[LOGIN_HISTORY] Failed to record login history to Firebase: {ex.Message}", "Warning", "SYSTEM");
            }
        }

        public static List<LoginEntry> GetLoginHistory(string username, int maxEntries = 20)
        {
            var entries = LoadHistory();
            return entries
                .Where(e => e.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Timestamp)
                .Take(maxEntries)
                .ToList();
        }

        public static List<LoginEntry> GetRecentFailedLogins(string username, TimeSpan period)
        {
            var entries = LoadHistory();
            var cutoff = DateTime.Now - period;
            
            return entries
                .Where(e => e.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && 
                           !e.Success && 
                           e.Timestamp > cutoff)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }

        public static int GetFailedLoginCount(string username, TimeSpan period)
        {
            return GetRecentFailedLogins(username, period).Count;
        }

        public static async Task ClearHistoryAsync(string username)
        {
            var entries = LoadHistory();
            entries.RemoveAll(e => e.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            await SaveHistoryAsync(entries);
        }

        private static List<LoginEntry> LoadHistory()
        {
            try
            {
                if (!File.Exists(HistoryFile))
                    return new List<LoginEntry>();

                var json = File.ReadAllText(HistoryFile);
                return JsonSerializer.Deserialize<List<LoginEntry>>(json) ?? new List<LoginEntry>();
            }
            catch
            {
                return new List<LoginEntry>();
            }
        }

        private static async Task SaveHistoryAsync(List<LoginEntry> entries)
        {
            try
            {
                var directory = Path.GetDirectoryName(HistoryFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(HistoryFile, json);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[LOGIN_HISTORY] Failed to save: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private static string GetLocalIpAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return ip?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string GetDeviceInfo()
        {
            try
            {
                return $"{Environment.MachineName} - {Environment.OSVersion}";
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string GetDeviceId()
        {
            try
            {
                var machineId = Environment.MachineName + Environment.UserName + Environment.ProcessorCount;
                using var hash = System.Security.Cryptography.SHA256.Create();
                var bytes = System.Text.Encoding.UTF8.GetBytes(machineId);
                var hashBytes = hash.ComputeHash(bytes);
                return Convert.ToHexString(hashBytes).Substring(0, 16).ToLower();
            }
            catch
            {
                return "unknown-device";
            }
        }
    }
}
