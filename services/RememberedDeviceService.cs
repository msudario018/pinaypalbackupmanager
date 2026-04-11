using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class RememberedDeviceService
    {
        private static readonly string DevicesFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinayPalBackupManager", "remembered_devices.json");

        private static readonly string FirebaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
        private static readonly string DevicesPath = "remembered_devices";
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public class RememberedDevice
        {
            public int UserId { get; set; }
            public string DeviceId { get; set; } = "";
            public string DeviceName { get; set; } = "";
            public DateTime RememberedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        public class FirebaseDeviceData
        {
            public int UserId { get; set; }
            public string DeviceId { get; set; } = "";
            public string DeviceName { get; set; } = "";
            public string RememberedAt { get; set; } = "";
            public string ExpiresAt { get; set; } = "";
            public string SourcePc { get; set; } = "";
        }

        /// <summary>
        /// Get a unique device ID for this machine
        /// </summary>
        public static string GetDeviceId()
        {
            try
            {
                // Use machine name + a GUID to create a unique device ID
                var machineName = Environment.MachineName;
                var userFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var deviceIdFile = Path.Combine(userFolder, "PinayPalBackupManager", "device_id.txt");

                // Try to load existing device ID
                if (File.Exists(deviceIdFile))
                {
                    return File.ReadAllText(deviceIdFile).Trim();
                }

                // Generate new device ID
                var newDeviceId = $"{machineName}_{Guid.NewGuid().ToString().Substring(0, 8)}";
                
                // Save it
                Directory.CreateDirectory(Path.GetDirectoryName(deviceIdFile)!);
                File.WriteAllText(deviceIdFile, newDeviceId);
                
                return newDeviceId;
            }
            catch
            {
                // Fallback to machine name + random
                return $"{Environment.MachineName}_{Random.Shared.Next(1000, 9999)}";
            }
        }

        /// <summary>
        /// Check if this device is remembered for the given user
        /// </summary>
        public static bool IsDeviceRemembered(int userId)
        {
            var deviceId = GetDeviceId();
            var devices = LoadDevices();
            
            return devices.Any(d => 
                d.UserId == userId && 
                d.DeviceId == deviceId && 
                d.ExpiresAt > DateTime.UtcNow);
        }

        /// <summary>
        /// Remember this device for the user for 30 days
        /// </summary>
        public static async Task RememberDeviceAsync(int userId, string username)
        {
            var deviceId = GetDeviceId();
            var deviceName = $"{Environment.MachineName} ({Environment.OSVersion.Platform})";
            var now = DateTime.UtcNow;
            var expiresAt = now.AddDays(30);

            var devices = LoadDevices();
            
            // Remove any existing entry for this device/user
            devices.RemoveAll(d => d.UserId == userId && d.DeviceId == deviceId);
            
            // Add new entry
            devices.Add(new RememberedDevice
            {
                UserId = userId,
                DeviceId = deviceId,
                DeviceName = deviceName,
                RememberedAt = now,
                ExpiresAt = expiresAt
            });

            await SaveDevicesAsync(devices);
            
            // Sync to Firebase
            _ = SyncToFirebaseAsync(userId, username, deviceId, deviceName, now, expiresAt);
        }

        /// <summary>
        /// Forget this device for the user
        /// </summary>
        public static async Task ForgetDeviceAsync(int userId)
        {
            var deviceId = GetDeviceId();
            var devices = LoadDevices();
            
            devices.RemoveAll(d => d.UserId == userId && d.DeviceId == deviceId);
            
            await SaveDevicesAsync(devices);
            
            // Remove from Firebase
            var user = AuthService.GetUserById(userId);
            if (user != null)
            {
                _ = RemoveFromFirebaseAsync(user.Username, deviceId);
            }
        }

        /// <summary>
        /// Clean up expired device entries
        /// </summary>
        public static async Task CleanupExpiredDevicesAsync()
        {
            var devices = LoadDevices();
            var now = DateTime.UtcNow;
            
            var expiredCount = devices.RemoveAll(d => d.ExpiresAt <= now);
            
            if (expiredCount > 0)
            {
                await SaveDevicesAsync(devices);
            }
        }

        private static List<RememberedDevice> LoadDevices()
        {
            try
            {
                if (!File.Exists(DevicesFile))
                    return new List<RememberedDevice>();

                var json = File.ReadAllText(DevicesFile);
                return JsonSerializer.Deserialize<List<RememberedDevice>>(json) ?? new List<RememberedDevice>();
            }
            catch
            {
                return new List<RememberedDevice>();
            }
        }

        private static async Task SaveDevicesAsync(List<RememberedDevice> devices)
        {
            try
            {
                var directory = Path.GetDirectoryName(DevicesFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(DevicesFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RememberedDeviceService] Failed to save devices: {ex.Message}");
            }
        }

        private static async Task SyncToFirebaseAsync(int userId, string username, string deviceId, string deviceName, DateTime rememberedAt, DateTime expiresAt)
        {
            try
            {
                var data = new FirebaseDeviceData
                {
                    UserId = userId,
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    RememberedAt = rememberedAt.ToString("o"),
                    ExpiresAt = expiresAt.ToString("o"),
                    SourcePc = Environment.MachineName
                };

                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync($"{FirebaseUrl}{DevicesPath}/{username}/{deviceId}.json", content);

                if (response.IsSuccessStatusCode)
                    Console.WriteLine($"[RememberedDeviceService] Device synced to Firebase: {username}/{deviceId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RememberedDeviceService] Firebase sync failed: {ex.Message}");
            }
        }

        private static async Task RemoveFromFirebaseAsync(string username, string deviceId)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{FirebaseUrl}{DevicesPath}/{username}/{deviceId}.json");
                if (response.IsSuccessStatusCode)
                    Console.WriteLine($"[RememberedDeviceService] Device removed from Firebase: {username}/{deviceId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RememberedDeviceService] Firebase removal failed: {ex.Message}");
            }
        }
    }
}
