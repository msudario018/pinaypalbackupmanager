using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public class SyncStatusInfo
    {
        public string Service { get; set; } = "";
        public string Status { get; set; } = "UNKNOWN"; // LATEST, OUTDATED, CHECKING, ERROR
        public string Detail { get; set; } = "Not checked yet";
        public bool IsOutdated { get; set; } = false;
        public DateTime? LastCheckedUtc { get; set; }
        public string? RemoteLatestFile { get; set; }
        public DateTime? RemoteLatestTimeUtc { get; set; }
        public string? LocalLatestFile { get; set; }
        public DateTime? LocalLatestTimeUtc { get; set; }
    }

    public static class SyncStatusService
    {
        private static readonly ConcurrentDictionary<string, SyncStatusInfo> _statuses = new(StringComparer.OrdinalIgnoreCase);

        static SyncStatusService()
        {
            _statuses["FTP"] = new SyncStatusInfo { Service = "FTP" };
            _statuses["SQL"] = new SyncStatusInfo { Service = "SQL" };
            _statuses["Mailchimp"] = new SyncStatusInfo { Service = "Mailchimp" };
        }

        public static void UpdateStatus(
            string service,
            string status,
            string detail,
            bool isOutdated,
            string? remoteFile = null,
            DateTime? remoteTimeUtc = null,
            string? localFile = null,
            DateTime? localTimeUtc = null)
        {
            var key = service.ToUpperInvariant();
            var info = _statuses.GetOrAdd(key, k => new SyncStatusInfo { Service = service });
            lock (info)
            {
                info.Status = status;
                info.Detail = detail;
                info.IsOutdated = isOutdated;
                info.LastCheckedUtc = DateTime.UtcNow;
                if (remoteFile != null) info.RemoteLatestFile = remoteFile;
                if (remoteTimeUtc.HasValue) info.RemoteLatestTimeUtc = remoteTimeUtc.Value;
                if (localFile != null) info.LocalLatestFile = localFile;
                if (localTimeUtc.HasValue) info.LocalLatestTimeUtc = localTimeUtc.Value;
            }
        }

        public static SyncStatusInfo GetStatus(string service)
        {
            var key = service.ToUpperInvariant();
            if (_statuses.TryGetValue(key, out var info))
            {
                return info;
            }
            return new SyncStatusInfo { Service = service };
        }

        public static bool IsServiceOutdated(string service)
        {
            var s = GetStatus(service);
            return s.IsOutdated || string.Equals(s.Status, "OUTDATED", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Runs a fast headless remote vs local sync verification for FTP without UI thread.
        /// </summary>
        public static async Task<SyncStatusInfo> CheckFtpSyncAsync()
        {
            var info = GetStatus("FTP");
            info.Status = "CHECKING";
            info.LastCheckedUtc = DateTime.UtcNow;

            try
            {
                using var ftp = new FtpService();
                string decryptedPass = SecurityService.GetDecryptedFtpPassword();
                ftp.Initialize(BackupConfig.FtpHost, BackupConfig.FtpUser, decryptedPass, BackupConfig.FtpTlsFingerprint, BackupConfig.FtpPort);

                if (!await ftp.ConnectAsync())
                {
                    UpdateStatus("FTP", "CONNECTION_FAILED", "Unable to connect to FTP server", isOutdated: true);
                    return GetStatus("FTP");
                }

                var remoteLatest = ftp.ListFiles("/")
                    .Where(f => !f.IsDirectory && f.Name.Contains("PinayPal", StringComparison.OrdinalIgnoreCase) && f.Name.Contains(".tar", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();

                FileInfo? localLatest = null;
                if (Directory.Exists(BackupConfig.FtpLocalFolder))
                {
                    localLatest = new DirectoryInfo(BackupConfig.FtpLocalFolder)
                        .EnumerateFiles("*PinayPal*.tar*", SearchOption.AllDirectories)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();
                }

                if (remoteLatest == null)
                {
                    UpdateStatus("FTP", "EMPTY", "No PinayPal backup on remote server", isOutdated: false);
                    return GetStatus("FTP");
                }

                if (localLatest == null)
                {
                    UpdateStatus("FTP", "OUTDATED", $"Remote has {remoteLatest.Name}, but no local backup exists", isOutdated: true, remoteLatest.Name, remoteLatest.LastWriteTime);
                    return GetStatus("FTP");
                }

                var matchingLocal = new DirectoryInfo(BackupConfig.FtpLocalFolder)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .FirstOrDefault(f => string.Equals(f.Name, remoteLatest.Name, StringComparison.OrdinalIgnoreCase));

                bool hasRemoteLocally = matchingLocal != null && matchingLocal.Length == remoteLatest.Length;
                var fileTimeUtc = matchingLocal?.LastWriteTimeUtc ?? localLatest.LastWriteTimeUtc;
                var age = DateTime.UtcNow - fileTimeUtc;

                if (!hasRemoteLocally || age.TotalHours > 24)
                {
                    var reason = !hasRemoteLocally ? $"Remote has newer file: {remoteLatest.Name}" : $"Local backup is {(int)age.TotalHours}h old";
                    UpdateStatus("FTP", "OUTDATED", reason, isOutdated: true, remoteLatest.Name, remoteLatest.LastWriteTime, localLatest.Name, localLatest.LastWriteTimeUtc);
                }
                else
                {
                    UpdateStatus("FTP", "LATEST", $"Local matches remote: {remoteLatest.Name}", isOutdated: false, remoteLatest.Name, remoteLatest.LastWriteTime, localLatest.Name, localLatest.LastWriteTimeUtc);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("FTP", "ERROR", $"Sync check failed: {ex.Message}", isOutdated: true);
            }

            return GetStatus("FTP");
        }

        /// <summary>
        /// Runs a fast headless remote vs local sync verification for SQL without UI thread.
        /// </summary>
        public static async Task<SyncStatusInfo> CheckSqlSyncAsync()
        {
            var info = GetStatus("SQL");
            info.Status = "CHECKING";
            info.LastCheckedUtc = DateTime.UtcNow;

            try
            {
                using var sql = new SqlService();
                string decryptedPass = SecurityService.GetDecryptedSqlPassword();
                sql.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, decryptedPass, BackupConfig.SqlTlsFingerprint);

                if (!await sql.ConnectAsync())
                {
                    UpdateStatus("SQL", "CONNECTION_FAILED", "Unable to connect to SQL server", isOutdated: true);
                    return GetStatus("SQL");
                }

                var remoteFiles = sql.ListFiles(BackupConfig.SqlRemotePath);
                var remoteLatest = remoteFiles
                    .Where(f => !f.IsDirectory && (f.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();

                FileInfo? localLatest = null;
                if (Directory.Exists(BackupConfig.SqlLocalFolder))
                {
                    localLatest = new DirectoryInfo(BackupConfig.SqlLocalFolder)
                        .EnumerateFiles("*.sql*", SearchOption.AllDirectories)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();
                }

                if (remoteLatest == null)
                {
                    UpdateStatus("SQL", "EMPTY", "No SQL backup on remote server", isOutdated: false);
                    return GetStatus("SQL");
                }

                if (localLatest == null)
                {
                    UpdateStatus("SQL", "OUTDATED", $"Remote has {remoteLatest.Name}, but no local backup exists", isOutdated: true, remoteLatest.Name, remoteLatest.LastWriteTime);
                    return GetStatus("SQL");
                }

                string expectedLocalPath = Path.Combine(BackupConfig.SqlLocalFolder, remoteLatest.Name);
                bool hasRemoteLocally = File.Exists(expectedLocalPath) && new FileInfo(expectedLocalPath).Length == remoteLatest.Length;
                var fileTimeUtc = localLatest.LastWriteTimeUtc;
                var age = DateTime.UtcNow - fileTimeUtc;

                if (!hasRemoteLocally || age.TotalHours > 24)
                {
                    var reason = !hasRemoteLocally ? $"Remote has newer SQL file: {remoteLatest.Name}" : $"Local SQL backup is {(int)age.TotalHours}h old";
                    UpdateStatus("SQL", "OUTDATED", reason, isOutdated: true, remoteLatest.Name, remoteLatest.LastWriteTime, localLatest.Name, localLatest.LastWriteTimeUtc);
                }
                else
                {
                    UpdateStatus("SQL", "LATEST", $"Local matches remote: {remoteLatest.Name}", isOutdated: false, remoteLatest.Name, remoteLatest.LastWriteTime, localLatest.Name, localLatest.LastWriteTimeUtc);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("SQL", "ERROR", $"SQL sync check failed: {ex.Message}", isOutdated: true);
            }

            return GetStatus("SQL");
        }
    }
}
