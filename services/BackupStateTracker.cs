using System;

namespace PinayPalBackupManager.Services
{
    public class ActiveBackupInfo
    {
        public bool IsBusy { get; set; } = false;
        public string Service { get; set; } = "none";
        public string StatusText { get; set; } = "Idle";
        public int Progress { get; set; } = 0;
        public DateTime? StartedAt { get; set; }
    }

    public static class BackupStateTracker
    {
        private static readonly object _lock = new();
        private static readonly ActiveBackupInfo _state = new();

        public static ActiveBackupInfo CurrentState
        {
            get
            {
                lock (_lock)
                {
                    return new ActiveBackupInfo
                    {
                        IsBusy = _state.IsBusy,
                        Service = _state.Service,
                        StatusText = _state.StatusText,
                        Progress = _state.Progress,
                        StartedAt = _state.StartedAt
                    };
                }
            }
        }

        public static void SetRunning(string service, string status = "Running", int progress = 0)
        {
            lock (_lock)
            {
                _state.IsBusy = true;
                _state.Service = service;
                _state.StatusText = status;
                _state.Progress = progress;
                _state.StartedAt = DateTime.UtcNow;
            }
        }

        public static void UpdateProgress(int progress, string? status = null)
        {
            lock (_lock)
            {
                _state.Progress = progress;
                if (!string.IsNullOrEmpty(status))
                {
                    _state.StatusText = status;
                }
            }
        }

        public static void SetIdle(string service = "none", string status = "Idle")
        {
            lock (_lock)
            {
                _state.IsBusy = false;
                _state.Service = service;
                _state.StatusText = status;
                _state.Progress = 0;
                _state.StartedAt = null;
            }
        }
    }
}
