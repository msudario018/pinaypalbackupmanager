using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Scheduled service that purges local backup files older than the configured retention period.
    /// Runs independently so cleanup happens even when no backup is in progress.
    /// </summary>
    public static class BackupRetentionService
    {
        private static System.Timers.Timer? _cleanupTimer;
        private static bool _isInitialized = false;
        private static int _isCleaningUp = 0;

        private static ElapsedEventHandler? _cleanupTimerHandler;

        /// <summary>
        /// Starts the retention cleanup timer (every 6 hours).
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            
            // Properly unsubscribe and dispose old timer
            if (_cleanupTimer != null)
            {
                _cleanupTimer.Elapsed -= _cleanupTimerHandler;
                _cleanupTimer.Stop();
                _cleanupTimer.Dispose();
            }

            _cleanupTimerHandler = async (_, _) => await RunCleanupAsync();
            _cleanupTimer = new System.Timers.Timer(TimeSpan.FromHours(6).TotalMilliseconds);
            _cleanupTimer.Elapsed += _cleanupTimerHandler;
            _cleanupTimer.AutoReset = true;
            _cleanupTimer.Start();

            LogService.WriteSystemLog("[RETENTION] Auto-cleanup service initialized (interval: 6h)", "Information", "SYSTEM");

            // Run initial cleanup on startup
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunCleanupAsync();
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[RETENTION] Initial cleanup failed: {ex.Message}", "Error", "SYSTEM");
                }
            });
        }

        public static void Stop()
        {
            if (_cleanupTimer != null)
            {
                _cleanupTimer.Elapsed -= _cleanupTimerHandler;
                _cleanupTimer.Stop();
                _cleanupTimer.Dispose();
                _cleanupTimer = null;
            }
            _isInitialized = false;
            Interlocked.Exchange(ref _isCleaningUp, 0);
            LogService.WriteSystemLog("[RETENTION] Auto-cleanup service stopped", "Information", "SYSTEM");
        }

        /// <summary>
        /// Runs a single cleanup pass across all configured backup folders.
        /// </summary>
        public static async Task RunCleanupAsync()
        {
            if (Interlocked.Exchange(ref _isCleaningUp, 1) == 1)
                return;

            try
            {
                ConfigService.Load();
                var retentionDays = ConfigService.Current.Operation.RetentionDays;
                var limitDate = BackupManager.GetTzDate().AddDays(-retentionDays);

                int totalDeleted = 0;
                long totalBytesFreed = 0;

                totalDeleted += CleanupFolder(BackupConfig.FtpLocalFolder, limitDate, "FTP", ref totalBytesFreed);
                totalDeleted += CleanupFolder(BackupConfig.MailchimpFolder, limitDate, "Mailchimp", ref totalBytesFreed);
                totalDeleted += CleanupFolder(BackupConfig.SqlLocalFolder, limitDate, "SQL", ref totalBytesFreed);

                // Also clean up old checksum records
                await CleanupOldChecksumsAsync(limitDate);

                if (totalDeleted > 0)
                {
                    var mbFreed = totalBytesFreed / (1024.0 * 1024.0);
                    LogService.WriteSystemLog($"[RETENTION] Purged {totalDeleted} old backup(s), freed {mbFreed:F1} MB", "Information", "SYSTEM");
                }
                else
                {
                    LogService.WriteSystemLog("[RETENTION] No old backups found to purge", "Information", "SYSTEM");
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[RETENTION] Cleanup failed: {ex.Message}", "Error", "SYSTEM");
            }
            finally
            {
                Interlocked.Exchange(ref _isCleaningUp, 0);
            }

            await Task.CompletedTask;
        }

        private static int CleanupFolder(string folderPath, DateTime limitDate, string label, ref long bytesFreed)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return 0;

            int deleted = 0;
            try
            {
                var files = new DirectoryInfo(folderPath)
                    .GetFiles("*", SearchOption.AllDirectories)
                    .Where(f => f.LastWriteTime < limitDate
                             && !f.Name.Equals("backuplog.txt", StringComparison.OrdinalIgnoreCase)
                             && !f.Name.Equals("backup_log.txt", StringComparison.OrdinalIgnoreCase)
                             && !f.Name.EndsWith(".checksums.json", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        bytesFreed += file.Length;
                        file.Delete();
                        deleted++;
                    }
                    catch { /* ignore locked files */ }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[RETENTION] Error cleaning {label}: {ex.Message}", "Error", "SYSTEM");
            }

            return deleted;
        }

        /// <summary>
        /// Cleans up checksum records older than the retention limit.
        /// </summary>
        private static async Task CleanupOldChecksumsAsync(DateTime limitDate)
        {
            try
            {
                await ChecksumService.CleanupOldChecksumsAsync((int)(DateTime.Now - limitDate).TotalDays);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[RETENTION] Failed to cleanup old checksums: {ex.Message}", "Warning", "SYSTEM");
            }
        }
    }
}
