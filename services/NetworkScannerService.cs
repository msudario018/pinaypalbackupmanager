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
using System.Runtime.InteropServices;
using System.Text;
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
                if (!string.IsNullOrWhiteSpace(Vendor) && Vendor != "Unknown" && !Vendor.StartsWith("Device") && !Vendor.StartsWith("LAN"))
                    return $"{Vendor}-PC";

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

            // Re-read ARP table now that ping / TCP probes have populated the OS ARP cache
            var postScanArp = ReadArpTable();
            foreach (var h in results)
            {
                if ((string.IsNullOrWhiteSpace(h.MacAddress) || h.Vendor == "Unknown") && postScanArp.TryGetValue(h.IpAddress, out var entry))
                {
                    h.MacAddress = entry.Mac;
                    h.Vendor = entry.Vendor;
                }

                if (string.IsNullOrWhiteSpace(h.MacAddress))
                {
                    var directMac = QueryMacViaSendArp(h.IpAddress);
                    if (!string.IsNullOrWhiteSpace(directMac))
                    {
                        h.MacAddress = directMac;
                        h.Vendor = LookupVendor(directMac);
                    }
                }
            }

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
                // 1. Try NetBIOS Name Query (UDP 137) - very fast and gives exact Windows PC computer name
                try
                {
                    var nbName = await TryResolveNetBiosNameAsync(h.IpAddress, 400).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(nbName))
                    {
                        h.HostName = nbName;
                        return;
                    }
                }
                catch { }

                // 2. Fall back to reverse DNS
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

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(int destIp, int srcIp, byte[] pMacAddr, ref int phyAddrLen);

        private static string? QueryMacViaSendArp(string ip)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            try
            {
                if (!IPAddress.TryParse(ip, out var address)) return null;
                var ipBytes = address.GetAddressBytes();
                int destIp = BitConverter.ToInt32(ipBytes, 0);

                byte[] macBytes = new byte[6];
                int macLen = macBytes.Length;

                if (SendARP(destIp, 0, macBytes, ref macLen) == 0 && macLen == 6)
                {
                    return BitConverter.ToString(macBytes).Replace("-", "").ToUpperInvariant();
                }
            }
            catch { }
            return null;
        }

        private static async Task<string?> TryResolveNetBiosNameAsync(string ip, int timeoutMs = 400)
        {
            try
            {
                using var client = new UdpClient();
                client.Client.SendTimeout = timeoutMs;
                client.Client.ReceiveTimeout = timeoutMs;

                // Standard NetBIOS Node Status query request (RFC 1001/1002)
                byte[] request = new byte[]
                {
                    0x80, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x20, 0x43, 0x4B, 0x41,
                    0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                    0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                    0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                    0x41, 0x00, 0x00, 0x21, 0x00, 0x01
                };

                var ep = new IPEndPoint(IPAddress.Parse(ip), 137);
                await client.SendAsync(request, request.Length, ep).ConfigureAwait(false);

                using var cts = new CancellationTokenSource(timeoutMs);
                var result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
                var buffer = result.Buffer;

                if (buffer.Length > 57)
                {
                    int namesCount = buffer[56];
                    for (int i = 0; i < namesCount; i++)
                    {
                        int offset = 57 + (i * 18);
                        if (offset + 18 > buffer.Length) break;

                        byte recordType = buffer[offset + 15];
                        // 0x00 = Workstation/PC name, 0x20 = Server name
                        if (recordType == 0x00 || recordType == 0x20)
                        {
                            var nameBytes = new byte[15];
                            Array.Copy(buffer, offset, nameBytes, 0, 15);
                            var name = Encoding.ASCII.GetString(nameBytes).Trim();
                            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("__MSBROWSE__") && !name.Contains('\0'))
                            {
                                return name;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Comprehensive OUI vendor table for computers, servers, virtual machines, and network appliances.
        /// </summary>
        private static string LookupVendor(string macHex)
        {
            if (string.IsNullOrWhiteSpace(macHex) || macHex.Length < 6) return "Unknown";
            var oui = macHex.Substring(0, 6).ToUpperInvariant();
            return oui switch
            {
                // Virtualization & Containers
                "000C29" or "0050F2" or "000569" or "001C14" or "005056" => "VMware",
                "080027" or "0A0027" or "525400" => "VirtualBox",
                "00155D" or "0CC47A" => "Hyper-V",

                // Motherboards / NICs / PC OEMs
                "0002B3" or "000347" or "000423" or "000E0C" or "001302" or "001320" or "001500" or
                "001676" or "0018DE" or "0019D1" or "001B21" or "001C23" or "001D09" or "001E64" or
                "00215C" or "0022FB" or "0024D7" or "0026C6" or "0026C7" or "008082" or "3413E8" or
                "48F17F" or "548CA0" or "64006A" or "6805CA" or "70CD60" or "7C5CF8" or "8086F2" or
                "843A4B" or "88AE1D" or "8C1645" or "A434D9" or "A44E31" or "A86DAA" or "B49691" or
                "C8D9D2" or "DC5360" or "E82A44" or "F44D30" => "Intel",

                "000732" or "000A79" or "0016E6" or "0018E7" or "002215" or "002618" or "00E04C" or
                "047D7B" or "1866DA" or "204747" or "244BFE" or "448500" or "503EA0" or "5404A6" or
                "581122" or "6C198F" or "74D435" or "7C10C9" or "882583" or "A0B3CC" or "B025AA" or
                "BCF685" or "C85B76" or "D8BB26" or "E0D55E" or "EC086B" or "F46D04" => "Realtek",

                "00065B" or "000874" or "000BDB" or "001143" or "00123F" or "001372" or "001422" or
                "0015C5" or "0016F0" or "00188B" or "0019B9" or "001A6B" or "001E4F" or "00219B" or
                "002219" or "0023AE" or "0024E8" or "0026B9" or "180373" or "24B6FD" or "3417EB" or
                "44A842" or "74867A" or "842B2B" or "90B11C" or "A4BA4B" or "B8AC6F" or "BC305B" or
                "D4BED9" or "E4F004" or "F8BC12" => "Dell",

                "0001E6" or "0002A5" or "000802" or "000B46" or "000E7F" or "001083" or "00110A" or
                "001279" or "001321" or "0014C2" or "001560" or "001635" or "001708" or "0018FE" or
                "001A4B" or "001B78" or "001CC4" or "001E0B" or "00215A" or "00237D" or "002481" or
                "0025B3" or "002655" or "1C34DA" or "2C44FD" or "3C5282" or "3CD92B" or "40A8F0" or
                "705A0F" or "843497" or "984BE1" or "B4B52F" or "C8D3FF" or "D4C9EF" => "HP",

                "002186" or "002264" or "00262D" or "083E8E" or "08D40C" or "106530" or "141877" or
                "207693" or "283926" or "34E6D7" or "40B034" or "54EE75" or "600292" or "6CD158" or
                "8CDCD4" or "988389" or "A4B1C1" or "B88584" or "F0761C" => "Lenovo",

                "F4F5D8" or "3C7C3F" or "D85D4C" or "000C6E" or "0011D8" or "0013D4" or "0015F2" or
                "001731" or "0018F3" or "001A92" or "001BFC" or "001E8C" or "002354" or "00248C" or
                "049226" or "08606E" or "086266" or "0C9D92" or "107B44" or "10BF48" or "14DDA9" or
                "1831BF" or "1C872C" or "2C4D54" or "2CFDA1" or "3085A9" or "3497F6" or "382C4A" or
                "40167E" or "40169F" or "4C5262" or "50465D" or "6045CB" or "60A44C" or "704D7B" or
                "74D02B" or "7824AF" or "88D7F6" or "90E6BA" or "AC9E17" or "B06EBF" or "BC107B" or
                "C86000" or "E03F49" or "F832E4" => "ASUSTek",

                "0003FF" or "000D3A" or "00125A" or "0017FA" or "002248" or "0025AE" or
                "281878" or "3059B7" or "6045BD" or "70BC10" or "DCB4C4" or "F86A0F" => "Microsoft",

                "F0189E" or "ACDE48" or "001B63" or "001DD8" or "8C8590" or "000393" or "000502" or
                "000A27" or "000A95" or "000D93" or "0010FA" or "001124" or "001451" or "0016CB" or
                "0017F2" or "0019E3" or "001C42" or "001C6A" or "001E52" or "001E7D" or "001F5B" or
                "0021E9" or "002241" or "002312" or "002332" or "002369" or "0023DF" or "002436" or
                "002500" or "00254B" or "0025BC" or "002608" or "00264A" or "0026B0" or "109ADD" or
                "14109F" or "14205E" or "28CFE9" or "34363B" or "406C8F" or "48437C" or "542696" or
                "600308" or "68967B" or "701124" or "784F43" or "804971" or "88665A" or "9027E4" or
                "941625" or "9800C6" or "A43135" or "A82066" or "B019C6" or "B418D1" or "B817C2" or
                "C0847A" or "CC088D" or "D0034B" or "E05F45" or "F40F24" or "F82793" => "Apple",

                "B827EB" or "DCA632" or "E45F01" or "88D872" or "28CDC1" => "Raspberry Pi",

                // Network & Storage Appliances
                "001D0F" or "002127" or "0023CD" or "002586" or "002719" or "14CC20" or "18A6F7" or
                "1C3BF3" or "30B5C2" or "349672" or "50C7BF" or "54C80F" or "60E327" or "6466B3" or
                "647002" or "6C5AB0" or "704F57" or "7405A5" or "74DA38" or "78A183" or "7C8BCA" or
                "8416F9" or "882593" or "90F652" or "98DA4E" or "A0F3C1" or "B0487A" or "B09575" or
                "C006C3" or "C025E9" or "C04A00" or "D807B6" or "D84732" or "E4C32A" or "EC172F" or
                "F4EC38" => "TP-Link",

                "00095B" or "000FB5" or "00146C" or "00184D" or "001B2F" or "001E2A" or "001F33" or
                "00223F" or "0024B2" or "0026F2" or "04A151" or "08BD43" or "100C6B" or "10DA43" or
                "204E7F" or "20E52A" or "288088" or "2C3033" or "30469A" or "4494FC" or "4C60DE" or
                "6C7220" or "78D294" or "841B5E" or "84A8E4" or "9C3DCF" or "A00460" or "B07F2E" or
                "C0FFD4" or "C40415" or "C89E43" or "CC40D0" or "E0469A" or "E4F4C6" or "F87394" => "Netgear",

                "00156D" or "002722" or "0418D6" or "24A43C" or "44D9E7" or "687251" or
                "7483C2" or "788A20" or "802AA8" or "B4FBE4" or "DC9FDB" or "F09FC2" => "Ubiquiti",

                "001132" => "Synology",
                "00089B" or "245EBE" => "QNAP",

                "00000C" or "000142" or "000143" or "000163" or "000164" or "000196" or "000197" or
                "0001C7" or "0001C9" or "000216" or "000217" or "00024A" or "00027D" or "00027E" or
                "0002B9" or "0002BA" or "0002FC" or "0002FD" or "000331" or "000332" or "00036B" or
                "00036C" or "00039F" or "0003A0" or "0003E3" or "0003E4" or "000427" or "000428" => "Cisco",

                "0000F0" or "000278" or "0007AB" or "000DAE" or "001247" or "0012FB" or "001599" or
                "00166B" or "0017C9" or "0017D5" or "0018AF" or "001A8A" or "001B98" or "001C43" or
                "001D25" or "001DF6" or "002119" or "0021D1" or "002339" or "0023D6" or "0023D7" or
                "002454" or "002490" or "002491" or "002637" or "1489FD" or "244BFE" or "342387" or
                "4844F7" or "505527" or "54FA3E" or "608334" or "70F927" or "8425DB" or "90187C" or
                "A0821F" or "B0EC71" or "BC20A4" or "C4731E" or "D0176A" or "E8039A" or "F47B5E" => "Samsung",

                "009EEA" or "04CF4B" or "0C1DAF" or "102A94" or "14F65A" or "185936" or "1C3899" or
                "286C07" or "34800D" or "34CE00" or "38A4ED" or "3C286D" or "50642B" or "5448E6" or
                "584498" or "640980" or "64CC22" or "68DFDD" or "742344" or "7811DC" or "7C1D2A" or
                "842096" or "98FA9B" or "A470D6" or "B0E5ED" or "D4970B" or "F01898" or "F4844C" => "Xiaomi",

                "18FE34" or "240AC4" or "2462AB" or "246F28" or "24A160" or "24B2DE" or "2C3AE8" or
                "30AE84" or "3C6105" or "3C71BF" or "409151" or "483FDA" or "485519" or "545A46" or
                "5C0272" or "5CF9DD" or "600194" or "68C63A" or "70039F" or "7C9EBD" or "840D8E" or
                "84CCF9" or "84F3EB" or "9097D5" or "94B97E" or "A020A6" or "A4CF12" or "A842E3" or
                "AC67B2" or "B4E62D" or "BCDD53" or "C44F33" or "C82B96" or "CC50E3" or "DC4F22" or
                "ECFABC" or "F008D1" or "F4CFCE" => "Espressif",

                "001A11" or "3C5AB4" or "546009" or "94EB2C" or "D4E6B7" or "F4F5DB" or "F88F12" => "Google",
                "00FC8B" or "0C47C9" or "18742E" or "380197" or "38F73D" or "44650D" or "50DC5D" or
                "6854FD" or "747548" or "8871E5" or "A002DC" or "AC63BE" or "B47C9C" or "F0272D" => "Amazon",

                _ => $"Device ({macHex.Substring(0, 2)}:{macHex.Substring(2, 2)}:{macHex.Substring(4, 2)})"
            };
        }
    }
}