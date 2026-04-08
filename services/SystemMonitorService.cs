using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class SystemMonitorService
    {
        private static long _lastNetworkBytesSent = 0;
        private static long _lastNetworkBytesReceived = 0;
        private static DateTime _lastNetworkCheck = DateTime.MinValue;
        private static long _lastDiskReadBytes = 0;
        private static DateTime _lastDiskCheck = DateTime.MinValue;

        /// <summary>
        /// Get current network usage in KB/s
        /// </summary>
        public static async Task<string> GetNetworkUsageAsync()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                long totalBytesReceived = 0;
                long totalBytesSent = 0;

                foreach (var ni in interfaces)
                {
                    var stats = ni.GetIPv4Statistics();
                    totalBytesReceived += stats.BytesReceived;
                    totalBytesSent += stats.BytesSent;
                }

                var now = DateTime.Now;
                if (_lastNetworkCheck == DateTime.MinValue)
                {
                    // First call - just store values
                    _lastNetworkBytesReceived = totalBytesReceived;
                    _lastNetworkBytesSent = totalBytesSent;
                    _lastNetworkCheck = now;
                    return "0 KB/s";
                }

                var timeDiff = (now - _lastNetworkCheck).TotalSeconds;
                if (timeDiff < 0.1) return "0 KB/s"; // Avoid division by zero

                var bytesReceivedDiff = totalBytesReceived - _lastNetworkBytesReceived;
                var bytesSentDiff = totalBytesSent - _lastNetworkBytesSent;
                var totalBytesDiff = bytesReceivedDiff + bytesSentDiff;

                var kbps = (totalBytesDiff / 1024.0) / timeDiff;

                // Update stored values
                _lastNetworkBytesReceived = totalBytesReceived;
                _lastNetworkBytesSent = totalBytesSent;
                _lastNetworkCheck = now;

                if (kbps < 1)
                    return $"{kbps * 1024:F0} B/s";
                else if (kbps < 1024)
                    return $"{kbps:F1} KB/s";
                else
                    return $"{kbps / 1024:F2} MB/s";
            }
            catch
            {
                return "0 KB/s";
            }
        }

        /// <summary>
        /// Get current disk I/O in MB/s; on Windows uses PDH for accurate per-second bytes; otherwise fallback.
        /// </summary>
        public static async Task<string> GetDiskIoAsync()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var bps = await GetWindowsDiskBytesPerSecondAsync();
                    if (bps >= 0)
                    {
                        var mbps = bps / (1024.0 * 1024.0);
                        if (mbps < 0.01)
                            return $"{bps / 1024.0:F1} KB/s";
                        else
                            return $"{mbps:F2} MB/s";
                    }
                }
            }
            catch { /* ignore and fallback */ }

            // Fallback approach if Windows PDH not available or fails
            return await GetFallbackDiskIoAsync();
        }

        // Windows-only: PDH wrappers to read disk read/write bytes per second accurately
        private static async Task<double> GetWindowsDiskBytesPerSecondAsync()
        {
            try
            {
                // Open PDH query and add counters for total read/write bytes per second
                if (PdhOpenQuery(null, IntPtr.Zero, out var hQuery) != 0) return -1;

                try
                {
                    var readPath = "\\\\PhysicalDisk(_Total)\\Disk Read Bytes/sec";
                    var writePath = "\\\\PhysicalDisk(_Total)\\Disk Write Bytes/sec";

                    if (PdhAddCounter(hQuery, readPath, IntPtr.Zero, out var hRead) != 0) return -1;
                    if (PdhAddCounter(hQuery, writePath, IntPtr.Zero, out var hWrite) != 0) return -1;

                    // First sample
                    if (PdhCollectQueryData(hQuery) != 0) return -1;
                    await Task.Delay(250);
                    // Second sample for rate calculation
                    if (PdhCollectQueryData(hQuery) != 0) return -1;

                    double readVal = GetDoubleCounterValue(hRead);
                    double writeVal = GetDoubleCounterValue(hWrite);
                    if (readVal < 0 || writeVal < 0) return -1;
                    return readVal + writeVal; // bytes per second
                }
                finally
                {
                    PdhCloseQuery(hQuery);
                }
            }
            catch
            {
                return -1;
            }
        }

        private static double GetDoubleCounterValue(IntPtr hCounter)
        {
            PDH_FMT_COUNTERVALUE value;
            var status = PdhGetFormattedCounterValue(hCounter, PDH_FMT_DOUBLE, out var _, out value);
            if (status == 0 && value.CStatus == 0)
            {
                return value.doubleValue;
            }
            return -1;
        }

        private const uint PDH_FMT_DOUBLE = 0x00000200;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhOpenQuery(string? dataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhAddCounter(IntPtr hQuery, string pszFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll")]
        private static extern int PdhGetFormattedCounterValue(
            IntPtr hCounter,
            uint dwFormat,
            out uint lpdwType,
            out PDH_FMT_COUNTERVALUE pValue);

        [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr hQuery);

        [StructLayout(LayoutKind.Sequential)]
        private struct PDH_FMT_COUNTERVALUE
        {
            public uint CStatus;
            public double doubleValue;
        }

        private static async Task<string> GetFallbackDiskIoAsync()
        {
            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady);
                long totalReadBytes = 0;

                foreach (var drive in drives)
                {
                    // We can't get real-time disk I/O without PerformanceCounters
                    // This is a rough estimate based on drive usage
                    totalReadBytes += drive.TotalSize - drive.AvailableFreeSpace;
                }

                var now = DateTime.Now;
                if (_lastDiskCheck == DateTime.MinValue)
                {
                    _lastDiskReadBytes = totalReadBytes;
                    _lastDiskCheck = now;
                    return "0 MB/s";
                }

                var timeDiff = (now - _lastDiskCheck).TotalSeconds;
                if (timeDiff < 0.1) return "0 MB/s";

                var bytesDiff = totalReadBytes - _lastDiskReadBytes;
                var mbps = (bytesDiff / (1024.0 * 1024.0)) / timeDiff;

                _lastDiskReadBytes = totalReadBytes;
                _lastDiskCheck = now;

                if (Math.Abs(mbps) < 0.01)
                    return "0 MB/s";
                else if (mbps < 1)
                    return $"{mbps * 1024:F1} KB/s";
                else
                    return $"{mbps:F2} MB/s";
            }
            catch
            {
                return "0 MB/s";
            }
        }

        /// <summary>
        /// Get detailed network info
        /// </summary>
        public static async Task<(string upload, string download, string total)> GetNetworkDetailsAsync()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up);

                long totalReceived = 0;
                long totalSent = 0;

                foreach (var ni in interfaces)
                {
                    var stats = ni.GetIPv4Statistics();
                    totalReceived += stats.BytesReceived;
                    totalSent += stats.BytesSent;
                }

                var receivedGB = totalReceived / (1024.0 * 1024 * 1024);
                var sentGB = totalSent / (1024.0 * 1024 * 1024);
                var totalGB = receivedGB + sentGB;

                return (
                    $"{sentGB:F2} GB",
                    $"{receivedGB:F2} GB",
                    $"{totalGB:F2} GB"
                );
            }
            catch
            {
                return ("0 GB", "0 GB", "0 GB");
            }
        }

        /// <summary>
        /// Reset all counters
        /// </summary>
        public static void ResetCounters()
        {
            _lastNetworkBytesSent = 0;
            _lastNetworkBytesReceived = 0;
            _lastNetworkCheck = DateTime.MinValue;
            _lastDiskReadBytes = 0;
            _lastDiskCheck = DateTime.MinValue;
        }
    }
}
