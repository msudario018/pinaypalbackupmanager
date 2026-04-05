using Firebase.Database;
using Firebase.Database.Query;
using System;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class FirebaseRemoteService
    {
        private static FirebaseClient? _database;
        private static string? _username;
        private static string? _deviceId;
        private static bool _isInitialized = false;

        public static void Initialize(string databaseUrl, string username)
        {
            try
            {
                _database = new FirebaseClient(databaseUrl);
                _username = username;
                _deviceId = GetDeviceId();
                _isInitialized = true;
                
                LogService.WriteSystemLog($"[FIREBASE] Initialized for user: {username}, device: {_deviceId}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Initialization failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task RegisterDeviceAsync(string deviceName)
        {
            if (!_isInitialized || _database == null || _username == null || _deviceId == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot register device", "Error", "SYSTEM");
                return;
            }

            try
            {
                var deviceData = new
                {
                    name = deviceName,
                    platform = Environment.OSVersion.Platform.ToString(),
                    lastSeen = DateTime.UtcNow.ToString("o"),
                    status = "online"
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("devices")
                    .Child(_deviceId)
                    .PutAsync(deviceData);

                LogService.WriteSystemLog($"[FIREBASE] Device registered: {_deviceId}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Device registration failed: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static void ListenForCommands(Action<string, string> onCommandReceived)
        {
            if (!_isInitialized || _database == null || _username == null || _deviceId == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot listen for commands", "Error", "SYSTEM");
                return;
            }

            try
            {
                var commandsRef = _database
                    .Child("users")
                    .Child(_username)
                    .Child("devices")
                    .Child(_deviceId)
                    .Child("commands");

                commandsRef.AsObservable<FirebaseCommand>()
                    .Subscribe(change =>
                    {
                        if (change.Object != null && change.Object.Status == "pending")
                        {
                            LogService.WriteSystemLog($"[FIREBASE] Command received: {change.Object.Type}", "Information", "SYSTEM");
                            onCommandReceived?.Invoke(change.Object.Type, change.Key);
                        }
                    });

                LogService.WriteSystemLog("[FIREBASE] Listening for commands...", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to listen for commands: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task UpdateCommandStatusAsync(string commandId, string status, string? result = null)
        {
            if (!_isInitialized || _database == null || _username == null || _deviceId == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot update command", "Error", "SYSTEM");
                return;
            }

            try
            {
                var updateData = new
                {
                    status = status,
                    result = result ?? ""
                };

                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("devices")
                    .Child(_deviceId)
                    .Child("commands")
                    .Child(commandId)
                    .PatchAsync(updateData);

                LogService.WriteSystemLog($"[FIREBASE] Command {commandId} updated to: {status}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to update command: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task UpdateDeviceStatusAsync(string status)
        {
            if (!_isInitialized || _database == null || _username == null || _deviceId == null)
            {
                return;
            }

            try
            {
                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("devices")
                    .Child(_deviceId)
                    .Child("status")
                    .PutAsync(status);
            }
            catch { }
        }

        private static string GetDeviceId()
        {
            var machineName = Environment.MachineName;
            var userName = Environment.UserName;
            return $"{machineName}_{userName}".Replace(" ", "_").Replace("-", "_").ToLower();
        }
    }

    public class FirebaseCommand
    {
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }
}
