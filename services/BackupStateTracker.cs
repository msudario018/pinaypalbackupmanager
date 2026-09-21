using System;
using System.Collections.Generic;
using System.Linq;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// A snapshot of one currently running backup. This is the contract served to
    /// the desktop web dashboard and mobile clients.
    /// </summary>
    public class ActiveBackupInfo
    {
        public bool IsBusy { get; set; }
        public string Service { get; set; } = "none";
        public string StatusText { get; set; } = "Idle";
        public int Progress { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public IReadOnlyList<ActiveBackupServiceInfo> ActiveServices { get; set; } = Array.Empty<ActiveBackupServiceInfo>();
    }

    public class ActiveBackupServiceInfo
    {
        public string Service { get; set; } = "none";
        public string StatusText { get; set; } = "Running";
        public int Progress { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
    }

    /// <summary>
    /// Shared, thread-safe source of truth for all backup surfaces. A backup is
    /// removed only by the service that started it, so one completed job cannot
    /// incorrectly clear a concurrently running job.
    /// </summary>
    public static class BackupStateTracker
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, ActiveBackupServiceInfo> _active = new(StringComparer.OrdinalIgnoreCase);

        public static ActiveBackupInfo CurrentState
        {
            get
            {
                lock (_lock)
                {
                    var activeServices = _active.Values
                        .OrderByDescending(item => item.LastUpdatedAt)
                        .Select(Clone)
                        .ToList();
                    var primary = activeServices.FirstOrDefault();

                    return primary == null
                        ? new ActiveBackupInfo()
                        : new ActiveBackupInfo
                        {
                            IsBusy = true,
                            Service = primary.Service,
                            StatusText = primary.StatusText,
                            Progress = primary.Progress,
                            StartedAt = primary.StartedAt,
                            LastUpdatedAt = primary.LastUpdatedAt,
                            ActiveServices = activeServices
                        };
                }
            }
        }

        /// <summary>Atomically reserves the backup surface for a remote trigger.</summary>
        public static bool TrySetRunning(string service, string status = "Running", int progress = 0)
        {
            lock (_lock)
            {
                if (_active.Count > 0)
                {
                    return false;
                }

                SetRunningUnsafe(service, status, progress);
                return true;
            }
        }

        public static void SetRunning(string service, string status = "Running", int progress = 0)
        {
            lock (_lock)
            {
                SetRunningUnsafe(service, status, progress);
            }
        }

        public static void UpdateProgress(string service, int progress, string? status = null)
        {
            lock (_lock)
            {
                if (!_active.TryGetValue(service, out var operation))
                {
                    return;
                }

                operation.Progress = Math.Clamp(progress, 0, 100);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    operation.StatusText = status;
                }
                operation.LastUpdatedAt = DateTime.UtcNow;
            }
        }

        // Retained for callers that only have one active backup.
        public static void UpdateProgress(int progress, string? status = null)
        {
            lock (_lock)
            {
                var operation = _active.Values.OrderByDescending(item => item.LastUpdatedAt).FirstOrDefault();
                if (operation == null)
                {
                    return;
                }

                operation.Progress = Math.Clamp(progress, 0, 100);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    operation.StatusText = status;
                }
                operation.LastUpdatedAt = DateTime.UtcNow;
            }
        }

        public static void SetIdle(string service = "none", string status = "Idle")
        {
            lock (_lock)
            {
                if (string.Equals(service, "none", StringComparison.OrdinalIgnoreCase))
                {
                    _active.Clear();
                    return;
                }

                _active.Remove(service);
            }
        }

        private static void SetRunningUnsafe(string service, string status, int progress)
        {
            var now = DateTime.UtcNow;
            if (_active.TryGetValue(service, out var existing))
            {
                existing.StatusText = status;
                existing.Progress = Math.Clamp(progress, 0, 100);
                existing.LastUpdatedAt = now;
                return;
            }

            _active[service] = new ActiveBackupServiceInfo
            {
                Service = service,
                StatusText = status,
                Progress = Math.Clamp(progress, 0, 100),
                StartedAt = now,
                LastUpdatedAt = now
            };
        }

        private static ActiveBackupServiceInfo Clone(ActiveBackupServiceInfo source) => new()
        {
            Service = source.Service,
            StatusText = source.StatusText,
            Progress = source.Progress,
            StartedAt = source.StartedAt,
            LastUpdatedAt = source.LastUpdatedAt
        };
    }
}
