namespace PinayPalBackupManager.Services
{
    public sealed class AppSettings
    {
        public PathsSettings Paths { get; set; } = new();
        public FtpSettings Ftp { get; set; } = new();
        public SqlSettings Sql { get; set; } = new();
        public MailchimpSettings Mailchimp { get; set; } = new();
        public ScheduleSettings Schedule { get; set; } = new();
    }

    public sealed class PathsSettings
    {
        public string FtpLocalFolder { get; set; } = string.Empty;
        public string MailchimpFolder { get; set; } = string.Empty;
        public string SqlLocalFolder { get; set; } = string.Empty;
    }

    public sealed class FtpSettings
    {
        public string Host { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string TlsFingerprint { get; set; } = string.Empty;
        public int Port { get; set; } = 21;
    }

    public sealed class SqlSettings
    {
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string RemotePath { get; set; } = string.Empty;
        public string TlsFingerprint { get; set; } = string.Empty;
    }

    public sealed class MailchimpSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string AudienceId { get; set; } = string.Empty;
    }

    public sealed class ScheduleSettings
    {
        public int FtpDailySyncHourMnl { get; set; } = 22;
        public int FtpDailySyncMinuteMnl { get; set; } = 0;
        public int MailchimpDailySyncHourMnl { get; set; } = 18;
        public int MailchimpDailySyncMinuteMnl { get; set; } = 0;
        public int SqlDailySyncHourMnl { get; set; } = 17;
        public int SqlDailySyncMinuteMnl { get; set; } = 0;
    }
}
