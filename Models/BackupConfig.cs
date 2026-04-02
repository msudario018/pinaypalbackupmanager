using System.IO;

namespace PinayPalBackupManager.Models
{
    public static class BackupConfig
    {
        public const string AppVersion = "v2.3 Unified";
        public const string CreatorName = "Wesley";

        // Folder & Log Definitions
        public static string FtpLocalFolder { get; set; } = "";
        public static string MailchimpFolder { get; set; } = "";
        public static string SqlLocalFolder { get; set; } = "";

        public static string FtpLogFile => Path.Combine(FtpLocalFolder, "backup_log.txt");
        public static string McLogFile => Path.Combine(MailchimpFolder, "backup_log.txt");
        public static string SqlLogFile => Path.Combine(SqlLocalFolder, "backup_log.txt");

        // FTP Credentials (Website)
        public const string FtpHost = "";
        public const string FtpUser = "";
        public const string FtpEncryptedPass = "";
        public const string FtpTlsFingerprint = "";
        public static readonly byte[] FtpSecretKey = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

        // SQL Credentials
        public const string SqlUser = "";
        public const string SqlEncryptedPass = "";
        public const string SqlRemotePath = "";
        public const string SqlTlsFingerprint = "";

        // Mailchimp Config
        public const string McApiKey = "";
        public const string McAudienceId = "";

        // Intervals (Minutes)
        public const int FtpDailySyncHourMnl = 22;
        public const int FtpDailySyncMinuteMnl = 0;

        public const int MailchimpDailySyncHourMnl = 18;
        public const int MailchimpDailySyncMinuteMnl = 0;

        public const int SqlDailySyncHourMnl = 17;
        public const int SqlDailySyncMinuteMnl = 0;
    }
}
