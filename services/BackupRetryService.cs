using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private static int _isCheckingRetries = 0;

        private static ElapsedEventHandler? _checkTimerHandler;

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
            
            // Properly unsubscribe and dispose old timer
            if (_checkTimer != null)
            {
                _checkTimer.Elapsed -= _checkTimerHandler;
                _checkTimer.Stop();
                _checkTimer.Dispose();
            }

            _checkTimerHandler = async (_, _) => await CheckRetriesAsync();
            _checkTimer = new System.Timers.Timer(30000); // Check every 30 seconds
            _checkTimer.Elapsed += _checkTimerHandler;
            _checkTimer.AutoReset = true;
            _checkTimer.Start();

            LogService.WriteSystemLog("[RETRY] Auto-retry service initialized", "Information", "SYSTEM");
        }

        public static void Stop()
        {
            if (_checkTimer != null)
            {
                _checkTimer.Elapsed -= _checkTimerHandler;
                _checkTimer.Stop();
                _checkTimer.Dispose();
                _checkTimer = null;
            }
            _retryQueue.Clear();
            _isInitialized = false;
            Interlocked.Exchange(ref _isCheckingRetries, 0);
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
                    // Already queued — reschedule from now so the retry timer is not stale
                    var delay = entry.AttemptCount switch
                    {
                        0 => TimeSpan.FromMinutes(5),
                        1 => TimeSpan.FromMinutes(15),
                        2 => TimeSpan.FromMinutes(30),
                        _ => TimeSpan.FromHours(1)
                    };
                    entry.LastFailureTime = DateTime.UtcNow;
                    entry.NextRetryTime = DateTime.UtcNow.Add(delay);
                    LogService.WriteSystemLog($"[RETRY] {service} failed again (attempt {entry.AttemptCount}), rescheduled retry in {delay.TotalMinutes:F0} min", "Warning", "SYSTEM");
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
        /// Reschedules a retry without incrementing the attempt count (used when a retry was skipped, e.g., control busy).
        /// </summary>
        public static void Reschedule(string service, TimeSpan delay)
        {
            lock (_retryQueue)
            {
                if (_retryQueue.TryGetValue(service, out var entry))
                {
                    entry.NextRetryTime = DateTime.UtcNow.Add(delay);
                    LogService.WriteSystemLog($"[RETRY] {service} retry skipped, rescheduled in {delay.TotalMinutes:F0} min", "Information", "SYSTEM");
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
            if (Interlocked.Exchange(ref _isCheckingRetries, 1) == 1)
                return;

            try
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
                    lock (_retryQueue)
                    {
                        // Entry may have been removed by MarkSuccess while we were iterating
                        if (!_retryQueue.ContainsKey(retry.Service))
                            continue;

                        retry.AttemptCount++;

                        if (retry.AttemptCount > 3)
                        {
                            // Max retries exceeded
                            _retryQueue.Remove(retry.Service);
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
                    }

                    LogService.WriteSystemLog($"[RETRY] Triggering retry #{retry.AttemptCount} for {retry.Service}, next retry in {retry.NextRetryTime:HH:mm:ss} if fails", "Information", "SYSTEM");
                    NotificationService.ShowBackupToast("Auto-Retry", $"Retrying {retry.Service} backup (attempt {retry.AttemptCount}/3)...", "Info");

                    try
                    {
                        OnRetryDue?.Invoke(retry.Service);
                    }
                    catch (Exception ex)
                    {
                        LogService.WriteSystemLog($"[RETRY] Error triggering retry for {retry.Service}: {ex.Message}", "Error", "SYSTEM");
                    }
                    await Task.Delay(100); // Small delay between retries
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isCheckingRetries, 0);
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
                    .Select(r => (r.Service, r.AttemptCount, r.NextRetryTime.ToLocalTime().ToString("HH:mm:ss")))
                    .ToList();
            }
        }

        public static bool HasPendingRetries => _retryQueue.Count > 0;
    }
}
