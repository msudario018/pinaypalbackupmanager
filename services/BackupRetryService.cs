using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Tracks backup failures and schedules automatic retries with exponential backoff.
    /// Retry schedule: 5 min → 15 min → 30 min (max 3 retries)
    /// </summary>
    public static class BackupRetryService
    {
        private static readonly Dictionary<string, RetryEntry> _retryQueue = new();
        private static System.Timers.Timer? _checkTimer;
        private static bool _isInitialized = false;

        public static event Action<string>? OnRetryDue;

        private class RetryEntry
        {
            public string Service { get; set; } = "";
            public int AttemptCount { get; set; } = 0;
            public DateTime NextRetryTime { get; set; }
            public DateTime LastFailureTime { get; set; }
        }

        /// <summary>
        /// Starts the retry monitor (checks every 30 seconds).
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            _checkTimer = new System.Timers.Timer(30000); // Check every 30 seconds
            _checkTimer.Elapsed += async (_, _) => await CheckRetriesAsync();
            _checkTimer.AutoReset = true;
            _checkTimer.Start();

            LogService.WriteSystemLog("[RETRY] Auto-retry service initialized", "Information", "SYSTEM");
        }

        public static void Stop()
        {
            _checkTimer?.Stop();
            _checkTimer?.Dispose();
            _checkTimer = null;
            _retryQueue.Clear();
            _isInitialized = false;
            LogService.WriteSystemLog("[RETRY] Auto-retry service stopped", "Information", "SYSTEM");
        }

        /// <summary>
        /// Register a failure for a service, queuing it for retry.
        /// </summary>
        public static void RegisterFailure(string service)
        {
            lock (_retryQueue)
            {
                if (_retryQueue.TryGetValue(service, out var entry))
                {
                    // Already queued, update failure time
                    entry.LastFailureTime = DateTime.UtcNow;
                    LogService.WriteSystemLog($"[RETRY] {service} failed again (attempt {entry.AttemptCount}), keeping in queue", "Warning", "SYSTEM");
                }
                else
                {
                    // New failure
                    var delay = TimeSpan.FromMinutes(5); // First retry after 5 min
                    entry = new RetryEntry
                    {
                        Service = service,
                        AttemptCount = 0,
                        NextRetryTime = DateTime.UtcNow.Add(delay),
                        LastFailureTime = DateTime.UtcNow
                    };
                    _retryQueue[service] = entry;
                    LogService.WriteSystemLog($"[RETRY] {service} failed, scheduled retry in 5 min", "Warning", "SYSTEM");
                }
            }
        }

        /// <summary>
        /// Mark a service as successfully completed, removing it from retry queue.
        /// </summary>
        public static void MarkSuccess(string service)
        {
            lock (_retryQueue)
            {
                if (_retryQueue.Remove(service))
                {
                    LogService.WriteSystemLog($"[RETRY] {service} succeeded, removed from retry queue", "Information", "SYSTEM");
                }
            }
        }

        private static async Task CheckRetriesAsync()
        {
            List<RetryEntry> dueRetries;
            lock (_retryQueue)
            {
                dueRetries = _retryQueue.Values
                    .Where(r => r.NextRetryTime <= DateTime.UtcNow)
                    .ToList();
            }

            foreach (var retry in dueRetries)
            {
                retry.AttemptCount++;

                if (retry.AttemptCount > 3)
                {
                    // Max retries exceeded
                    lock (_retryQueue) _retryQueue.Remove(retry.Service);
                    LogService.WriteSystemLog($"[RETRY] {retry.Service} exceeded max retries (3), giving up", "Error", "SYSTEM");
                    NotificationService.ShowBackupToast("Retry Failed", $"{retry.Service} backup failed after 3 retry attempts.", "Error");
                    continue;
                }

                // Schedule next retry before firing, so concurrent handlers don't double-trigger
                var nextDelay = retry.AttemptCount switch
                {
                    1 => TimeSpan.FromMinutes(15), // 2nd attempt: 15 min
                    2 => TimeSpan.FromMinutes(30), // 3rd attempt: 30 min
                    _ => TimeSpan.FromHours(1)
                };
                retry.NextRetryTime = DateTime.UtcNow.Add(nextDelay);

                LogService.WriteSystemLog($"[RETRY] Triggering retry #{retry.AttemptCount} for {retry.Service}, next retry in {nextDelay.TotalMinutes:F0} min if fails", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Auto-Retry", $"Retrying {retry.Service} backup (attempt {retry.AttemptCount}/3)...", "Info");

                try
                {
                    OnRetryDue?.Invoke(retry.Service);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[RETRY] Error triggering retry for {retry.Service}: {ex.Message}", "Error", "SYSTEM");
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets current retry status for display in UI.
        /// </summary>
        public static List<(string Service, int Attempt, string NextRetry)> GetPendingRetries()
        {
            lock (_retryQueue)
            {
                return _retryQueue.Values
                    .Select(r => (r.Service, r.AttemptCount, r.NextRetryTime.ToString("HH:mm:ss")))
                    .ToList();
            }
        }

        public static bool HasPendingRetries => _retryQueue.Count > 0;
    }
}
