using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Detects the local Tailscale mesh-VPN interface so the Web Dashboard, pairing QR,
    /// and iOS companion app can use the private 100.x.y.z address as a third-tier
    /// failover route when the LAN is unreachable (device is off-site) and the
    /// Cloudflare Quick Tunnel is down.
    /// </summary>
    public static class TailscaleNetworkService
    {
        private static readonly object _lock = new();
        private static string? _cachedIp;
        private static DateTime _lastScan = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        /// <summary>True when a Tailscale IPv4 address is currently assigned.</summary>
        public static bool IsAvailable => !string.IsNullOrEmpty(GetTailscaleIp());

        /// <summary>
        /// Returns the Tailscale IPv4 address (100.64.0.0/10 CGNAT range) or null when Tailscale is not connected.
        /// Results are cached for 30 seconds to keep per-poll status builds cheap.
        /// </summary>
        public static string? GetTailscaleIp(bool forceRefresh = false)
        {
            lock (_lock)
            {
                if (!forceRefresh && _cachedIp != null && DateTime.UtcNow - _lastScan < CacheTtl)
                {
                    return _cachedIp;
                }
            }

            string? ip = ScanForTailscaleIp();

            lock (_lock)
            {
                _cachedIp = ip;
                _lastScan = DateTime.UtcNow;
            }

            return ip;
        }

        /// <summary>
        /// Returns the full http URL for the Tailscale interface (e.g. http://100.x.y.z:8080),
        /// or an empty string when Tailscale is not available.
        /// </summary>
        public static string GetTailscaleUrl(int port = 0)
        {
            var ip = GetTailscaleIp();
            if (string.IsNullOrEmpty(ip)) return "";

            if (port <= 0)
            {
                port = ConfigService.Current?.HttpServer?.Port > 0 ? ConfigService.Current.HttpServer.Port : 8080;
            }

            return $"http://{ip}:{port}";
        }

        private static string? ScanForTailscaleIp()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                // 1. Preferred: the adapter explicitly created by Tailscale ("Tailscale" / "tailscale#n")
                foreach (var ni in interfaces)
                {
                    var desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (!desc.Contains("tailscale")) continue;

                    var ip = FirstUsableIpv4(ni);
                    if (ip != null) return ip;
                }

                // 2. Fallback: any interface holding a CGNAT 100.64.0.0/10 address (Tailscale's shared address space)
                foreach (var ni in interfaces)
                {
                    var ip = FirstUsableIpv4(ni);
                    if (ip == null) continue;

                    if (IsInCgnatRange(ip)) return ip;
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[TailscaleNetwork] Interface scan failed: {ex.Message}", "Warning", "SYSTEM");
            }

            return null;
        }

        private static string? FirstUsableIpv4(NetworkInterface ni)
        {
            try
            {
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !System.Net.IPAddress.IsLoopback(addr.Address))
                    {
                        return addr.Address.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool IsInCgnatRange(string ip)
        {
            if (!System.Net.IPAddress.TryParse(ip, out var parsed)) return false;
            var bytes = parsed.GetAddressBytes();
            if (bytes.Length != 4) return false;
            // 100.64.0.0/10 → first octet 100, second octet in [64..127]
            return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
        }
    }
}