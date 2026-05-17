namespace PinayPalBackupManager.Services
{
    public sealed class AppSettings
    {
        public PathsSettings Paths { get; set; } = new();
        public FtpSettings Ftp { get; set; } = new();
        public SqlSettings Sql { get; set; } = new();
        public MailchimpSettings Mailchimp { get; set; } = new();
        public NetworkDriveSettings NetworkDrive { get; set; } = new();
        public ScheduleSettings Schedule { get; set; } = new();
        public OperationSettings Operation { get; set; } = new();
        public HttpServerSettings HttpServer { get; set; } = new();
    }

    public sealed class OperationSettings
    {
        public int RetentionDays { get; set; } = 7;
        public bool AutoStartWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool NotificationSound { get; set; } = true;
        public bool ThemeAutoSchedule { get; set; } = false;
        public int ThemeDarkHour { get; set; } = 18; // 6 PM
        public int ThemeLightHour { get; set; } = 6; // 6 AM
        public string Language { get; set; } = "en";
        public bool SetupCompleted { get; set; } = false;
        public int AutoIntervalMinutes { get; set; } = 60;
    }

    public sealed class PathsSettings
    {
        public string FtpLocalFolder { get; set; } = string.Empty;
        public string MailchimpFolder { get; set; } = string.Empty;
        public string SqlLocalFolder { get; set; } = string.Empty;
        public string NetworkDriveFolder { get; set; } = string.Empty;
    }

    public sealed class FtpSettings
    {
        public string Host { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string TlsFingerprint { get; set; } = string.Empty;
        public string LocalFolder { get; set; } = string.Empty;
        public int Port { get; set; } = 21;
    }

    public sealed class SqlSettings
    {
        public string Host { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string RemotePath { get; set; } = string.Empty;
        public string TlsFingerprint { get; set; } = string.Empty;
        public string LocalFolder { get; set; } = string.Empty;
    }

    public sealed class MailchimpSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string AudienceId { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
    }

    public sealed class NetworkDriveSettings
    {
        public string Path { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool Enabled { get; set; } = false;
    }

    public sealed class ScheduleSettings
    {
        public int FtpDailySyncHourMnl { get; set; } = 22;
        public int FtpDailySyncMinuteMnl { get; set; } = 0;
        public int MailchimpDailySyncHourMnl { get; set; } = 18;
        public int MailchimpDailySyncMinuteMnl { get; set; } = 0;
        public int SqlDailySyncHourMnl { get; set; } = 17;
        public int SqlDailySyncMinuteMnl { get; set; } = 0;

        public int FtpAutoScanHours { get; set; } = 3;
        public int FtpAutoScanMinutes { get; set; } = 0;
        public int MailchimpAutoScanHours { get; set; } = 2;
        public int MailchimpAutoScanMinutes { get; set; } = 0;
        public int SqlAutoScanHours { get; set; } = 2;
        public int SqlAutoScanMinutes { get; set; } = 15;
        
        // Schedule days (0 = Sunday, 1 = Monday, etc.) - empty means every day
        public bool ScheduleSunday { get; set; } = true;
        public bool ScheduleMonday { get; set; } = true;
        public bool ScheduleTuesday { get; set; } = true;
        public bool ScheduleWednesday { get; set; } = true;
        public bool ScheduleThursday { get; set; } = true;
        public bool ScheduleFriday { get; set; } = true;
        public bool ScheduleSaturday { get; set; } = true;
    }

    public sealed class HttpServerSettings
    {
        public int Port { get; set; } = 8080;
        public bool Enabled { get; set; } = true;
    }
}
