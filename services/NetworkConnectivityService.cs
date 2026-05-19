using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Monitors internet connectivity and raises events when the state changes.
    /// Uses NetworkInterface.GetIsNetworkAvailable() as a fast local check
    /// and optionally pings a reliable external host for confirmation.
    /// </summary>
    public static class NetworkConnectivityService
    {
        private static Timer? _pollTimer;
        private static bool _isOnline = true;
        private static readonly object _lock = new();
        private static int _isChecking = 0;

        /// <summary>Raised when connectivity state changes.</summary>
        public static event Action<bool>? OnConnectivityChanged;

        /// <summary>Current cached online status.</summary>
        public static bool IsOnline => _isOnline;

        /// <summary>Tags of tabs that require an active internet connection.</summary>
        public static readonly string[] InternetRequiredTags =
        {
            "FTP",
            "Mailchimp",
            "SQL",
            "HealthCheck",
            "PerformanceMetrics"
        };

        /// <summary>
        /// Starts periodic connectivity monitoring.
        /// </summary>
        public static void StartMonitoring(TimeSpan? interval = null)
        {
            StopMonitoring();

            _pollTimer = new Timer(interval?.TotalMilliseconds ?? 15000); // default 15s
            _pollTimer.Elapsed += async (_, _) => await CheckConnectivityAsync();
            _pollTimer.AutoReset = true;
            _pollTimer.Start();

            // Run an initial check immediately (fire-and-forget)
            _ = Task.Run(async () => await CheckConnectivityAsync());
        }

        /// <summary>
        /// Stops the monitoring timer.
        /// </summary>
        public static void StopMonitoring()
        {
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        /// <summary>
        /// Performs a connectivity check and raises OnConnectivityChanged if state changed.
        /// </summary>
        public static async Task CheckConnectivityAsync()
        {
            if (Interlocked.Exchange(ref _isChecking, 1) == 1)
                return;

            try
            {
                bool nowOnline = await ProbeConnectivityAsync();

                lock (_lock)
                {
                    if (nowOnline == _isOnline)
                        return;

                    _isOnline = nowOnline;
                }

                try
                {
                    OnConnectivityChanged?.Invoke(nowOnline);
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[NetworkConnectivity] Event handler error: {ex.Message}", "", "Warning", "SYSTEM");
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isChecking, 0);
            }
        }

        /// <summary>
        /// Returns true if the given sidebar tag requires internet to function.
        /// </summary>
        public static bool IsInternetRequired(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;

            foreach (var required in InternetRequiredTags)
            {
                if (string.Equals(tag, required, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static async Task<bool> ProbeConnectivityAsync()
        {
            // Fast local adapter check first
            if (!NetworkInterface.GetIsNetworkAvailable())
                return false;

            // Confirm with a lightweight ping to a reliable public DNS
            try
            {
                using var ping = new Ping();
                // Cloudflare DNS (1.1.1.1) and Google DNS (8.8.8.8) as fallbacks
                var reply = await ping.SendPingAsync("1.1.1.1", 3000).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                    return true;

                // Retry with Google DNS if Cloudflare fails
                reply = await ping.SendPingAsync("8.8.8.8", 3000).ConfigureAwait(false);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}
