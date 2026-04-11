using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PinayPalBackupManager.Services
{
    public static class ConfigService
    {
        public static event Action? OnScheduleChanged;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static AppSettings Current { get; private set; } = new();

        public static Dictionary<string, object> HealthThresholds { get; private set; } = new();

        public static string GetConfigDirectory()
        {
            // Always return AppData directory - survives Velopack updates
            return AppDataPaths.CurrentDirectory;
        }

        public static void Load()
        {
            var settings = new AppSettings();

            // Migrate appsettings.local.json from install dir to AppData if needed
            MigrateLocalConfigToAppData();

            var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            var (sharedPath, localPath) = FindConfigPaths(baseDir);

            if (!string.IsNullOrEmpty(sharedPath))
            {
                MergeInto(settings, ReadFile(sharedPath));
            }

            // Always prefer AppData local config over install-dir local config
            var appDataLocalPath = Path.Combine(AppDataPaths.CurrentDirectory, "appsettings.local.json");
            if (File.Exists(appDataLocalPath))
            {
                MergeInto(settings, ReadFile(appDataLocalPath));
            }
            else if (!string.IsNullOrEmpty(localPath))
            {
                MergeInto(settings, ReadFile(localPath));
            }

            Current = settings;
        }

        private static void MigrateLocalConfigToAppData()
        {
            try
            {
                var appDataPath = Path.Combine(AppDataPaths.CurrentDirectory, "appsettings.local.json");
                if (File.Exists(appDataPath)) return; // Already migrated

                // Search install dir and up to 3 parents for appsettings.local.json
                var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
                var dir = new DirectoryInfo(baseDir);
                for (int i = 0; i < 3 && dir != null; i++)
                {
                    var candidate = Path.Combine(dir.FullName, "appsettings.local.json");
                    if (File.Exists(candidate))
                    {
                        Directory.CreateDirectory(AppDataPaths.CurrentDirectory);
                        File.Copy(candidate, appDataPath, false);
                        Console.WriteLine($"[ConfigService] Migrated appsettings.local.json to AppData");
                        break;
                    }
                    dir = dir.Parent;
                }
            }
            catch { }
        }

        public static bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(Current.Paths.FtpLocalFolder)
                && !string.IsNullOrWhiteSpace(Current.Paths.MailchimpFolder)
                && !string.IsNullOrWhiteSpace(Current.Paths.SqlLocalFolder)
                && !string.IsNullOrWhiteSpace(Current.Ftp.Host)
                && !string.IsNullOrWhiteSpace(Current.Ftp.User)
                && !string.IsNullOrWhiteSpace(Current.Ftp.Password)
                && !string.IsNullOrWhiteSpace(Current.Sql.User)
                && !string.IsNullOrWhiteSpace(Current.Sql.Password)
                && !string.IsNullOrWhiteSpace(Current.Sql.RemotePath)
                && !string.IsNullOrWhiteSpace(Current.Mailchimp.ApiKey)
                && !string.IsNullOrWhiteSpace(Current.Mailchimp.AudienceId);
        }

        private static (string? shared, string? local) FindConfigPaths(string startDir)
        {
            string? shared = null;
            string? local = null;

            var dir = new DirectoryInfo(startDir);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                var sharedCandidate = Path.Combine(dir.FullName, "appsettings.json");
                if (shared == null && File.Exists(sharedCandidate)) shared = sharedCandidate;

                var localCandidate = Path.Combine(dir.FullName, "appsettings.local.json");
                if (local == null && File.Exists(localCandidate)) local = localCandidate;

                if (shared != null && local != null) break;
                dir = dir.Parent;
            }

            return (shared, local);
        }

        private static AppSettings ReadFile(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }

        public static void SaveOperation()
        {
            try
            {
                var appDataPath = Path.Combine(AppDataPaths.CurrentDirectory, "appsettings.local.json");
                var existing = File.Exists(appDataPath) ? ReadFile(appDataPath) : new AppSettings();
                existing.Operation.RetentionDays    = Current.Operation.RetentionDays;
                existing.Operation.AutoStartWindows = Current.Operation.AutoStartWindows;
                var json = JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(AppDataPaths.CurrentDirectory);
                File.WriteAllText(appDataPath, json);
            }
            catch { }
        }

        public static void SaveHttpServerSettings()
        {
            try
            {
                var appDataPath = Path.Combine(AppDataPaths.CurrentDirectory, "appsettings.local.json");
                var existing = File.Exists(appDataPath) ? ReadFile(appDataPath) : new AppSettings();
                existing.HttpServer.Port = Current.HttpServer.Port;
                existing.HttpServer.Enabled = Current.HttpServer.Enabled;
                var json = JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(AppDataPaths.CurrentDirectory);
                File.WriteAllText(appDataPath, json);
            }
            catch { }
        }

        public static void SaveSchedule()
        {
            try
            {
                var appDataPath = Path.Combine(AppDataPaths.CurrentDirectory, "appsettings.local.json");
                var existing = File.Exists(appDataPath) ? ReadFile(appDataPath) : new AppSettings();
                existing.Schedule.FtpDailySyncHourMnl = Current.Schedule.FtpDailySyncHourMnl;
                existing.Schedule.FtpDailySyncMinuteMnl = Current.Schedule.FtpDailySyncMinuteMnl;
                existing.Schedule.MailchimpDailySyncHourMnl = Current.Schedule.MailchimpDailySyncHourMnl;
                existing.Schedule.MailchimpDailySyncMinuteMnl = Current.Schedule.MailchimpDailySyncMinuteMnl;
                existing.Schedule.SqlDailySyncHourMnl = Current.Schedule.SqlDailySyncHourMnl;
                existing.Schedule.SqlDailySyncMinuteMnl = Current.Schedule.SqlDailySyncMinuteMnl;
                existing.Schedule.FtpAutoScanHours = Current.Schedule.FtpAutoScanHours;
                existing.Schedule.FtpAutoScanMinutes = Current.Schedule.FtpAutoScanMinutes;
                existing.Schedule.MailchimpAutoScanHours = Current.Schedule.MailchimpAutoScanHours;
                existing.Schedule.MailchimpAutoScanMinutes = Current.Schedule.MailchimpAutoScanMinutes;
                existing.Schedule.SqlAutoScanHours = Current.Schedule.SqlAutoScanHours;
                existing.Schedule.SqlAutoScanMinutes = Current.Schedule.SqlAutoScanMinutes;
                var json = JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(AppDataPaths.CurrentDirectory);
                File.WriteAllText(appDataPath, json);
            }
            catch { }
        }

        public static void MergeFirebaseSchedule(Dictionary<string, object>? firebaseSchedule)
        {
            LogService.WriteSystemLog("[CONFIG] MergeFirebaseSchedule called", "Information", "SYSTEM");

            if (firebaseSchedule == null)
            {
                LogService.WriteSystemLog("[CONFIG] Firebase schedule is null", "Warning", "SYSTEM");
                return;
            }

            try
            {
                LogService.WriteSystemLog($"[CONFIG] Merging Firebase schedule with keys: {string.Join(", ", firebaseSchedule.Keys)}", "Information", "SYSTEM");

                // Log raw Firebase data for debugging
                foreach (var kvp in firebaseSchedule)
                {
                    LogService.WriteSystemLog($"[CONFIG] Firebase key: {kvp.Key}, value type: {kvp.Value?.GetType().Name}", "Information", "SYSTEM");
                    if (kvp.Value is Dictionary<string, object> dict)
                    {
                        LogService.WriteSystemLog($"[CONFIG] {kvp.Key} sub-keys: {string.Join(", ", dict.Keys)}", "Information", "SYSTEM");
                    }
                }

                // Process website/ftp schedule
                if (firebaseSchedule.ContainsKey("website"))
                {
                    var websiteValue = firebaseSchedule["website"];
                    Dictionary<string, object>? websiteSchedule = null;

                    // Handle JsonElement from HTTP polling
                    if (websiteValue is JsonElement jsonElement)
                    {
                        LogService.WriteSystemLog($"[CONFIG] Website JsonElement: {jsonElement}", "Information", "SYSTEM");
                        websiteSchedule = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement);
                        if (websiteSchedule != null)
                        {
                            LogService.WriteSystemLog($"[CONFIG] Website deserialized dict keys: {string.Join(", ", websiteSchedule.Keys)}", "Information", "SYSTEM");
                            foreach (var kvp in websiteSchedule)
                            {
                                LogService.WriteSystemLog($"[CONFIG] Website dict - {kvp.Key}: {kvp.Value} ({kvp.Value?.GetType().Name})", "Information", "SYSTEM");
                            }
                        }
                    }
                    // Handle Dictionary from FirebaseDatabase.net
                    else if (websiteValue is Dictionary<string, object> dict)
                    {
                        websiteSchedule = dict;
                    }

                    if (websiteSchedule != null)
                    {
                        // Extract actual values from JsonElement
                        var enabledValue = websiteSchedule.GetValueOrDefault("enabled");
                        var frequencyValue = websiteSchedule.GetValueOrDefault("frequency");
                        var timeValue = websiteSchedule.GetValueOrDefault("time");
                        var dayValue = websiteSchedule.GetValueOrDefault("day");

                        bool enabled = false;
                        string frequency = "daily";
                        string time = "";
                        string day = "Sunday";

                        if (enabledValue is JsonElement enabledEl && enabledEl.ValueKind == JsonValueKind.True)
                            enabled = true;
                        if (frequencyValue is JsonElement frequencyEl)
                            frequency = frequencyEl.GetString() ?? "daily";
                        if (timeValue is JsonElement timeEl)
                            time = timeEl.GetString() ?? "";
                        if (dayValue is JsonElement dayEl)
                            day = dayEl.GetString() ?? "Sunday";

                        LogService.WriteSystemLog($"[CONFIG] Website schedule - enabled: {enabled}, frequency: {frequency}, time: {time}, day: {day}", "Information", "SYSTEM");

                        if (enabled && frequency == "daily")
                        {
                            var timeParts = time.Split(':');
                            if (timeParts.Length == 2)
                            {
                                if (int.TryParse(timeParts[0], out var hour) && int.TryParse(timeParts[1], out var minute))
                                {
                                    Current.Schedule.FtpDailySyncHourMnl = hour;
                                    Current.Schedule.FtpDailySyncMinuteMnl = minute;
                                    LogService.WriteSystemLog($"[CONFIG] FTP schedule updated to {hour}:{minute:D2}", "Information", "SYSTEM");
                                }
                            }
                        }
                        else
                        {
                            LogService.WriteSystemLog($"[CONFIG] FTP schedule NOT updated - enabled={enabled}, frequency={frequency}", "Warning", "SYSTEM");
                        }
                    }
                }

                // Process sql schedule
                if (firebaseSchedule.ContainsKey("sql"))
                {
                    var sqlValue = firebaseSchedule["sql"];
                    Dictionary<string, object>? sqlSchedule = null;

                    if (sqlValue is JsonElement jsonElement)
                    {
                        sqlSchedule = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement);
                    }
                    else if (sqlValue is Dictionary<string, object> dict)
                    {
                        sqlSchedule = dict;
                    }

                    if (sqlSchedule != null)
                    {
                        // Extract actual values from JsonElement
                        var enabledValue = sqlSchedule.GetValueOrDefault("enabled");
                        var frequencyValue = sqlSchedule.GetValueOrDefault("frequency");
                        var timeValue = sqlSchedule.GetValueOrDefault("time");

                        bool enabled = false;
                        string frequency = "daily";
                        string time = "";

                        if (enabledValue is JsonElement enabledEl && enabledEl.ValueKind == JsonValueKind.True)
                            enabled = true;
                        if (frequencyValue is JsonElement frequencyEl)
                            frequency = frequencyEl.GetString() ?? "daily";
                        if (timeValue is JsonElement timeEl)
                            time = timeEl.GetString() ?? "";

                        LogService.WriteSystemLog($"[CONFIG] SQL schedule - enabled: {enabled}, frequency: {frequency}, time: {time}", "Information", "SYSTEM");

                        if (enabled && frequency == "daily")
                        {
                            var timeParts = time.Split(':');
                            if (timeParts.Length == 2)
                            {
                                if (int.TryParse(timeParts[0], out var hour) && int.TryParse(timeParts[1], out var minute))
                                {
                                    Current.Schedule.SqlDailySyncHourMnl = hour;
                                    Current.Schedule.SqlDailySyncMinuteMnl = minute;
                                    LogService.WriteSystemLog($"[CONFIG] SQL schedule updated to {hour}:{minute:D2}", "Information", "SYSTEM");
                                }
                            }
                        }
                        else
                        {
                            LogService.WriteSystemLog($"[CONFIG] SQL schedule NOT updated - enabled={enabled}, frequency={frequency}", "Warning", "SYSTEM");
                        }
                    }
                }

                // Process mailchimp schedule
                if (firebaseSchedule.ContainsKey("mailchimp"))
                {
                    var mcValue = firebaseSchedule["mailchimp"];
                    Dictionary<string, object>? mcSchedule = null;

                    if (mcValue is JsonElement jsonElement)
                    {
                        mcSchedule = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement);
                    }
                    else if (mcValue is Dictionary<string, object> dict)
                    {
                        mcSchedule = dict;
                    }

                    if (mcSchedule != null)
                    {
                        // Extract actual values from JsonElement
                        var enabledValue = mcSchedule.GetValueOrDefault("enabled");
                        var frequencyValue = mcSchedule.GetValueOrDefault("frequency");
                        var timeValue = mcSchedule.GetValueOrDefault("time");

                        bool enabled = false;
                        string frequency = "daily";
                        string time = "";

                        if (enabledValue is JsonElement enabledEl && enabledEl.ValueKind == JsonValueKind.True)
                            enabled = true;
                        if (frequencyValue is JsonElement frequencyEl)
                            frequency = frequencyEl.GetString() ?? "daily";
                        if (timeValue is JsonElement timeEl)
                            time = timeEl.GetString() ?? "";

                        LogService.WriteSystemLog($"[CONFIG] Mailchimp schedule - enabled: {enabled}, frequency: {frequency}, time: {time}", "Information", "SYSTEM");

                        if (enabled && frequency == "daily")
                        {
                            var timeParts = time.Split(':');
                            if (timeParts.Length == 2)
                            {
                                if (int.TryParse(timeParts[0], out var hour) && int.TryParse(timeParts[1], out var minute))
                                {
                                    Current.Schedule.MailchimpDailySyncHourMnl = hour;
                                    Current.Schedule.MailchimpDailySyncMinuteMnl = minute;
                                    LogService.WriteSystemLog($"[CONFIG] Mailchimp schedule updated to {hour}:{minute:D2}", "Information", "SYSTEM");
                                }
                            }
                        }
                        else
                        {
                            LogService.WriteSystemLog($"[CONFIG] Mailchimp schedule NOT updated - enabled={enabled}, frequency={frequency}", "Warning", "SYSTEM");
                        }
                    }
                }

                LogService.WriteSystemLog("[CONFIG] Firebase schedule merged into local config", "Information", "SYSTEM");
                LogService.WriteSystemLog($"[CONFIG] Current schedule after merge - FTP: {Current.Schedule.FtpDailySyncHourMnl}:{Current.Schedule.FtpDailySyncMinuteMnl:D2}, MC: {Current.Schedule.MailchimpDailySyncHourMnl}:{Current.Schedule.MailchimpDailySyncMinuteMnl:D2}, SQL: {Current.Schedule.SqlDailySyncHourMnl}:{Current.Schedule.SqlDailySyncMinuteMnl:D2}", "Information", "SYSTEM");
                SaveSchedule();
                OnScheduleChanged?.Invoke();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[CONFIG] Failed to merge Firebase schedule: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static Dictionary<string, object> GetFirebaseSchedule()
        {
            var schedule = new Dictionary<string, object>();

            // Website/FTP schedule
            schedule["website"] = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["frequency"] = "daily",
                ["time"] = $"{Current.Schedule.FtpDailySyncHourMnl:D2}:{Current.Schedule.FtpDailySyncMinuteMnl:D2}",
                ["day"] = "Everyday"
            };

            // SQL schedule
            schedule["sql"] = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["frequency"] = "daily",
                ["time"] = $"{Current.Schedule.SqlDailySyncHourMnl:D2}:{Current.Schedule.SqlDailySyncMinuteMnl:D2}",
                ["day"] = "Everyday"
            };

            // Mailchimp schedule
            schedule["mailchimp"] = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["frequency"] = "daily",
                ["time"] = $"{Current.Schedule.MailchimpDailySyncHourMnl:D2}:{Current.Schedule.MailchimpDailySyncMinuteMnl:D2}",
                ["day"] = "Everyday"
            };

            return schedule;
        }

        public static Dictionary<string, object> GetFirebaseAutoScan()
        {
            var autoScan = new Dictionary<string, object>();

            // Website/FTP auto scan
            autoScan["website"] = new Dictionary<string, object>
            {
                ["hours"] = Current.Schedule.FtpAutoScanHours,
                ["minutes"] = Current.Schedule.FtpAutoScanMinutes
            };

            // SQL auto scan
            autoScan["sql"] = new Dictionary<string, object>
            {
                ["hours"] = Current.Schedule.SqlAutoScanHours,
                ["minutes"] = Current.Schedule.SqlAutoScanMinutes
            };

            // Mailchimp auto scan
            autoScan["mailchimp"] = new Dictionary<string, object>
            {
                ["hours"] = Current.Schedule.MailchimpAutoScanHours,
                ["minutes"] = Current.Schedule.MailchimpAutoScanMinutes
            };

            return autoScan;
        }

        public static void MergeFirebaseAutoScan(Dictionary<string, object>? firebaseAutoScan)
        {
            if (firebaseAutoScan == null) return;

            try
            {
                // Process website/ftp auto scan
                if (firebaseAutoScan.ContainsKey("website"))
                {
                    var websiteValue = firebaseAutoScan["website"];
                    Dictionary<string, object>? websiteAutoScan = null;

                    if (websiteValue is JsonElement jsonElement)
                    {
                        websiteAutoScan = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement);
                    }
                    else if (websiteValue is Dictionary<string, object> dict)
                    {
                        websiteAutoScan = dict;
                    }

                    if (websiteAutoScan != null)
                    {
                        // Extract actual values from JsonElement
                        var hoursValue = websiteAutoScan.GetValueOrDefault("hours");
                        var minutesValue = websiteAutoScan.GetValueOrDefault("minutes");

                        int hours = 3;
                        int minutes = 0;

                        if (hoursValue is JsonElement hoursEl && hoursEl.ValueKind == JsonValueKind.Number)
                            hours = hoursEl.GetInt32();
                        if (minutesValue is JsonElement minutesEl && minutesEl.ValueKind == JsonValueKind.Number)
                            minutes = minutesEl.GetInt32();

                        Current.Schedule.FtpAutoScanHours = hours;
                        Current.Schedule.FtpAutoScanMinutes = minutes;
                    }
                }

                // Process sql auto scan
                if (firebaseAutoScan.ContainsKey("sql"))
                {
                    var sqlValue = firebaseAutoScan["sql"];
                    Dictionary<string, object>? sqlAutoScan = null;

                    if (sqlValue is JsonElement jsonElement)
                    {
                        sqlAutoScan = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement);
                    }
                    else if (sqlValue is Dictionary<string, object> dict)
                    {
                        sqlAutoScan = dict;
                    }

                    if (sqlAutoScan != null)
                    {
                        // Extract actual values from JsonElement
                        var hoursValue = sqlAutoScan.GetValueOrDefault("hours");
                        var minutesValue = sqlAutoScan.GetValueOrDefault("minutes");

                        int hours = 2;
                        int minutes = 15;

                        if (hoursValue is JsonElement hoursEl && hoursEl.ValueKind == JsonValueKind.Number)
                            hours = hoursEl.GetInt32();
                        if (minutesValue is JsonElement minutesEl && minutesEl.ValueKind == JsonValueKind.Number)
                            minutes = minutesEl.GetInt32();

                        Current.Schedule.SqlAutoScanHours = hours;
                        Current.Schedule.SqlAutoScanMinutes = minutes;
                    }
                }

                // Process mailchimp auto scan
                if (firebaseAutoScan.ContainsKey("mailchimp"))
                {
                    var mcValue = firebaseAutoScan["mailchimp"];
                    Dictionary<string, object>? mailchimpAutoScan = null;

                    if (mcValue is JsonElement jsonElement)
                    {
                        mailchimpAutoScan = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement);
                    }
                    else if (mcValue is Dictionary<string, object> dict)
                    {
                        mailchimpAutoScan = dict;
                    }

                    if (mailchimpAutoScan != null)
                    {
                        // Extract actual values from JsonElement
                        var hoursValue = mailchimpAutoScan.GetValueOrDefault("hours");
                        var minutesValue = mailchimpAutoScan.GetValueOrDefault("minutes");

                        int hours = 2;
                        int minutes = 0;

                        if (hoursValue is JsonElement hoursEl && hoursEl.ValueKind == JsonValueKind.Number)
                            hours = hoursEl.GetInt32();
                        if (minutesValue is JsonElement minutesEl && minutesEl.ValueKind == JsonValueKind.Number)
                            minutes = minutesEl.GetInt32();

                        Current.Schedule.MailchimpAutoScanHours = hours;
                        Current.Schedule.MailchimpAutoScanMinutes = minutes;
                    }
                }

                LogService.WriteSystemLog("[CONFIG] Firebase auto scan merged into local config", "Information", "SYSTEM");
                SaveSchedule();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[CONFIG] Failed to merge Firebase auto scan: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static void MergeFirebaseHealthThresholds(Dictionary<string, object>? firebaseThresholds)
        {
            if (firebaseThresholds == null) return;

            try
            {
                if (firebaseThresholds.ContainsKey("website"))
                {
                    var websiteValue = firebaseThresholds["website"];
                    Dictionary<string, object>? websiteThreshold = null;

                    if (websiteValue is JsonElement jsonElement)
                    {
                        websiteThreshold = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement);
                    }
                    else if (websiteValue is Dictionary<string, object> dict)
                    {
                        websiteThreshold = dict;
                    }

                    if (websiteThreshold != null && websiteThreshold.ContainsKey("maxAgeHours"))
                    {
                        var maxAge = websiteThreshold["maxAgeHours"];
                        int hours = 24;
                        if (maxAge is JsonElement maxAgeEl && maxAgeEl.ValueKind == JsonValueKind.Number)
                            hours = maxAgeEl.GetInt32();
                        else if (maxAge is int h)
                            hours = h;
                        HealthThresholds["website"] = new Dictionary<string, object> { ["maxAgeHours"] = hours };
                    }
                }

                if (firebaseThresholds.ContainsKey("sql"))
                {
                    var sqlValue = firebaseThresholds["sql"];
                    Dictionary<string, object>? sqlThreshold = null;

                    if (sqlValue is JsonElement jsonElement)
                    {
                        sqlThreshold = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement);
                    }
                    else if (sqlValue is Dictionary<string, object> dict)
                    {
                        sqlThreshold = dict;
                    }

                    if (sqlThreshold != null && sqlThreshold.ContainsKey("maxAgeHours"))
                    {
                        var maxAge = sqlThreshold["maxAgeHours"];
                        int hours = 24;
                        if (maxAge is JsonElement maxAgeEl && maxAgeEl.ValueKind == JsonValueKind.Number)
                            hours = maxAgeEl.GetInt32();
                        else if (maxAge is int h)
                            hours = h;
                        HealthThresholds["sql"] = new Dictionary<string, object> { ["maxAgeHours"] = hours };
                    }
                }

                if (firebaseThresholds.ContainsKey("mailchimp"))
                {
                    var mcValue = firebaseThresholds["mailchimp"];
                    Dictionary<string, object>? mailchimpThreshold = null;

                    if (mcValue is JsonElement jsonElement)
                    {
                        mailchimpThreshold = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement);
                    }
                    else if (mcValue is Dictionary<string, object> dict)
                    {
                        mailchimpThreshold = dict;
                    }

                    if (mailchimpThreshold != null && mailchimpThreshold.ContainsKey("maxAgeHours"))
                    {
                        var maxAge = mailchimpThreshold["maxAgeHours"];
                        int hours = 24;
                        if (maxAge is JsonElement maxAgeEl && maxAgeEl.ValueKind == JsonValueKind.Number)
                            hours = maxAgeEl.GetInt32();
                        else if (maxAge is int h)
                            hours = h;
                        HealthThresholds["mailchimp"] = new Dictionary<string, object> { ["maxAgeHours"] = hours };
                    }
                }

                LogService.WriteSystemLog("[CONFIG] Firebase health thresholds merged into local config", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[CONFIG] Failed to merge Firebase health thresholds: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static int GetHealthThreshold(string service)
        {
            if (HealthThresholds.ContainsKey(service))
            {
                var thresholdValue = HealthThresholds[service];
                Dictionary<string, object>? serviceThreshold = null;

                if (thresholdValue is Newtonsoft.Json.Linq.JObject jObject)
                {
                    serviceThreshold = jObject.ToObject<Dictionary<string, object>>();
                }
                else if (thresholdValue is Dictionary<string, object> dict)
                {
                    serviceThreshold = dict;
                }

                if (serviceThreshold != null && serviceThreshold.ContainsKey("maxAgeHours"))
                {
                    var maxAge = serviceThreshold["maxAgeHours"];
                    if (maxAge is int hours) return hours;
                    if (maxAge is long longHours) return (int)longHours;
                    if (maxAge is double doubleHours) return (int)doubleHours;
                    if (maxAge is string strHours && int.TryParse(strHours, out var parsedHours)) return parsedHours;
                }
            }
            // Default thresholds (in hours)
            return service.ToLower() switch
            {
                "mailchimp" => 168, // 7 days
                _ => 24, // 1 day for website/sql
            };
        }

        private static void MergeInto(AppSettings target, AppSettings source)
        {
            if (!string.IsNullOrWhiteSpace(source.Paths.FtpLocalFolder)) target.Paths.FtpLocalFolder = source.Paths.FtpLocalFolder;
            if (!string.IsNullOrWhiteSpace(source.Paths.MailchimpFolder)) target.Paths.MailchimpFolder = source.Paths.MailchimpFolder;
            if (!string.IsNullOrWhiteSpace(source.Paths.SqlLocalFolder)) target.Paths.SqlLocalFolder = source.Paths.SqlLocalFolder;

            if (!string.IsNullOrWhiteSpace(source.Ftp.Host)) target.Ftp.Host = source.Ftp.Host;
            if (!string.IsNullOrWhiteSpace(source.Ftp.User)) target.Ftp.User = source.Ftp.User;
            if (!string.IsNullOrWhiteSpace(source.Ftp.Password)) target.Ftp.Password = source.Ftp.Password;
            if (!string.IsNullOrWhiteSpace(source.Ftp.TlsFingerprint)) target.Ftp.TlsFingerprint = source.Ftp.TlsFingerprint;
            if (source.Ftp.Port != 0) target.Ftp.Port = source.Ftp.Port;

            if (!string.IsNullOrWhiteSpace(source.Sql.Host)) target.Sql.Host = source.Sql.Host;
            if (!string.IsNullOrWhiteSpace(source.Sql.User)) target.Sql.User = source.Sql.User;
            if (!string.IsNullOrWhiteSpace(source.Sql.Password)) target.Sql.Password = source.Sql.Password;
            if (!string.IsNullOrWhiteSpace(source.Sql.RemotePath)) target.Sql.RemotePath = source.Sql.RemotePath;
            if (!string.IsNullOrWhiteSpace(source.Sql.TlsFingerprint)) target.Sql.TlsFingerprint = source.Sql.TlsFingerprint;

            if (!string.IsNullOrWhiteSpace(source.Mailchimp.ApiKey)) target.Mailchimp.ApiKey = source.Mailchimp.ApiKey;
            if (!string.IsNullOrWhiteSpace(source.Mailchimp.AudienceId)) target.Mailchimp.AudienceId = source.Mailchimp.AudienceId;

            if (source.Schedule.FtpDailySyncHourMnl != 0) target.Schedule.FtpDailySyncHourMnl = source.Schedule.FtpDailySyncHourMnl;
            if (source.Schedule.FtpDailySyncMinuteMnl != 0) target.Schedule.FtpDailySyncMinuteMnl = source.Schedule.FtpDailySyncMinuteMnl;
            if (source.Schedule.MailchimpDailySyncHourMnl != 0) target.Schedule.MailchimpDailySyncHourMnl = source.Schedule.MailchimpDailySyncHourMnl;
            if (source.Schedule.MailchimpDailySyncMinuteMnl != 0) target.Schedule.MailchimpDailySyncMinuteMnl = source.Schedule.MailchimpDailySyncMinuteMnl;
            if (source.Schedule.SqlDailySyncHourMnl != 0) target.Schedule.SqlDailySyncHourMnl = source.Schedule.SqlDailySyncHourMnl;
            if (source.Schedule.SqlDailySyncMinuteMnl != 0) target.Schedule.SqlDailySyncMinuteMnl = source.Schedule.SqlDailySyncMinuteMnl;

            if (source.Schedule.FtpAutoScanHours != 0) target.Schedule.FtpAutoScanHours = source.Schedule.FtpAutoScanHours;
            if (source.Schedule.FtpAutoScanMinutes != 0) target.Schedule.FtpAutoScanMinutes = source.Schedule.FtpAutoScanMinutes;
            if (source.Schedule.MailchimpAutoScanHours != 0) target.Schedule.MailchimpAutoScanHours = source.Schedule.MailchimpAutoScanHours;
            if (source.Schedule.MailchimpAutoScanMinutes != 0) target.Schedule.MailchimpAutoScanMinutes = source.Schedule.MailchimpAutoScanMinutes;
            if (source.Schedule.SqlAutoScanHours != 0) target.Schedule.SqlAutoScanHours = source.Schedule.SqlAutoScanHours;
            if (source.Schedule.SqlAutoScanMinutes != 0) target.Schedule.SqlAutoScanMinutes = source.Schedule.SqlAutoScanMinutes;

            if (source.Operation.RetentionDays != 0) target.Operation.RetentionDays = source.Operation.RetentionDays;
            if (source.Operation.AutoStartWindows) target.Operation.AutoStartWindows = true;
        }
    }
}
