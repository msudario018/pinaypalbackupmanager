using System.IO;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.Models
{
    public static class BackupConfig
    {
        public static string AppVersion
        {
            get
            {
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v?.?.?";
            }
        }
        public const string CreatorName = "Wesley";

        // Folder & Log Definitions
        public static string FtpLocalFolder
        {
            get => ConfigService.Current.Paths?.FtpLocalFolder ?? string.Empty;
            set { if (ConfigService.Current.Paths != null) ConfigService.Current.Paths.FtpLocalFolder = value; }
        }

        public static string MailchimpFolder
        {
            get => ConfigService.Current.Paths?.MailchimpFolder ?? string.Empty;
            set { if (ConfigService.Current.Paths != null) ConfigService.Current.Paths.MailchimpFolder = value; }
        }

        public static string SqlLocalFolder
        {
            get => ConfigService.Current.Paths?.SqlLocalFolder ?? string.Empty;
            set { if (ConfigService.Current.Paths != null) ConfigService.Current.Paths.SqlLocalFolder = value; }
        }

        public static string NetworkDriveFolder
        {
            get => ConfigService.Current.Paths?.NetworkDriveFolder ?? string.Empty;
            set { if (ConfigService.Current.Paths != null) ConfigService.Current.Paths.NetworkDriveFolder = value; }
        }

        public static string FtpLogFile => Path.Combine(FtpLocalFolder, "backup_log.txt");
        public static string McLogFile => Path.Combine(MailchimpFolder, "backup_log.txt");
        public static string SqlLogFile => Path.Combine(SqlLocalFolder, "backup_log.txt");
        public static string NetworkDriveLogFile => Path.Combine(NetworkDriveFolder, "backup_log.txt");

        // FTP Credentials (Website)
        public static string FtpHost => ConfigService.Current.Ftp?.Host ?? string.Empty;
        public static string FtpUser => ConfigService.Current.Ftp?.User ?? string.Empty;
        public static string FtpTlsFingerprint => ConfigService.Current.Ftp?.TlsFingerprint ?? string.Empty;
        public static int FtpPort => ConfigService.Current.Ftp?.Port ?? 21;

        // SQL Credentials
        public static string SqlUser => ConfigService.Current.Sql?.User ?? string.Empty;
        public static string SqlRemotePath => ConfigService.Current.Sql?.RemotePath ?? string.Empty;
        public static string SqlTlsFingerprint => ConfigService.Current.Sql?.TlsFingerprint ?? string.Empty;

        // Mailchimp Config
        public static string McApiKey => ConfigService.Current.Mailchimp?.ApiKey ?? string.Empty;
        public static string McAudienceId => ConfigService.Current.Mailchimp?.AudienceId ?? string.Empty;

        // Network Drive Config
        public static string NetworkDrivePath => ConfigService.Current.NetworkDrive?.Path ?? string.Empty;
        public static string NetworkDriveUsername => ConfigService.Current.NetworkDrive?.Username ?? string.Empty;
        public static string NetworkDrivePassword => ConfigService.Current.NetworkDrive?.Password ?? string.Empty;
        public static bool NetworkDriveEnabled => ConfigService.Current.NetworkDrive?.Enabled ?? false;

        // Intervals (Minutes)
        public static int FtpDailySyncHourMnl => ConfigService.Current.Schedule?.FtpDailySyncHourMnl ?? 22;
        public static int FtpDailySyncMinuteMnl => ConfigService.Current.Schedule?.FtpDailySyncMinuteMnl ?? 0;

        public static int MailchimpDailySyncHourMnl => ConfigService.Current.Schedule?.MailchimpDailySyncHourMnl ?? 18;
        public static int MailchimpDailySyncMinuteMnl => ConfigService.Current.Schedule?.MailchimpDailySyncMinuteMnl ?? 0;

        public static int SqlDailySyncHourMnl => ConfigService.Current.Schedule?.SqlDailySyncHourMnl ?? 17;
        public static int SqlDailySyncMinuteMnl => ConfigService.Current.Schedule?.SqlDailySyncMinuteMnl ?? 0;

    }
}
