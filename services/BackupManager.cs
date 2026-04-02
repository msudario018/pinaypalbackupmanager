using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public class BackupManager
    {
        private readonly Timer _mainTimer;
        private int _healthRunning;

        private DateTime _lastFtpAutoReset;
        private DateTime _lastMailchimpAutoReset;
        private DateTime _lastSqlAutoReset;

        private DateTime _lastFtpDailyRunMnlDate;
        private DateTime _lastMailchimpDailyRunMnlDate;
        private DateTime _lastSqlDailyRunMnlDate;

        public event Action<List<BackupHealthReport>>? OnHealthUpdate;
        public event Action<DateTime, DateTime, DateTime, DateTime>? OnTimeUpdate;
        public event Action? OnFtpAutoSyncRequested;
        public event Action? OnMailchimpAutoSyncRequested;
        public event Action? OnSqlAutoSyncRequested;

        public static readonly TimeSpan FtpAutoScanInterval = TimeSpan.FromHours(3);
        public static readonly TimeSpan MailchimpAutoScanInterval = TimeSpan.FromHours(2);
        public static readonly TimeSpan SqlAutoScanInterval = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15);

        public DateTime NextFtpAutoScan => _lastFtpAutoReset.Add(FtpAutoScanInterval);
        public DateTime NextMailchimpAutoScan => _lastMailchimpAutoReset.Add(MailchimpAutoScanInterval);
        public DateTime NextSqlAutoScan => _lastSqlAutoReset.Add(SqlAutoScanInterval);

        public static DateTime NextFtpDailySyncMnl => GetNextDailyMnl(BackupConfig.FtpDailySyncHourMnl, BackupConfig.FtpDailySyncMinuteMnl);
        public static DateTime NextMailchimpDailySyncMnl => GetNextDailyMnl(BackupConfig.MailchimpDailySyncHourMnl, BackupConfig.MailchimpDailySyncMinuteMnl);
        public static DateTime NextSqlDailySyncMnl => GetNextDailyMnl(BackupConfig.SqlDailySyncHourMnl, BackupConfig.SqlDailySyncMinuteMnl);

        public BackupManager()
        {
            _mainTimer = new Timer(1000);
            _mainTimer.Elapsed += MainTimer_Elapsed;
            var now = GetTzDate();
            _lastFtpAutoReset = now;
            _lastMailchimpAutoReset = now;
            _lastSqlAutoReset = now;
            _lastFtpDailyRunMnlDate = DateTime.MinValue;
            _lastMailchimpDailyRunMnlDate = DateTime.MinValue;
            _lastSqlDailyRunMnlDate = DateTime.MinValue;
        }

        public void Start()
        {
            _mainTimer.Start();
            EnsureFoldersExist();
            // Initial run
            _ = RunHealthCheckAsync();
        }

        public void Stop()
        {
            _mainTimer.Stop();
        }

        private void MainTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            DateTime now = GetTzDate();
            DateTime mnlTime = now.AddHours(15); // UTC-7 to UTC+8 is +15 hours
            
            OnTimeUpdate?.Invoke(now, mnlTime, NextFtpAutoScan, NextFtpDailySyncMnl);

            if (System.Threading.Volatile.Read(ref _healthRunning) == 0)
            {
                if (now >= NextFtpAutoScan)
                {
                    _lastFtpAutoReset = now;
                    _ = RunHealthCheckAsync(true);
                }
                else if (now >= NextMailchimpAutoScan)
                {
                    _lastMailchimpAutoReset = now;
                    _ = RunHealthCheckAsync(true);
                }
                else if (now >= NextSqlAutoScan)
                {
                    _lastSqlAutoReset = now;
                    _ = RunHealthCheckAsync(true);
                }
            }

            if (mnlTime.Second == 0)
            {
                var todayMnl = mnlTime.Date;

                if (mnlTime.Hour == BackupConfig.FtpDailySyncHourMnl && mnlTime.Minute == BackupConfig.FtpDailySyncMinuteMnl && _lastFtpDailyRunMnlDate != todayMnl)
                {
                    _lastFtpDailyRunMnlDate = todayMnl;
                    LogService.WriteLiveLog("DAILY FTP SYNC TRIGGERED (MANILA 10 PM)", BackupConfig.FtpLogFile, "Information", "SYSTEM");
                    _ = RunHealthCheckAsync(true);
                }

                if (mnlTime.Hour == BackupConfig.MailchimpDailySyncHourMnl && mnlTime.Minute == BackupConfig.MailchimpDailySyncMinuteMnl && _lastMailchimpDailyRunMnlDate != todayMnl)
                {
                    _lastMailchimpDailyRunMnlDate = todayMnl;
                    LogService.WriteLiveLog("DAILY MAILCHIMP BACKUP TRIGGERED (MANILA 6 PM)", BackupConfig.McLogFile, "Information", "SYSTEM");
                    _ = RunHealthCheckAsync(true);
                }

                if (mnlTime.Hour == BackupConfig.SqlDailySyncHourMnl && mnlTime.Minute == BackupConfig.SqlDailySyncMinuteMnl && _lastSqlDailyRunMnlDate != todayMnl)
                {
                    _lastSqlDailyRunMnlDate = todayMnl;
                    LogService.WriteLiveLog("DAILY SQL BACKUP TRIGGERED (MANILA 5 PM)", BackupConfig.SqlLogFile, "Information", "SYSTEM");
                    _ = RunHealthCheckAsync(true);
                }
            }
        }

        public async Task RunHealthCheckAsync(bool triggerAutoSync = false)
        {
            if (System.Threading.Interlocked.Exchange(ref _healthRunning, 1) == 1) return;
            LogService.WriteLiveLog("HEALTH: Starting global health check...", BackupConfig.FtpLogFile, "Information", "SYSTEM");
            try
            {
                await Task.Run(async () =>
                {
                    var reports = new List<BackupHealthReport>();
                    var nowUtc = DateTime.UtcNow;
                    var freshWindowUtc = nowUtc.AddHours(-25);

                    LogService.WriteLiveLog($"HEALTH: Fresh window (UTC) set to {freshWindowUtc:MM/dd hh:mm:ss}.", BackupConfig.FtpLogFile, "Information", "SYSTEM");

                    // 1. Website Check
                    LogService.WriteLiveLog("HEALTH: Checking Website status...", BackupConfig.FtpLogFile, "Information", "SYSTEM");
                    reports.Add(await CheckWebsiteHealthAsync());

                    // 2. Mailchimp Check
                    LogService.WriteLiveLog("HEALTH: Checking Mailchimp status...", BackupConfig.McLogFile, "Information", "SYSTEM");
                    reports.Add(CheckMailchimpHealth(freshWindowUtc));

                    // 3. Database Check
                    LogService.WriteLiveLog("HEALTH: Checking Database status...", BackupConfig.SqlLogFile, "Information", "SYSTEM");
                    reports.Add(CheckDatabaseHealth(freshWindowUtc));

                    LogService.WriteLiveLog("HEALTH: Global health check completed.", BackupConfig.FtpLogFile, "Information", "SYSTEM");
                    OnHealthUpdate?.Invoke(reports);

                    if (triggerAutoSync)
                    {
                        // Check for OUTDATED status to trigger auto-sync
                        foreach (var report in reports)
                        {
                            if (report.Status == "OUTDATED" || report.Status == "INCOMPLETE")
                            {
                                string logFile = report.Service == "Website" ? BackupConfig.FtpLogFile :
                                                 report.Service == "Mailchimp" ? BackupConfig.McLogFile :
                                                 report.Service == "Database" ? BackupConfig.SqlLogFile : BackupConfig.FtpLogFile;

                                LogService.WriteLiveLog($"AUTO-SYNC: Detected {report.Service} is {report.Status}. Requesting auto-sync...", logFile, "Information", "SYSTEM");
                                if (report.Service == "Website") OnFtpAutoSyncRequested?.Invoke();
                                else if (report.Service == "Mailchimp") OnMailchimpAutoSyncRequested?.Invoke();
                                else if (report.Service == "Database") OnSqlAutoSyncRequested?.Invoke();
                            }
                        }
                    }
                });
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _healthRunning, 0);
            }
        }

        private static async Task<BackupHealthReport> CheckWebsiteHealthAsync()
        {
            var report = new BackupHealthReport { Service = "Website" };
            if (Directory.Exists(BackupConfig.FtpLocalFolder))
            {
                var latestLocal = new DirectoryInfo(BackupConfig.FtpLocalFolder)
                    .EnumerateFiles("*PinayPal*.tar*", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latestLocal != null)
                {
                    report.Status = "OK";
                    report.Color = "LimeGreen";
                    report.LastUpdate = latestLocal.LastWriteTime.ToString("MM/dd hh:mm:ss");
                    report.FileName = latestLocal.Name;

                    // --- WEBSITE: FTP COMPARISON ---
                    try
                    {
                        using var ftp = new FtpService();
                        string decryptedPass = SecurityService.GetDecryptedFtpPassword();
                        ftp.Initialize(BackupConfig.FtpHost, BackupConfig.FtpUser, decryptedPass, BackupConfig.FtpTlsFingerprint, BackupConfig.FtpPort);

                        if (await ftp.ConnectAsync())
                        {
                            var ftpLatest = ftp.ListFiles("/").ToList()
                                .Where(f => f.Name.Contains("PinayPal", StringComparison.OrdinalIgnoreCase) && f.Name.Contains(".tar", StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(f => f.LastWriteTime)
                                .FirstOrDefault();

                            if (ftpLatest != null)
                            {
                                var matchingLocal = new DirectoryInfo(BackupConfig.FtpLocalFolder)
                                    .EnumerateFiles("*", SearchOption.AllDirectories)
                                    .FirstOrDefault(f => string.Equals(f.Name, ftpLatest.Name, StringComparison.OrdinalIgnoreCase));

                                if (matchingLocal != null && matchingLocal.Length == ftpLatest.Length)
                                {
                                    report.LastUpdate = matchingLocal.LastWriteTime.ToString("MM/dd hh:mm:ss");
                                    report.FileName = matchingLocal.Name;
                                }
                                else
                                {
                                    report.NeedsSync = true;
                                    report.Status = "OUTDATED";
                                    report.Color = "Red";
                                }
                            }
                            else
                            {
                                report.Status = "FTP EMPTY";
                            }
                        }
                    }
                    catch { report.Status = "SCAN ERROR"; }
                }
                else
                {
                    report.Status = "EMPTY";
                    report.Color = "Red";
                    report.LastUpdate = "N/A";
                }
            }
            else
            {
                report.Status = "NOT SET";
                report.Color = "Gray";
                report.LastUpdate = "Path Missing";
            }
            return report;
        }

        private static BackupHealthReport CheckMailchimpHealth(DateTime freshWindowUtc)
        {
            var report = new BackupHealthReport { Service = "Mailchimp" };
            if (Directory.Exists(BackupConfig.MailchimpFolder))
            {
                var targets = new (string Label, string[] Keywords)[]
                {
                    ("audience", ["audience", "members"]),
                    ("campaigns", ["campaigns"]),
                    ("report", ["report", "reports"]),
                    ("merge_fields", ["merge_fields", "merge_fields", "merge-fields"]),
                    ("tags", ["tags"])
                };
                var missingItems = new List<string>();
                var infoList = new List<string>();

                var allFiles = new DirectoryInfo(BackupConfig.MailchimpFolder).GetFiles("*", SearchOption.AllDirectories).ToList();

                static DateTime GetFreshnessUtc(FileInfo file) =>
                    file.LastWriteTimeUtc > file.CreationTimeUtc ? file.LastWriteTimeUtc : file.CreationTimeUtc;

                foreach (var (label, keywords) in targets)
                {
                    var latest = allFiles
                        .Where(f => keywords.Any(k => f.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                        .OrderByDescending(GetFreshnessUtc)
                        .FirstOrDefault();

                    if (latest != null)
                    {
                        var freshnessUtc = GetFreshnessUtc(latest);
                        infoList.Add($"{label} ({freshnessUtc:HH:mm:ss})");
                        if (freshnessUtc < freshWindowUtc) missingItems.Add($"{label} (STALE)");
                    }
                    else
                    {
                        missingItems.Add(label);
                    }
                }

                report.LastUpdate = infoList.Count > 0 ? string.Join(" | ", infoList) : "No Files";
                report.Status = missingItems.Count == 0 ? "OK (5/5)" : "INCOMPLETE";
                report.Color = missingItems.Count == 0 ? "LimeGreen" : "Orange";
                report.Missing = string.Join(", ", missingItems);
            }
            else
            {
                report.Status = "NOT SET";
                report.Color = "Gray";
                report.LastUpdate = "Path Missing";
            }
            return report;
        }

        private static BackupHealthReport CheckDatabaseHealth(DateTime freshWindowUtc)
        {
            var report = new BackupHealthReport { Service = "Database" };
            if (Directory.Exists(BackupConfig.SqlLocalFolder))
            {
                try
                {
                    using var sql = new SqlService();
                    string decryptedPass = SecurityService.GetDecryptedSqlPassword();
                    sql.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, decryptedPass, BackupConfig.SqlTlsFingerprint);

                    var localLatest = new DirectoryInfo(BackupConfig.SqlLocalFolder)
                        .EnumerateFiles("*.sql*", SearchOption.AllDirectories)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();

                    if (localLatest == null)
                    {
                        report.Status = "EMPTY";
                        report.Color = "Red";
                        report.LastUpdate = "No local .sql backups";
                        report.NeedsSync = true;
                        return report;
                    }

                    report.FileName = localLatest.Name;
                    report.LastUpdate = $"Local latest: {localLatest.Name} ({localLatest.LastWriteTimeUtc:MM/dd HH:mm} UTC)";

                    if (!sql.ConnectAsync().GetAwaiter().GetResult())
                    {
                        report.Status = "CONNECTION FAILED";
                        report.Color = "Orange";
                        report.Missing = "Unable to connect to SQL FTP";
                        return report;
                    }

                    var remoteLatest = sql.ListFiles(BackupConfig.SqlRemotePath)
                        .Where(f => !f.IsDirectory)
                        .Where(f => f.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    if (remoteLatest == null)
                    {
                        report.Status = "REMOTE EMPTY";
                        report.Color = "Orange";
                        report.Missing = "No remote .sql backups";
                        return report;
                    }

                    string expectedLocalPath = Path.Combine(BackupConfig.SqlLocalFolder, remoteLatest.Name);
                    bool hasRemoteFileLocally = File.Exists(expectedLocalPath);
                    long remoteSize = remoteLatest.Length;
                    long localSize = hasRemoteFileLocally ? new FileInfo(expectedLocalPath).Length : -1;

                    if (hasRemoteFileLocally && localSize == remoteSize)
                    {
                        report.Status = "OK";
                        report.Color = "LimeGreen";
                        report.LastUpdate = $"Local has latest remote: {remoteLatest.Name} ({remoteLatest.LastWriteTime:MM/dd HH:mm} UTC)";
                        return report;
                    }

                    if (remoteLatest.LastWriteTime <= localLatest.LastWriteTimeUtc.AddMinutes(1))
                    {
                        report.Status = "OK";
                        report.Color = "LimeGreen";
                        return report;
                    }

                    if (hasRemoteFileLocally && localSize != remoteSize)
                    {
                        report.Status = "SIZE MISMATCH";
                        report.Color = "Orange";
                        report.Missing = $"Remote: {remoteLatest.Name} ({remoteSize:n0} bytes) | Local: {localSize:n0} bytes";
                        report.NeedsSync = true;
                        return report;
                    }

                    report.Status = "OUTDATED";
                    report.Color = "Red";
                    report.NeedsSync = true;
                    report.LastUpdate = $"Remote latest: {remoteLatest.Name} ({remoteLatest.LastWriteTime:MM/dd HH:mm} UTC) | Local latest: {localLatest.Name} ({localLatest.LastWriteTimeUtc:MM/dd HH:mm} UTC)";
                }
                catch (Exception ex)
                {
                    report.Status = "SCAN ERROR";
                    report.Color = "Orange";
                    report.Missing = ex.Message;
                }
            }
            else
            {
                report.Status = "NOT SET";
                report.Color = "Gray";
                report.LastUpdate = "Path Missing";
            }
            return report;
        }

        public void ResetHealthTimer()
        {
            var now = GetTzDate();
            _lastFtpAutoReset = now;
            _lastMailchimpAutoReset = now;
            _lastSqlAutoReset = now;
        }

        public void ResetFtpTimer()
        {
            _lastFtpAutoReset = GetTzDate();
        }

        public void ResetMailchimpTimer()
        {
            _lastMailchimpAutoReset = GetTzDate();
        }

        public void ResetSqlTimer()
        {
            _lastSqlAutoReset = GetTzDate();
        }

        public static DateTime GetTzDate()
        {
            return DateTime.UtcNow.AddHours(-7);
        }

        public static DateTime GetManilaDate()
        {
            return DateTime.UtcNow.AddHours(8);
        }

        private static DateTime GetNextDailyMnl(int hour, int minute)
        {
            var mnl = GetManilaDate();
            var next = new DateTime(mnl.Year, mnl.Month, mnl.Day, hour, minute, 0);
            if (next <= mnl) next = next.AddDays(1);
            return next;
        }

        private static void EnsureFoldersExist()
        {
            string[] targets = [BackupConfig.FtpLocalFolder, BackupConfig.MailchimpFolder, BackupConfig.SqlLocalFolder];
            foreach (var folder in targets)
            {
                if (!string.IsNullOrEmpty(folder))
                {
                    if (!Directory.Exists(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }
                    string logPath = Path.Combine(folder, "backup_log.txt");
                    if (!File.Exists(logPath))
                    {
                        File.WriteAllText(logPath, "--- Log Initialized ---" + Environment.NewLine);
                    }
                }
            }
        }

        public static void CleanupOldBackups(string backupPath, string logLabel, int days = 7)
        {
            if (Directory.Exists(backupPath))
            {
                var limitDate = DateTime.Now.AddDays(-days);
                var logFile = Path.Combine(backupPath, "backuplog.txt");

                var oldFiles = new DirectoryInfo(backupPath).GetFiles()
                    .Where(f => f.LastWriteTime < limitDate && f.Name != "backuplog.txt" && f.Name != "backup_log.txt")
                    .ToList();

                if (oldFiles.Count > 0)
                {
                    foreach (var file in oldFiles)
                    {
                        string msg = $"CLEANUP [{logLabel}]: Deleted {days}-day old file: {file.Name}";
                        try
                        {
                            file.Delete();
                            LogService.WriteLiveLog(msg, logFile, "Information", "SYSTEM");
                        }
                        catch (Exception ex)
                        {
                            LogService.WriteLiveLog($"CLEANUP ERROR [{logLabel}]: {ex.Message}", logFile, "Error", "SYSTEM");
                        }
                    }
                }
            }
        }
    }
}
