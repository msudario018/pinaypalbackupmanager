using System;
using System.Threading;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public static class SessionTimeoutService
    {
        private static Timer? _inactivityTimer;
        private static DateTime _lastActivityTime = DateTime.UtcNow;
        private static readonly object _lock = new object();
        private static bool _isRunning = false;
        
        // Default timeout: 30 minutes
        private static int _timeoutMinutes = 30;
        
        public static int TimeoutMinutes
        {
            get => _timeoutMinutes;
            set => _timeoutMinutes = Math.Max(1, value); // Minimum 1 minute
        }
        
        public static event Action? OnSessionTimeout;
        
        public static void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                
                _isRunning = true;
                _lastActivityTime = DateTime.UtcNow;
                
                // Check inactivity every minute
                _inactivityTimer = new Timer(CheckInactivity, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
        }
        
        public static void Stop()
        {
            lock (_lock)
            {
                _isRunning = false;
                _inactivityTimer?.Dispose();
                _inactivityTimer = null;
            }
        }
        
        public static void ResetActivity()
        {
            lock (_lock)
            {
                _lastActivityTime = DateTime.UtcNow;
            }
        }
        
        private static void CheckInactivity(object? state)
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                
                var inactiveDuration = DateTime.UtcNow - _lastActivityTime;
                
                if (inactiveDuration.TotalMinutes >= _timeoutMinutes)
                {
                    // Session timeout - trigger logout
                    Stop();
                    OnSessionTimeout?.Invoke();
                }
            }
        }
        
        public static TimeSpan GetRemainingTime()
        {
            lock (_lock)
            {
                if (!_isRunning) return TimeSpan.Zero;
                
                var elapsed = DateTime.UtcNow - _lastActivityTime;
                var remaining = TimeSpan.FromMinutes(_timeoutMinutes) - elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }
}
