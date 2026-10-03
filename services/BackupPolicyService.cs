using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    /// <summary>The outcome of evaluating whether a backup may run right now.</summary>
    public class BackupPolicyDecision
    {
        public bool Allowed { get; set; } = true;

        /// <summary>Short machine-readable reason, e.g. "window" or "bandwidth".</summary>
        public string Reason { get; set; } = "";

        /// <summary>Human-readable explanation suitable for a toast or the AI.</summary>
        public string Message { get; set; } = "";

        /// <summary>When set, the backup was deferred rather than rejected outright.</summary>
        public DateTime? RetryAfterUtc { get; set; }
    }

    /// <summary>
    /// Decides whether a scheduled backup is allowed to run, and prepares the machine for it.
    ///
    /// Three independent concerns live here because they all gate the same moment:
    ///   1. Memory      - evict the local LLM so a 16 GB box is not starved during a transfer.
    ///   2. Time window - e.g. never sync Mailchimp during business hours.
    ///   3. Bandwidth   - defer while the link is busy (gaming / streaming / big download).
    ///
    /// All three are opt-in and default to a conservative, non-surprising configuration.
    /// </summary>
    public static class BackupPolicyService
    {
        /// <summary>
        /// Samples upload throughput from the OS network counters. This reuses the same counters
        /// the dashboard already reads, so the overhead is negligible.
        /// </summary>
        public static class BandwidthGuard
        {
            private static long _lastSentBytes;
            private static long _lastReceivedBytes;
            private static DateTime _lastSample = DateTime.MinValue;
            private static readonly object _sampleLock = new();
            private static System.Threading.Timer? _timer;
            private static DateTime _busySince = DateTime.MinValue;

            /// <summary>Smoothed upload rate in KB/s.</summary>
            public static double CurrentKbps { get; private set; }

            /// <summary>Smoothed download rate in KB/s.</summary>
            public static double CurrentDownKbps { get; private set; }

            /// <summary>True once the link has stayed above the threshold past the grace period.</summary>
            public static bool IsLinkBusy { get; private set; }

            public static void Start()
            {
                if (_timer != null) return;
                _timer = new System.Threading.Timer(_ => Sample(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
                Sample();
            }

            public static void Stop()
            {
                _timer?.Dispose();
                _timer = null;
            }
            private static void Sample()
            {
                try
                {
                    long sent = 0, received = 0;
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        var stats = nic.GetIPv4Statistics();
                        sent += stats.BytesSent;
                        received += stats.BytesReceived;
                    }

                    lock (_sampleLock)
                    {
                        var now = DateTime.UtcNow;
                        if (_lastSample != DateTime.MinValue)
                        {
                            var seconds = (now - _lastSample).TotalSeconds;
                            if (seconds > 0.5)
                            {
                                var upKbps = ((sent - _lastSentBytes) / 1024d) / seconds;
                                var downKbps = ((received - _lastReceivedBytes) / 1024d) / seconds;

                                // Exponential smoothing: one backup burst should not instantly trip
                                // the guard, and one idle sample should not instantly clear it.
                                CurrentKbps = (CurrentKbps * 0.6) + (Math.Max(0, upKbps) * 0.4);
                                CurrentDownKbps = (CurrentDownKbps * 0.6) + (Math.Max(0, downKbps) * 0.4);
                            }
                        }

                        _lastSentBytes = sent;
                        _lastReceivedBytes = received;
                        _lastSample = now;
                    }

                    EvaluateBusyState();
                }
                catch
                {
                    // A counter failure must never break backups; treat the link as idle.
                    IsLinkBusy = false;
                }
            }

            private static void EvaluateBusyState()
            {
                var op = ConfigService.Current.Operation;
                if (!op.BandwidthGuardEnabled)
                {
                    IsLinkBusy = false;
                    _busySince = DateTime.MinValue;
                    return;
                }

                var threshold = Math.Max(1, op.BandwidthThresholdKbps);
                var busy = CurrentKbps > threshold;

                if (busy)
                {
                    if (_busySince == DateTime.MinValue) _busySince = DateTime.UtcNow;
                    var grace = TimeSpan.FromSeconds(Math.Clamp(op.BandwidthGraceSeconds, 0, 600));
                    IsLinkBusy = (DateTime.UtcNow - _busySince) >= grace;
                }
                else
                {
                    _busySince = DateTime.MinValue;
                    IsLinkBusy = false;
                }
            }

            /// <summary>Forces an immediate re-evaluation, used after config changes.</summary>
            public static void Reset()
            {
                lock (_sampleLock)
                {
                    _lastSample = DateTime.MinValue;
                    _lastSentBytes = 0;
                    _lastReceivedBytes = 0;
                }
                _busySince = DateTime.MinValue;
                IsLinkBusy = false;
                CurrentKbps = 0;
                CurrentDownKbps = 0;
                Sample();
            }
        }
        /// <summary>Starts background bandwidth sampling. Safe to call repeatedly.</summary>
        public static void Start()
        {
            BandwidthGuard.Start();
        }

        /// <summary>
        /// Evaluates every gate for one service. Returns the first blocking reason, or an
        /// allowed decision when the backup can proceed.
        /// </summary>
        public static BackupPolicyDecision Evaluate(string service)
        {
            var op = ConfigService.Current.Operation;

            // 1. Time window per service.
            if (op.SyncWindowsEnabled)
            {
                var window = GetWindow(service);
                if (window != null && !window.IsAllDay)
                {
                    var now = DateTime.Now;
                    if (!window.Contains(now))
                    {
                        return new BackupPolicyDecision
                        {
                            Allowed = false,
                            Reason = "window",
                            Message = $"{Label(service)} is only allowed between {window.Describe()}. Right now it is {now:HH:mm}.",
                            RetryAfterUtc = NextWindowStartUtc(window, now)
                        };
                    }
                }
            }

            // 2. Bandwidth guard.
            if (op.BandwidthGuardEnabled && BandwidthGuard.IsLinkBusy)
            {
                return new BackupPolicyDecision
                {
                    Allowed = false,
                    Reason = "bandwidth",
                    Message = $"Upload is at {BandwidthGuard.CurrentKbps:F0} KB/s, above your {op.BandwidthThresholdKbps} KB/s threshold. Pausing so gaming and streaming stay smooth.",
                    RetryAfterUtc = DateTime.UtcNow.AddMinutes(5)
                };
            }

            return new BackupPolicyDecision { Allowed = true, Reason = "ok", Message = "Clear to run." };
        }

        /// <summary>
        /// Frees memory before a heavy transfer by evicting the local Ollama model.
        /// Best effort: a failure here must never block the backup.
        /// </summary>
        public static async Task<string> PrepareMemoryAsync(string service)
        {
            if (!ConfigService.Current.Operation.EvictAiModelBeforeBackup) return "";

            try
            {
                var provider = AIAssistantService.Config.Provider;
                if (!provider.Equals("ollama", StringComparison.OrdinalIgnoreCase) &&
                    !provider.Equals("hybrid", StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }

                var (ok, message) = await AIAssistantService.UnloadOllamaModelAsync();
                if (!ok) return "";

                LogService.WriteSystemLog($"[Policy] Released local AI model before {Label(service)} backup.", "Information", "BACKUPSCHEDULE");
                return message;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[Policy] AI model eviction failed: {ex.Message}", "Warning", "BACKUPSCHEDULE");
                return "";
            }
        }

        /// <summary>Resolves the configured window for a service key ("ftp", "sql", "mailchimp").</summary>
        public static SyncWindowSettings? GetWindow(string service)
        {
            var windows = ConfigService.Current.Windows;
            if (windows == null) return null;

            var key = (service ?? "").Trim().ToLowerInvariant();
            if (key.Contains("sql") || key.Contains("database")) return windows.Sql;
            if (key.Contains("mailchimp")) return windows.Mailchimp;
            return windows.Ftp;
        }

        private static string Label(string service) => (service ?? "").ToUpperInvariant() switch
        {
            "FTP" => "Website FTP",
            "SQL" => "SQL database",
            "MAILCHIMP" => "Mailchimp",
            _ => service ?? "Backup"
        };

        private static DateTime NextWindowStartUtc(SyncWindowSettings window, DateTime localNow)
        {
            // Approximate by working in local minutes and converting back; good enough for a
            // "try again at HH:mm" hint.
            var minute = (localNow.Hour * 60) + localNow.Minute;
            var delta = window.StartMinute - minute;
            if (delta <= 0) delta += 1440;
            return DateTime.UtcNow.AddMinutes(delta);
        }
    }
}