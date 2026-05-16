using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PinayPalBackupManager.Services
{
    public static class EnvironmentConfigService
    {
        private static readonly string ConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinayPalBackupManager", "environment.json");
        
        private static EnvironmentConfig? _currentConfig;
        private static readonly object _configLock = new object();
        
        public static string CurrentEnvironment { get; private set; } = "Production";
        
        public static void Initialize()
        {
            LoadConfig();
            
            // Ensure all required directories exist
            try
            {
                GetBackupPath(); // This will create the backup directory
                GetLogPath();    // This will create the log directory
                GetAvatarsPath(); // This will create the avatars directory
                GetTempPath();   // This will use system temp (no creation needed)
                
                LogService.WriteSystemLog("Environment directories initialized successfully", "Information", "ENVIRONMENT");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to initialize environment directories: {ex.Message}", "Error", "ENVIRONMENT");
            }
        }
        
        public static void SetEnvironment(string environment)
        {
            lock (_configLock)
            {
                CurrentEnvironment = environment;
                LoadConfig();
            }
        }
        
        public static EnvironmentConfig GetConfig()
        {
            lock (_configLock)
            {
                return _currentConfig ?? new EnvironmentConfig();
            }
        }
        
        public static string GetSetting(string key, string defaultValue = "")
        {
            var config = GetConfig();
            return config.Settings.GetValueOrDefault(key, defaultValue);
        }
        
        public static void SetSetting(string key, string value)
        {
            lock (_configLock)
            {
                if (_currentConfig == null)
                    _currentConfig = new EnvironmentConfig();
                
                _currentConfig.Settings[key] = value;
                SaveConfig();
            }
        }
        
        public static string GetConnectionString()
        {
            return GetSetting("ConnectionString", "");
        }
        
        public static string GetFirebaseUrl()
        {
            return GetSetting("FirebaseUrl", "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/");
        }
        
        public static int GetSessionTimeoutMinutes()
        {
            var value = GetSetting("SessionTimeoutMinutes", "30");
            return int.TryParse(value, out var minutes) ? minutes : 30;
        }
        
        public static int GetRateLimitAttempts()
        {
            var value = GetSetting("RateLimitAttempts", "5");
            return int.TryParse(value, out var attempts) ? attempts : 5;
        }
        
        public static int GetRateLimitWindowMinutes()
        {
            var value = GetSetting("RateLimitWindowMinutes", "15");
            return int.TryParse(value, out var minutes) ? minutes : 15;
        }
        
        public static bool IsDebugMode()
        {
            var value = GetSetting("DebugMode", "false");
            return bool.TryParse(value, out var debug) && debug;
        }
        
        public static bool EnableAuditLogging()
        {
            var value = GetSetting("EnableAuditLogging", "true");
            return bool.TryParse(value, out var enable) && enable;
        }
        
        public static int GetAuditLogRetentionDays()
        {
            var value = GetSetting("AuditLogRetentionDays", "90");
            return int.TryParse(value, out var days) ? days : 90;
        }
        
        public static string GetBackupPath()
        {
            var configured = GetSetting("BackupPath", "");
            string path;
            
            if (!string.IsNullOrEmpty(configured))
            {
                path = configured;
            }
            else
            {
                // Default to Documents folder
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PinayPalBackups");
            }
            
            // Ensure directory exists
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to create backup directory: {path} - {ex.Message}", "Error", "ENVIRONMENT");
                }
            }
            
            return path;
        }
        
        public static string GetLogPath()
        {
            var configured = GetSetting("LogPath", "");
            string path;
            
            if (!string.IsNullOrEmpty(configured))
            {
                path = configured;
            }
            else
            {
                // Default to AppData
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager", "logs");
            }
            
            // Ensure directory exists
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"Failed to create log directory: {path} - {ex.Message}", "Error", "ENVIRONMENT");
                }
            }
            
            return path;
        }
        
        public static string GetTempPath()
        {
            var configured = GetSetting("TempPath", "");
            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
                return configured;
            
            // Default to system temp
            return Path.GetTempPath();
        }
        
        public static string GetAvatarsPath()
        {
            var configured = GetSetting("AvatarsPath", "");
            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
                return configured;
            
            // Default to AppData/Avatars
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinayPalBackupManager", "Avatars");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }
        
        private static void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigFile))
                {
                    _currentConfig = CreateDefaultConfig();
                    SaveConfig();
                    return;
                }
                
                var json = File.ReadAllText(ConfigFile);
                var allConfigs = JsonSerializer.Deserialize<Dictionary<string, EnvironmentConfig>>(json);
                
                if (allConfigs != null && allConfigs.TryGetValue(CurrentEnvironment, out var config))
                {
                    _currentConfig = config;
                }
                else
                {
                    _currentConfig = CreateDefaultConfig();
                }
            }
            catch
            {
                _currentConfig = CreateDefaultConfig();
            }
        }
        
        private static void SaveConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                
                var allConfigs = new Dictionary<string, EnvironmentConfig>();
                
                // Load existing configs if file exists
                if (File.Exists(ConfigFile))
                {
                    try
                    {
                        var json = File.ReadAllText(ConfigFile);
                        var existing = JsonSerializer.Deserialize<Dictionary<string, EnvironmentConfig>>(json);
                        if (existing != null)
                            allConfigs = existing;
                    }
                    catch
                    {
                        // If loading fails, start fresh
                    }
                }
                
                // Update current environment config
                allConfigs[CurrentEnvironment] = _currentConfig ?? new EnvironmentConfig();
                
                var jsonToSave = JsonSerializer.Serialize(allConfigs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, jsonToSave);
            }
            catch
            {
                // Silently fail - config saving is not critical
            }
        }
        
        private static EnvironmentConfig CreateDefaultConfig()
        {
            return new EnvironmentConfig
            {
                Environment = CurrentEnvironment,
                Settings = new Dictionary<string, string>
                {
                    { "ConnectionString", "" },
                    { "FirebaseUrl", "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/" },
                    { "SessionTimeoutMinutes", "30" },
                    { "RateLimitAttempts", "5" },
                    { "RateLimitWindowMinutes", "15" },
                    { "DebugMode", "false" },
                    { "EnableAuditLogging", "true" },
                    { "AuditLogRetentionDays", "90" },
                    { "BackupPath", "" },
                    { "LogPath", "" },
                    { "TempPath", "" }
                }
            };
        }
        
        public static void ResetToDefaults()
        {
            lock (_configLock)
            {
                _currentConfig = CreateDefaultConfig();
                SaveConfig();
            }
        }
    }
    
    public class EnvironmentConfig
    {
        public string Environment { get; set; } = "Production";
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
    }
}
