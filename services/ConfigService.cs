using System;
using System.IO;
using System.Text.Json;

namespace PinayPalBackupManager.Services
{
    public static class ConfigService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static AppSettings Current { get; private set; } = new();

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
