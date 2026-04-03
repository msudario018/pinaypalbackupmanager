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
            get => ConfigService.Current.Paths.FtpLocalFolder;
            set => ConfigService.Current.Paths.FtpLocalFolder = value;
        }

        public static string MailchimpFolder
        {
            get => ConfigService.Current.Paths.MailchimpFolder;
            set => ConfigService.Current.Paths.MailchimpFolder = value;
        }

        public static string SqlLocalFolder
        {
            get => ConfigService.Current.Paths.SqlLocalFolder;
            set => ConfigService.Current.Paths.SqlLocalFolder = value;
        }

        public static string FtpLogFile => Path.Combine(FtpLocalFolder, "backup_log.txt");
        public static string McLogFile => Path.Combine(MailchimpFolder, "backup_log.txt");
        public static string SqlLogFile => Path.Combine(SqlLocalFolder, "backup_log.txt");

        // FTP Credentials (Website)
        public static string FtpHost => ConfigService.Current.Ftp.Host;
        public static string FtpUser => ConfigService.Current.Ftp.User;
        public static string FtpTlsFingerprint => ConfigService.Current.Ftp.TlsFingerprint;
        public static int FtpPort => ConfigService.Current.Ftp.Port;

        // SQL Credentials
        public static string SqlUser => ConfigService.Current.Sql.User;
        public static string SqlRemotePath => ConfigService.Current.Sql.RemotePath;
        public static string SqlTlsFingerprint => ConfigService.Current.Sql.TlsFingerprint;

        // Mailchimp Config
        public static string McApiKey => ConfigService.Current.Mailchimp.ApiKey;
        public static string McAudienceId => ConfigService.Current.Mailchimp.AudienceId;

        // Intervals (Minutes)
        public static int FtpDailySyncHourMnl => ConfigService.Current.Schedule.FtpDailySyncHourMnl;
        public static int FtpDailySyncMinuteMnl => ConfigService.Current.Schedule.FtpDailySyncMinuteMnl;

        public static int MailchimpDailySyncHourMnl => ConfigService.Current.Schedule.MailchimpDailySyncHourMnl;
        public static int MailchimpDailySyncMinuteMnl => ConfigService.Current.Schedule.MailchimpDailySyncMinuteMnl;

        public static int SqlDailySyncHourMnl => ConfigService.Current.Schedule.SqlDailySyncHourMnl;
        public static int SqlDailySyncMinuteMnl => ConfigService.Current.Schedule.SqlDailySyncMinuteMnl;
    }
}
