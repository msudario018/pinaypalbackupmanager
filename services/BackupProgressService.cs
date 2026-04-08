using System;
using System.Threading.Tasks;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.Services
{
    public static class BackupProgressService
    {
        private static string? _currentCommandId;
        private static string? _currentService;
        private static DateTime _startTime;

        public static void StartBackup(string commandId, string service)
        {
            _currentCommandId = commandId;
            _currentService = service;
            _startTime = DateTime.UtcNow;
            
            LogService.WriteSystemLog($"[BACKUP_PROGRESS] Started tracking: {service} ({commandId})", "Information", "SYSTEM");
        }

        public static void EndBackup()
        {
            _currentCommandId = null;
            _currentService = null;
            LogService.WriteSystemLog("[BACKUP_PROGRESS] Ended tracking", "Information", "SYSTEM");
        }

        public static async Task UpdateProgressAsync(int progress, string message, string currentFile = "", string transferSpeed = "", string eta = "")
        {
            if (_currentCommandId == null || _currentService == null) return;

            try
            {
                await RealtimeMonitoringService.UpdateCommandStatusAsync(
                    _currentCommandId, 
                    _currentService, 
                    "running", 
                    progress, 
                    message, 
                    currentFile, 
                    transferSpeed, 
                    eta
                );

                // Add log entry for significant milestones
                if (progress % 25 == 0) // Log every 25%
                {
                    await RealtimeMonitoringService.AddLogAsync("Info", 
                        $"{_currentService} backup progress: {progress}% - {message}", 
                        "BACKUP");
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[BACKUP_PROGRESS] Failed to update progress: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static async Task CompleteBackupAsync(bool success, string message = "")
        {
            if (_currentCommandId == null || _currentService == null) return;

            try
            {
                var status = success ? "completed" : "failed";
                var finalProgress = success ? 100 : 0;
                var finalMessage = success ? "Backup completed successfully" : message;

                await RealtimeMonitoringService.UpdateCommandStatusAsync(
                    _currentCommandId, 
                    _currentService, 
                    status, 
                    finalProgress, 
                    finalMessage, 
                    "", 
                    "", 
                    ""
                );

                await RealtimeMonitoringService.AddLogAsync(
                    success ? "Info" : "Error", 
                    $"{_currentService} backup {status}: {finalMessage}", 
                    "BACKUP"
                );

                // Calculate total time
                var duration = DateTime.UtcNow - _startTime;
                await RealtimeMonitoringService.AddActivityAsync(
                    success ? "backup_completed" : "backup_failed",
                    _currentService,
                    $"{_currentService} backup {status} in {duration.TotalMinutes:F1} minutes"
                );

                LogService.WriteSystemLog($"[BACKUP_PROGRESS] Backup completed: {success} in {duration.TotalMinutes:F1} min", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[BACKUP_PROGRESS] Failed to complete backup: {ex.Message}", "Error", "SYSTEM");
            }
            finally
            {
                EndBackup();
            }
        }

        public static async Task AddBackupLogAsync(string severity, string message)
        {
            if (_currentService == null) return;

            try
            {
                await RealtimeMonitoringService.AddLogAsync(severity, message, _currentService.ToUpper());
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[BACKUP_PROGRESS] Failed to add log: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static bool IsTracking => _currentCommandId != null;
        public static string? CurrentService => _currentService;
        public static TimeSpan ElapsedTime => DateTime.UtcNow - _startTime;
    }
}
