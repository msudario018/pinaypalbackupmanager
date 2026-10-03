using System;

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
        public WindowsSettings Windows { get; set; } = new();
    }

    public sealed class OperationSettings
    {
        public int RetentionDays { get; set; } = 7;
        public bool AutoStartWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool CloseToTray { get; set; } = false;
        public bool AutoUpdateTlsFingerprint { get; set; } = true;
        public bool AcceptAnyTlsCert { get; set; } = false;
        public bool DailyHealthCheckEnabled { get; set; } = true;
        public int DailyHealthCheckHour { get; set; } = 8;
        public bool NotificationSound { get; set; } = true;
        public bool ThemeAutoSchedule { get; set; } = false;
        public int ThemeDarkHour { get; set; } = 18; // 6 PM
        public int ThemeLightHour { get; set; } = 6; // 6 AM
        public string Language { get; set; } = "en";
        public bool SetupCompleted { get; set; } = false;
        public int AutoIntervalMinutes { get; set; } = 60;

        // ---- Smart scheduling (see BackupPolicyService) ----

        /// <summary>Evict the local AI model from RAM before a scheduled backup starts.</summary>
        public bool EvictAiModelBeforeBackup { get; set; } = true;

        /// <summary>Restrict each service to its own allowed time-of-day window.</summary>
        public bool SyncWindowsEnabled { get; set; } = false;

        /// <summary>Pause scheduled backups while the link is busier than the threshold.</summary>
        public bool BandwidthGuardEnabled { get; set; } = false;

        /// <summary>Upload threshold in KB/s that counts as "busy" (e.g. while gaming or streaming).</summary>
        public int BandwidthThresholdKbps { get; set; } = 2000;

        /// <summary>How long a busy link must stay busy before backups are actually deferred.</summary>
        public int BandwidthGraceSeconds { get; set; } = 20;

        /// <summary>When true, a deferred schedule is retried on the next scheduler tick.</summary>
        public bool RetryDeferredBackups { get; set; } = true;

        /// <summary>Keep a copy of remote files before overwriting them so a sync can be undone.</summary>
        public bool EnableSyncRollback { get; set; } = true;

        /// <summary>How many rollback snapshots to keep per service before pruning.</summary>
        public int RollbackHistoryCount { get; set; } = 5;

        /// <summary>Master switch for the computer fleet heartbeat monitor.</summary>
        public bool FleetHeartbeatEnabled { get; set; } = true;

        /// <summary>Seconds a computer must stay offline before it counts as a real outage.</summary>
        public int FleetOfflineGraceSeconds { get; set; } = 120;
    }

    /// <summary>
    /// An allowed time-of-day window for one backup service. Minutes are counted from midnight,
    /// and a window whose end is earlier than its start wraps over midnight (e.g. 22:00-06:00).
    /// A null/empty window means "any time".
    /// </summary>
    public sealed class SyncWindowSettings
    {
        /// <summary>Minutes from midnight, inclusive.</summary>
        public int StartMinute { get; set; } = 0;

        /// <summary>Minutes from midnight, exclusive. Equal to StartMinute means "24 hours".</summary>
        public int EndMinute { get; set; } = 1440;

        public bool IsAllDay => StartMinute == 0 && EndMinute >= 1440;

        public bool Contains(DateTime localTime)
        {
            if (IsAllDay) return true;
            if (EndMinute >= 1440 && StartMinute == 0) return true;

            var minute = (localTime.Hour * 60) + localTime.Minute;

            if (EndMinute > StartMinute)
                return minute >= StartMinute && minute < EndMinute;

            // Wraps midnight, e.g. 22:00 -> 06:00
            return minute >= StartMinute || minute < EndMinute;
        }

        public string Describe()
        {
            if (IsAllDay) return "Any time";
            return $"{StartMinute / 60:D2}:{StartMinute % 60:D2} - {EndMinute / 60:D2}:{EndMinute % 60:D2}";
        }
    }

    public sealed class WindowsSettings
    {
        public SyncWindowSettings Ftp { get; set; } = new();
        public SyncWindowSettings Sql { get; set; } = new();
        public SyncWindowSettings Mailchimp { get; set; } = new();
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
        public string WebPin { get; set; } = "";
        public bool RequireAuth { get; set; } = false;
        public string CloudflareUrl { get; set; } = "";
        /// <summary>Automatically recreate the Cloudflare Quick Tunnel after crashes, network loss, or app restart.</summary>
        public bool AutoRestartTunnel { get; set; } = true;
    }
}
