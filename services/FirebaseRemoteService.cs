using Firebase.Database;
using Firebase.Database.Query;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class FirebaseRemoteService
    {
        private static FirebaseClient? _database;
        private static string? _username;
        private static string? _deviceId;
        private static string? _databaseUrl;
        private static bool _isInitialized = false;
        private static readonly HttpClient _httpClient = new HttpClient();

        public static event Action<Dictionary<string, object>?>? OnScheduleUpdated;
        public static event Action<Dictionary<string, object>?>? OnHealthThresholdsUpdated;
        public static event Action<Dictionary<string, object>?>? OnAutoScanUpdated;
        public static event Action<string, string?>? OnCommandReceived;

        public static void Initialize(string databaseUrl, string username)
        {
            try
            {
                _database = new FirebaseClient(databaseUrl);
                _username = username;
                _databaseUrl = databaseUrl;
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

        public static void ListenForCommands(Action<string, string, string?> onCommandReceived)
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
                            onCommandReceived?.Invoke(change.Object.Type, change.Key, change.Object.Data);
                        }
                    });

                LogService.WriteSystemLog("[FIREBASE] Listening for commands...", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to listen for commands: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static Dictionary<string, object>? _lastKnownSchedule;
        private static HashSet<string> _processedCommands = new HashSet<string>();

        public static void ListenForScheduleUpdates()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot listen for schedule updates", "Error", "SYSTEM");
                return;
            }

            try
            {
                var scheduleRef = _database
                    .Child("users")
                    .Child(_username)
                    .Child("backup_schedule");

                scheduleRef.AsObservable<Dictionary<string, object>>()
                    .Subscribe(change =>
                    {
                        LogService.WriteSystemLog($"[FIREBASE] Schedule change detected - Key: {change.Key}, EventType: {change.EventType}", "Information", "SYSTEM");
                        if (change.Object != null)
                        {
                            LogService.WriteSystemLog($"[FIREBASE] Schedule data received: {string.Join(", ", change.Object.Keys)}", "Information", "SYSTEM");
                            LogService.WriteSystemLog("[FIREBASE] Schedule updated from Firebase", "Information", "SYSTEM");
                            _lastKnownSchedule = change.Object;
                            OnScheduleUpdated?.Invoke(change.Object);
                        }
                        else
                        {
                            LogService.WriteSystemLog("[FIREBASE] Schedule data is null", "Warning", "SYSTEM");
                        }
                    });

                LogService.WriteSystemLog("[FIREBASE] Listening for schedule updates...", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to listen for schedule updates: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task PollScheduleUpdatesAsync()
        {
            if (!_isInitialized || _databaseUrl == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot poll for schedule updates", "Error", "SYSTEM");
                return;
            }

            try
            {
                var url = $"{_databaseUrl}/users/{_username}/backup_schedule.json";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(json) && json != "null")
                    {
                        var schedule = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (schedule != null)
                        {
                            // Compare with last known schedule
                            if (_lastKnownSchedule == null || !SchedulesEqual(_lastKnownSchedule, schedule))
                            {
                                LogService.WriteSystemLog("[FIREBASE] Schedule change detected via HTTP polling", "Information", "SYSTEM");
                                LogService.WriteSystemLog($"[FIREBASE] Schedule data received: {string.Join(", ", schedule.Keys)}", "Information", "SYSTEM");
                                _lastKnownSchedule = schedule;
                                OnScheduleUpdated?.Invoke(schedule);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to poll for schedule updates: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task PollCommandsAsync()
        {
            if (!_isInitialized || _databaseUrl == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot poll for commands", "Error", "SYSTEM");
                return;
            }

            try
            {
                var url = $"{_databaseUrl}/users/{_username}/commands.json";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(json) && json != "null")
                    {
                        var commands = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (commands != null)
                        {
                            // Check for new commands
                            foreach (var kvp in commands)
                            {
                                var commandId = kvp.Key;
                                var commandValue = kvp.Value;

                                // Skip if already processed
                                if (_processedCommands.Contains(commandId))
                                {
                                    continue;
                                }

                                // Extract command type from the command object
                                string? commandType = null;
                                if (commandValue is JsonElement jsonElement)
                                {
                                    if (jsonElement.TryGetProperty("type", out var typeElement))
                                    {
                                        commandType = typeElement.GetString();
                                    }
                                }
                                else if (commandValue is Dictionary<string, object> dict)
                                {
                                    if (dict.ContainsKey("type"))
                                    {
                                        commandType = dict["type"]?.ToString();
                                    }
                                }

                                if (!string.IsNullOrEmpty(commandType))
                                {
                                    LogService.WriteSystemLog($"[FIREBASE] Command detected: {commandType} (ID: {commandId})", "Information", "SYSTEM");
                                    _processedCommands.Add(commandId);
                                    OnCommandReceived?.Invoke(commandType, commandId);

                                    // Delete the command after invoking
                                    await DeleteCommandAsync(commandId);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to poll for commands: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task DeleteCommandAsync(string commandId)
        {
            if (!_isInitialized || _databaseUrl == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot delete command", "Error", "SYSTEM");
                return;
            }

            try
            {
                var url = $"{_databaseUrl}/users/{_username}/commands/{commandId}.json";
                var request = new HttpRequestMessage(HttpMethod.Delete, url);
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    LogService.WriteSystemLog($"[FIREBASE] Command deleted: {commandId}", "Information", "SYSTEM");
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to delete command: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static bool SchedulesEqual(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kvp in a)
            {
                if (!b.ContainsKey(kvp.Key)) return false;
                var aVal = JsonSerializer.Serialize(kvp.Value);
                var bVal = JsonSerializer.Serialize(b[kvp.Key]);
                if (aVal != bVal) return false;
            }
            return true;
        }

        public static void ListenForHealthThresholdUpdates()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot listen for health threshold updates", "Error", "SYSTEM");
                return;
            }

            try
            {
                var thresholdsRef = _database
                    .Child("users")
                    .Child(_username)
                    .Child("health_thresholds");

                thresholdsRef.AsObservable<Dictionary<string, object>>()
                    .Subscribe(change =>
                    {
                        if (change.Object != null)
                        {
                            LogService.WriteSystemLog("[FIREBASE] Health thresholds updated from Firebase", "Information", "SYSTEM");
                            OnHealthThresholdsUpdated?.Invoke(change.Object);
                        }
                    });

                LogService.WriteSystemLog("[FIREBASE] Listening for health threshold updates...", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to listen for health threshold updates: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static void ListenForAutoScanUpdates()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot listen for auto scan updates", "Error", "SYSTEM");
                return;
            }

            try
            {
                var autoScanRef = _database
                    .Child("users")
                    .Child(_username)
                    .Child("auto_scan");

                autoScanRef.AsObservable<Dictionary<string, object>>()
                    .Subscribe(change =>
                    {
                        if (change.Object != null)
                        {
                            LogService.WriteSystemLog("[FIREBASE] Auto scan settings updated from Firebase", "Information", "SYSTEM");
                            OnAutoScanUpdated?.Invoke(change.Object);
                        }
                    });

                LogService.WriteSystemLog("[FIREBASE] Listening for auto scan updates...", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to listen for auto scan updates: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task<Dictionary<string, object>?> GetBackupScheduleAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot get backup schedule", "Error", "SYSTEM");
                return null;
            }

            try
            {
                var schedule = await _database
                    .Child("users")
                    .Child(_username)
                    .Child("backup_schedule")
                    .OnceSingleAsync<Dictionary<string, object>>();

                LogService.WriteSystemLog("[FIREBASE] Backup schedule retrieved", "Information", "SYSTEM");
                return schedule;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to get backup schedule: {ex.Message}", "Error", "SYSTEM");
                return null;
            }
        }

        public static async Task<bool> SaveBackupScheduleAsync(Dictionary<string, object> schedule)
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot save backup schedule", "Error", "SYSTEM");
                return false;
            }

            try
            {
                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("backup_schedule")
                    .PutAsync(schedule);

                LogService.WriteSystemLog("[FIREBASE] Backup schedule saved to Firebase", "Information", "SYSTEM");
                return true;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to save backup schedule: {ex.Message}", "Error", "SYSTEM");
                return false;
            }
        }

        public static async Task<bool> SaveAutoScanAsync(Dictionary<string, object> autoScan)
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot save auto scan", "Error", "SYSTEM");
                return false;
            }

            try
            {
                await _database
                    .Child("users")
                    .Child(_username)
                    .Child("auto_scan")
                    .PutAsync(autoScan);

                LogService.WriteSystemLog("[FIREBASE] Auto scan saved to Firebase", "Information", "SYSTEM");
                return true;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to save auto scan: {ex.Message}", "Error", "SYSTEM");
                return false;
            }
        }

        public static async Task<Dictionary<string, object>?> GetHealthThresholdsAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot get health thresholds", "Error", "SYSTEM");
                return null;
            }

            try
            {
                var thresholds = await _database
                    .Child("users")
                    .Child(_username)
                    .Child("health_thresholds")
                    .OnceSingleAsync<Dictionary<string, object>>();

                LogService.WriteSystemLog("[FIREBASE] Health thresholds retrieved", "Information", "SYSTEM");
                return thresholds;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to get health thresholds: {ex.Message}", "Error", "SYSTEM");
                return null;
            }
        }

        public static async Task<Dictionary<string, object>?> GetAutoScanSettingsAsync()
        {
            if (!_isInitialized || _database == null || _username == null)
            {
                LogService.WriteSystemLog("[FIREBASE] Not initialized - cannot get auto scan settings", "Error", "SYSTEM");
                return null;
            }

            try
            {
                var autoScan = await _database
                    .Child("users")
                    .Child(_username)
                    .Child("auto_scan")
                    .OnceSingleAsync<Dictionary<string, object>>();

                LogService.WriteSystemLog("[FIREBASE] Auto scan settings retrieved", "Information", "SYSTEM");
                return autoScan;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FIREBASE] Failed to get auto scan settings: {ex.Message}", "Error", "SYSTEM");
                return null;
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
        public string? Data { get; set; }
    }

    public static class BackupCommandTypes
    {
        public const string TriggerFtpBackup = "trigger_ftp_backup";
        public const string TriggerSqlBackup = "trigger_sql_backup";
        public const string TriggerMailchimpBackup = "trigger_mailchimp_backup";
        public const string PauseBackups = "pause_backups";
        public const string ResumeBackups = "resume_backups";
        public const string SyncFiles = "sync_files";
        public const string DeleteBackupFile = "delete_backup_file";
    }
}
