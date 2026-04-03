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
            var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            var (sharedPath, _) = FindConfigPaths(baseDir);
            if (!string.IsNullOrWhiteSpace(sharedPath))
            {
                return Path.GetDirectoryName(sharedPath) ?? baseDir;
            }

            return baseDir;
        }

        public static void Load()
        {
            var settings = new AppSettings();

            var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            var (sharedPath, localPath) = FindConfigPaths(baseDir);

            if (!string.IsNullOrEmpty(sharedPath))
            {
                MergeInto(settings, ReadFile(sharedPath));
            }

            if (!string.IsNullOrEmpty(localPath))
            {
                MergeInto(settings, ReadFile(localPath));
            }

            Current = settings;
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
        }
    }
}
