using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    /// <summary>How well a discovered address responded, used to rank results.</summary>
    public enum DiscoveryConfidence
    {
        /// <summary>Nothing answered.</summary>
        Unknown = 0,

        /// <summary>Host replied but does not look like PinayPal.</summary>
        HostUp = 1,

        /// <summary>Something answered on the web dashboard port.</summary>
        WebUp = 2,

        /// <summary>Identified PinayPal Backup Manager - safe to add with one tap.</summary>
        PinayPal = 3
    }

    /// <summary>A device discovered on the local subnet.</summary>
    public class DiscoveredHost
    {
        public string IpAddress { get; set; } = "";
        public string? HostName { get; set; }
        public string? MacAddress { get; set; }
        public string? Vendor { get; set; } = "Unknown";
        public int DashboardPort { get; set; }
        public DiscoveryConfidence Confidence { get; set; }
        public double ResponseMs { get; set; }
        public string AppVersion { get; set; } = "";
        public string HostnameFromServer { get; set; } = "";

        public bool IsPinayPal => Confidence == DiscoveryConfidence.PinayPal;

        /// <summary>Best-guess friendly name to pre-fill in the add-computer form.</summary>
        public string SuggestedName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(HostnameFromServer)) return HostnameFromServer;
                if (!string.IsNullOrWhiteSpace(HostName) && !string.Equals(HostName, IpAddress, StringComparison.OrdinalIgnoreCase))
                    return HostName!;

                var tail = IpAddress.Split('.').LastOrDefault();
                return string.IsNullOrEmpty(tail) ? IpAddress : $"PC-{tail}";
            }
        }
    }
    /// <summary>
    /// Scans the local subnet for other machines so they can be registered with a couple of
    /// taps instead of typing an address by hand.
    ///
    /// Design notes:
    ///  - The sweep is parallel and bounded, so a /24 finishes in a couple of seconds rather
    ///    than the very long serial equivalent.
    ///  - It only touches this machine's own subnet and only reads; nothing outside it is probed.
    ///  - Results are ranked so a real PinayPal instance is always listed first.
    /// </summary>
    public static class NetworkScannerService
    {
        private static readonly HttpClient _probe = new() { Timeout = TimeSpan.FromMilliseconds(1200) };

        /// <summary>
        /// Scans the /24 around this machine.
        /// </summary>
        /// <param name="ports">Dashboard ports to probe (default 8080).</param>
        /// <param name="maxConcurrency">Parallel probes, kept modest to avoid saturating a small router.</param>
        /// <param name="onFound">Optional per-device progress callback.</param>
        public static async Task<List<DiscoveredHost>> ScanAsync(
            IEnumerable<int>? ports = null,
            int maxConcurrency = 64,
            Action<DiscoveredHost>? onFound = null,
            CancellationToken cancellationToken = default)
        {
            var results = new ConcurrentBag<DiscoveredHost>();
            var probePorts = (ports ?? new[] { 8080 }).Distinct().ToArray();

            var subnet = GetLocalSubnet();
            if (subnet == null) return new List<DiscoveredHost>();

            var arp = ReadArpTable();
            using var throttle = new SemaphoreSlim(maxConcurrency);

            var tasks = EnumerateSubnet(subnet).Select(async ip =>
            {
                if (cancellationToken.IsCancellationRequested) return;

                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var host = await ProbeHostAsync(ip, probePorts, cancellationToken).ConfigureAwait(false);
                    if (host == null) return;

                    // Label anything already visible in the ARP cache so the user can tell
                    // "which one is the Dev PC?" without leaving the app.
                    if (arp.TryGetValue(host.IpAddress, out var known))
                    {
                        host.MacAddress = known.Mac;
                        host.Vendor = known.Vendor;
                    }

                    results.Add(host);
                    onFound?.Invoke(host);
                }
                catch { /* an unreachable address is the normal case, not an error */ }
                finally
                {
                    throttle.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            await ResolveHostNamesAsync(results).ConfigureAwait(false);

            return results.OrderByDescending(h => h.Confidence).ThenBy(h => h.IpAddress).ToList();
        }
        private static async Task<DiscoveredHost?> ProbeHostAsync(string ip, int[] ports, CancellationToken ct)
        {
            if (!await IsHostAliveAsync(ip, ct).ConfigureAwait(false)) return null;

            var host = new DiscoveredHost
            {
                IpAddress = ip,
                HostName = ip,
                Confidence = DiscoveryConfidence.HostUp
            };

            foreach (var port in ports)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"http://{ip}:{port}/api/ping");
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(1200);

                    using var response = await _probe.SendAsync(request, cts.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) continue;

                    var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    sw.Stop();

                    host.DashboardPort = port;
                    host.ResponseMs = sw.Elapsed.TotalMilliseconds;
                    host.Confidence = DiscoveryConfidence.WebUp;

                    // /api/ping returns appName/version/hostname for a PinayPal instance.
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    var appName = ReadString(root, "appName") ?? "";
                    if (appName.Contains("PinayPal", StringComparison.OrdinalIgnoreCase))
                    {
                        host.Confidence = DiscoveryConfidence.PinayPal;
                        host.AppVersion = ReadString(root, "version") ?? "";
                        host.HostnameFromServer = ReadString(root, "hostname") ?? "";
                        host.HostName = string.IsNullOrWhiteSpace(host.HostnameFromServer) ? ip : host.HostnameFromServer;
                    }

                    break; // One confirmed dashboard is enough.
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Timed out - try the next port.
                }
                catch { }
            }

            return host;
        }

        /// <summary>Fast liveness check: an ICMP echo with a short timeout.</summary>
        private static async Task<bool> IsHostAliveAsync(string ip, CancellationToken ct)
        {
            try
            {
                using var ping = new Ping();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(700);

                var reply = await ping.SendPingAsync(IPAddress.Parse(ip), 600)
                    .WaitAsync(cts.Token).ConfigureAwait(false);
                return reply.Status == IPStatus.Success;
            }
            catch { return false; }
        }

        private static async Task ResolveHostNamesAsync(IEnumerable<DiscoveredHost> hosts)
        {
            var pending = hosts
                .Where(h => string.IsNullOrWhiteSpace(h.HostName) || h.HostName == h.IpAddress)
                .ToList();
            if (pending.Count == 0) return;

            await Task.WhenAll(pending.Select(async h =>
            {
                try
                {
                    var entry = await Dns.GetHostEntryAsync(h.IpAddress).ConfigureAwait(false);
                    var name = entry.HostName;
                    if (!string.IsNullOrWhiteSpace(name) &&
                        !string.Equals(name, h.IpAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        h.HostName = name.Split('.')[0];
                    }
                }
                catch { /* reverse DNS frequently fails on home networks; the IP is fine */ }
            })).ConfigureAwait(false);
        }

        private static string? ReadString(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        private sealed class LocalSubnet
        {
            public byte[] Network { get; init; } = Array.Empty<byte>();
            public int PrefixLength { get; init; }
        }

        private static LocalSubnet? GetLocalSubnet()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        var address = unicast.Address;
                        if (address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(address)) continue;

                        var prefix = unicast.IPv4Mask != null ? CountBits(unicast.IPv4Mask) : 24;
                        if (prefix < 16 || prefix > 30) continue; // Skip odd /31 and /32 links.

                        return new LocalSubnet { Network = address.GetAddressBytes(), PrefixLength = prefix };
                    }
                }
            }
            catch { }
            return null;
        }

        private static int CountBits(IPAddress mask)
        {
            var bytes = mask.GetAddressBytes();
            var bits = 0;
            foreach (var b in bytes)
            {
                var v = b;
                while ((v & 0x80) != 0) { bits++; v <<= 1; }
                if (b != 0xFF) break;
            }
            return bits;
        }

        private static IEnumerable<string> EnumerateSubnet(LocalSubnet subnet)
        {
            var prefix = subnet.PrefixLength;

            // A /16 would be 65k probes, so cap the sweep at a /24.
            if (32 - prefix > 8) prefix = 24;

            var mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
            var network = BitConverter.ToUInt32(subnet.Network, 0) & mask;
            var count = 1u << (32 - prefix);

            // Skip the network and broadcast addresses.
            for (uint offset = 1; offset < count - 1; offset++)
            {
                var bytes = BitConverter.GetBytes(network + offset);
                if (bytes[0] == 0 || bytes[0] == 127) continue;
                yield return new IPAddress(bytes).ToString();
            }
        }

        /// <summary>
        /// Reads the local ARP table so already-seen devices can be labelled with their MAC and
        /// vendor, which makes "which one is the Dev PC?" obvious at a glance.
        /// </summary>
        public static Dictionary<string, (string Mac, string Vendor)> ReadArpTable()
        {
            var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

            // IPInterfaceProperties.GetArpEntries() is not available on this target framework,
            // so the neighbour cache is read from the system `arp` command instead. Failures
            // are non-fatal: the scan still works, devices just show without a vendor label.
            try
            {
                var psi = new ProcessStartInfo("arp", "-a")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return result;

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                foreach (var line in output.Split('\n'))
                {
                    // Typical: "  192.168.1.24           at a4-bb-6d-11-22-33"
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    if (!System.Net.IPAddress.TryParse(parts[0], out _)) continue;

                    var macToken = parts.FirstOrDefault(p => p.Contains('-') && p.Length >= 12);
                    if (macToken == null) continue;

                    var hex = new string(macToken.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
                    if (hex.Length != 12) continue;

                    result[parts[0]] = (hex, LookupVendor(hex));
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// A small OUI table covering vendors likely to appear on a home/office desk.
        /// Intentionally partial: an unknown vendor shows as "Unknown" rather than a guess.
        /// </summary>
        private static string LookupVendor(string macHex)
        {
            var oui = macHex.Substring(0, 6).ToUpperInvariant();
            return oui switch
            {
                "000C29" or "0050F2" or "000569" or "001C14" => "VMware",
                "080027" or "0A0027" or "525400" => "VirtualBox",
                "00155D" or "0CC47A" => "Hyper-V",
                "B827EB" or "DCA632" or "E45F01" or "88D872" => "Raspberry Pi",
                "001A11" => "Google",
                "F0189E" or "ACDE48" or "001B63" or "001DD8" or "8C8590" => "Apple",
                "F4F5D8" or "3C7C3F" or "D85D4C" => "ASUSTek",
                "002264" or "8CDCD4" or "54EE75" => "Lenovo",
                _ => "Unknown"
            };
        }
    }
}