using System;
using System.Collections.Generic;
using System.IO;

namespace PinayPalBackupManager.Services
{
    public static class LocalizationService
    {
        private static string _currentLanguage = "en";
        
        public static string CurrentLanguage => _currentLanguage;
        
        public static event Action? OnLanguageChanged;
        
        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
        {
            ["en"] = new Dictionary<string, string>
            {
                // Navigation
                ["nav_home"] = "Home",
                ["nav_ftp"] = "FTP/Website",
                ["nav_mailchimp"] = "Mailchimp",
                ["nav_sql"] = "SQL",
                ["nav_settings"] = "Settings",
                ["nav_profile"] = "Profile",
                ["nav_admin"] = "Admin",
                
                // Dashboard
                ["dashboard_title"] = "Dashboard",
                ["dashboard_subtitle"] = "System health overview",
                ["run_all"] = "Run All",
                ["view_all_backups"] = "View All Backups",
                ["customize"] = "Customize",
                
                // Quick Stats
                ["backups_today"] = "Backups Today",
                ["success_rate"] = "Success Rate (24h)",
                ["failed_backups"] = "Failed Backups",
                ["storage_used"] = "Storage Used",
                
                // Schedule
                ["schedule_overview"] = "SCHEDULE OVERVIEW",
                ["next_backup"] = "Next in:",
                ["upcoming"] = "UPCOMING",
                ["schedule_adjustment"] = "SCHEDULE ADJUSTMENT",
                ["daily_schedule"] = "DAILY SCHEDULE",
                ["edit_schedule"] = "Edit Schedule",
                
                // Service Cards
                ["ftp_service"] = "FTP/Website",
                ["mailchimp_service"] = "Mailchimp",
                ["sql_service"] = "SQL Database",
                ["last_backup"] = "Last Backup",
                ["next_scan"] = "Next Scan",
                ["sync_check"] = "Sync Check",
                ["quick_backup"] = "Quick Backup",
                ["view_files"] = "Files",
                ["view_log"] = "View Log",
                ["stop"] = "Stop",
                
                // Status
                ["status_healthy"] = "Healthy",
                ["status_warning"] = "Warning",
                ["status_error"] = "Error",
                ["status_outdated"] = "Outdated",
                ["status_idle"] = "Idle",
                ["status_running"] = "Running",
                ["status_completed"] = "Completed",
                ["status_failed"] = "Failed",
                
                // Activity
                ["recent_activity"] = "RECENT ACTIVITY",
                ["export_csv"] = "Export CSV",
                ["refresh"] = "Refresh",
                
                // Calendar
                ["backup_history"] = "BACKUP HISTORY (LAST 30 DAYS)",
                
                // Settings
                ["system_configuration"] = "SYSTEM CONFIGURATION",
                ["startup"] = "Run Backup on Windows Start Up",
                ["start_minimized"] = "Start Minimized to Tray",
                ["notification_sound"] = "Play Sound on Backup Complete",
                ["theme_auto_schedule"] = "Auto Theme Schedule (Dark at 6PM, Light at 6AM)",
                ["backup_retention"] = "Backup Retention",
                ["retention_days"] = "days to keep old backup files before auto-delete",
                ["save_retention"] = "Save Retention Policy",
                ["export_logs"] = "Export Logs (ZIP)",
                
                // System Info
                ["system_information"] = "SYSTEM INFORMATION",
                ["app_version"] = "App Version:",
                ["last_update_check"] = "Last Update Check:",
                ["check_updates"] = "Check for Updates",
                ["auto_update"] = "Auto-check for updates on startup",
                
                // Credentials
                ["credentials_paths"] = "CREDENTIALS & PATHS",
                ["edit_credentials"] = "Edit Credentials",
                ["edit_paths"] = "Edit Paths",
                
                // Diagnostics
                ["system_diagnostics"] = "SYSTEM DIAGNOSTICS",
                ["run_diagnostics"] = "RUN SYSTEM DIAGNOSTICS",
                ["health_status"] = "Status:",
                
                // Common
                ["save"] = "Save",
                ["cancel"] = "Cancel",
                ["close"] = "Close",
                ["apply"] = "Apply",
                ["ok"] = "OK",
                ["yes"] = "Yes",
                ["no"] = "No",
                ["loading"] = "Loading...",
                ["error"] = "Error",
                ["success"] = "Success",
                ["warning"] = "Warning",
                ["info"] = "Info",
                ["language"] = "Language",
                ["language_description"] = "Select your preferred language",
                
                // Keyboard shortcuts
                ["shortcut_backup_all"] = "Backup All",
                ["shortcut_test_all"] = "Test All",
                ["shortcut_retry"] = "Retry Failed",
                ["shortcut_stop"] = "Emergency Stop",
            },
            
            ["fil"] = new Dictionary<string, string>
            {
                // Navigation
                ["nav_home"] = "Home",
                ["nav_ftp"] = "FTP/Website",
                ["nav_mailchimp"] = "Mailchimp",
                ["nav_sql"] = "SQL",
                ["nav_settings"] = "Mga Setting",
                ["nav_profile"] = "Profile",
                ["nav_admin"] = "Admin",
                
                // Dashboard
                ["dashboard_title"] = "Dashboard",
                ["dashboard_subtitle"] = "Pangkalahatang kalusugan ng sistema",
                ["run_all"] = "Patakbuhin Lahat",
                ["view_all_backups"] = "Tingnan Lahat na Backup",
                ["customize"] = "I-customize",
                
                // Quick Stats
                ["backups_today"] = "Mga Backup Ngayon",
                ["success_rate"] = "Rate ng Tagumpay (24h)",
                ["failed_backups"] = "Nabigong Backup",
                ["storage_used"] = "Ginagamit na Storage",
                
                // Schedule
                ["schedule_overview"] = "PANGKALAHATANG ISKEDUL",
                ["next_backup"] = "Susunod sa:",
                ["upcoming"] = "MALAPIT NA",
                ["schedule_adjustment"] = "PAGBABAGO NG ISKEDUL",
                ["daily_schedule"] = "ARAW-ARAW NA ISKEDUL",
                ["edit_schedule"] = "I-edit ang Iskedul",
                
                // Service Cards
                ["ftp_service"] = "FTP/Website",
                ["mailchimp_service"] = "Mailchimp",
                ["sql_service"] = "SQL Database",
                ["last_backup"] = "Huling Backup",
                ["next_scan"] = "Susunod na Scan",
                ["sync_check"] = "Suriin ang Sync",
                ["quick_backup"] = "Mabilis na Backup",
                ["view_files"] = "Mga File",
                ["view_log"] = "Tingnan ang Log",
                ["stop"] = "Itigil",
                
                // Status
                ["status_healthy"] = "Malusog",
                ["status_warning"] = "Babala",
                ["status_error"] = "Error",
                ["status_outdated"] = "Luma",
                ["status_idle"] = "Walang Ginagawa",
                ["status_running"] = "Tumatakbo",
                ["status_completed"] = "Tapos na",
                ["status_failed"] = "Nabigo",
                
                // Activity
                ["recent_activity"] = "KAMAKATIRANG AKTIBIDAD",
                ["export_csv"] = "I-export ang CSV",
                ["refresh"] = "I-refresh",
                
                // Calendar
                ["backup_history"] = "KASAYSAYAN NG BACKUP (LAST 30 ARAW)",
                
                // Settings
                ["system_configuration"] = "KONFIGURASYON NG SISTEMA",
                ["startup"] = "Patakbuhin ang Backup sa Pagsisimula ng Windows",
                ["start_minimized"] = "Simulan nang Minimized sa Tray",
                ["notification_sound"] = "Mag-play ng Tunog sa Pagkumpleto ng Backup",
                ["theme_auto_schedule"] = "Auto Theme Schedule (Dark sa 6PM, Light sa 6AM)",
                ["backup_retention"] = "Retention ng Backup",
                ["retention_days"] = "araw para panatilihin ang mga lumang backup bago auto-delete",
                ["save_retention"] = "I-save ang Retention Policy",
                ["export_logs"] = "I-export ang Logs (ZIP)",
                
                // System Info
                ["system_information"] = "IMPORMASYON NG SISTEMA",
                ["app_version"] = "Bersyon ng App:",
                ["last_update_check"] = "Huling Check ng Update:",
                ["check_updates"] = "Mag-check ng Updates",
                ["auto_update"] = "Auto-check ng updates sa pagsisimula",
                
                // Credentials
                ["credentials_paths"] = "MGA CREDENTIALS AT PATH",
                ["edit_credentials"] = "I-edit ang Credentials",
                ["edit_paths"] = "I-edit ang Paths",
                
                // Diagnostics
                ["system_diagnostics"] = "DIAGNOSTICS NG SISTEMA",
                ["run_diagnostics"] = "PATAKbuhin ANG SYSTEM DIAGNOSTICS",
                ["health_status"] = "Status:",
                
                // Common
                ["save"] = "I-save",
                ["cancel"] = "Kanselahin",
                ["close"] = "Isara",
                ["apply"] = "Ilapat",
                ["ok"] = "OK",
                ["yes"] = "Oo",
                ["no"] = "Hindi",
                ["loading"] = "Naglo-load...",
                ["error"] = "Error",
                ["success"] = "Tagumpay",
                ["warning"] = "Babala",
                ["info"] = "Impormasyon",
                ["language"] = "Wika",
                ["language_description"] = "Piliin ang iyong nais na wika",
                
                // Keyboard shortcuts
                ["shortcut_backup_all"] = "Backup Lahat",
                ["shortcut_test_all"] = "Subukan Lahat",
                ["shortcut_retry"] = "Subukang Muli",
                ["shortcut_stop"] = "Emergency Stop",
            }
        };
        
        public static void Load()
        {
            try
            {
                var langFile = Path.Combine(AppDataPaths.CurrentDirectory, "language.txt");
                if (File.Exists(langFile))
                {
                    var lang = File.ReadAllText(langFile).Trim().ToLower();
                    if (Translations.ContainsKey(lang))
                        _currentLanguage = lang;
                }
            }
            catch { }
        }
        
        public static void SetLanguage(string lang)
        {
            if (Translations.ContainsKey(lang))
            {
                _currentLanguage = lang;
                System.Diagnostics.Debug.WriteLine($"[SetLanguage] Language changed to: {lang}");
                try
                {
                    var langFile = Path.Combine(AppDataPaths.CurrentDirectory, "language.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(langFile)!);
                    File.WriteAllText(langFile, lang);
                }
                catch { }
                System.Diagnostics.Debug.WriteLine($"[SetLanguage] Invoking OnLanguageChanged");
                OnLanguageChanged?.Invoke();
            }
        }
        
        public static string Get(string key)
        {
            if (Translations.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var value))
            {
                System.Diagnostics.Debug.WriteLine($"[Get] Found in {_currentLanguage}: {key} -> {value}");
                return value;
            }
            if (Translations.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enValue))
            {
                System.Diagnostics.Debug.WriteLine($"[Get] Fallback to en: {key} -> {enValue}");
                return enValue;
            }
            System.Diagnostics.Debug.WriteLine($"[Get] Not found: {key}");
            return key;
        }
        
        public static string[] GetAvailableLanguages() => new[] { "en", "fil" };
        
        public static string GetLanguageName(string code) => code switch
        {
            "en" => "English",
            "fil" => "Filipino",
            _ => code
        };
    }
}
