using System;
using System.Threading;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class ThrottleService
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static DateTime _lastUpdate = DateTime.MinValue;
        private static readonly TimeSpan _defaultThrottleInterval = TimeSpan.FromMilliseconds(500);
        
        public static async Task ThrottleAsync(Action action, TimeSpan? interval = null)
        {
            var throttleInterval = interval ?? _defaultThrottleInterval;
            
            await _semaphore.WaitAsync();
            try
            {
                var timeSinceLastUpdate = DateTime.UtcNow - _lastUpdate;
                
                if (timeSinceLastUpdate >= throttleInterval)
                {
                    _lastUpdate = DateTime.UtcNow;
                    action();
                }
                else
                {
                    var delay = throttleInterval - timeSinceLastUpdate;
                    await Task.Delay(delay);
                    
                    // Double-check after delay
                    timeSinceLastUpdate = DateTime.UtcNow - _lastUpdate;
                    if (timeSinceLastUpdate >= throttleInterval)
                    {
                        _lastUpdate = DateTime.UtcNow;
                        action();
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
        
        public static async Task ThrottleAsync(Func<Task> action, TimeSpan? interval = null)
        {
            var throttleInterval = interval ?? _defaultThrottleInterval;
            
            await _semaphore.WaitAsync();
            try
            {
                var timeSinceLastUpdate = DateTime.UtcNow - _lastUpdate;
                
                if (timeSinceLastUpdate >= throttleInterval)
                {
                    _lastUpdate = DateTime.UtcNow;
                    await action();
                }
                else
                {
                    var delay = throttleInterval - timeSinceLastUpdate;
                    await Task.Delay(delay);
                    
                    // Double-check after delay
                    timeSinceLastUpdate = DateTime.UtcNow - _lastUpdate;
                    if (timeSinceLastUpdate >= throttleInterval)
                    {
                        _lastUpdate = DateTime.UtcNow;
                        await action();
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
        
        public static void Reset()
        {
            _lastUpdate = DateTime.MinValue;
        }
    }
}
