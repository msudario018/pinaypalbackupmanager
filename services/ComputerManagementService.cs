using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    /// <summary>Role of a managed computer within the home/office fleet.</summary>
    public enum ComputerRole
    {
        Main = 0,
        Dev = 1,
        Aux = 2
    }

    /// <summary>Power/remote actions that can be dispatched to a managed computer.</summary>
    public enum ComputerPowerAction
    {
        Wake = 0,
        Shutdown = 1,
        Restart = 2,
        Lock = 3,
        Sleep = 4,
        SignOut = 5
    }

    /// <summary>A single managed computer: either this local host or a peer running PinayPal.</summary>
    public class ComputerNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string DisplayName { get; set; } = "Computer";
        public ComputerRole Role { get; set; } = ComputerRole.Aux;

        /// <summary>MAC address used for Wake-on-LAN magic packets (12 hex digits, no separators).</summary>
        public string MacAddress { get; set; } = "";

        /// <summary>Directed or global broadcast address used for the magic packet.</summary>
        public string BroadcastAddress { get; set; } = "255.255.255.255";

        public int WolPort { get; set; } = 9;

        /// <summary>Base URL of the peer's PinayPal dashboard, e.g. http://192.168.1.20:8080</summary>
        public string ApiBaseUrl { get; set; } = "";

        /// <summary>Optional web access PIN for the peer. Never returned back to API clients.</summary>
        public string Pin { get; set; } = "";

        public bool IsLocal { get; set; }

        public bool Enabled { get; set; } = true;

        public string Notes { get; set; } = "";
    }

    /// <summary>Live telemetry for one computer, flattened for easy mobile rendering.</summary>
    public class ComputerTelemetrySnapshot
    {
        public bool IsOnline { get; set; }
        public string Hostname { get; set; } = "";
        public string OsDescription { get; set; } = "";
        public string LocalIp { get; set; } = "";
        public string Version { get; set; } = "";
        public int? LatencyMs { get; set; }
        public double? CpuUsagePercent { get; set; }
        public double? CpuTempC { get; set; }
        public string CpuName { get; set; } = "";
        public string GpuName { get; set; } = "";
        public double? GpuTempC { get; set; }
        public double? GpuUsagePercent { get; set; }
        public double? RamUsagePercent { get; set; }
        public double? RamFreeGB { get; set; }
        public double? RamTotalGB { get; set; }
        public double? AppRamUsageMB { get; set; }
        public string UpTime { get; set; } = "";
        public DateTime? LastSeenUtc { get; set; }
        public string Error { get; set; } = "";
    }

    /// <summary>A node plus its most recent telemetry, ready for API and UI consumption.</summary>
    public class ComputerView
    {
        public ComputerNode Node { get; set; } = new();
        public ComputerTelemetrySnapshot Telemetry { get; set; } = new();
        public List<string> AvailableActions { get; set; } = new();
    }

    public class ComputerPowerResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string TargetId { get; set; } = "";
    }
    /// <summary>
    /// Manages the user's computers (for example "Dev PC" and "Main PC"):
    /// wake-on-LAN, remote restart/shutdown/lock, and aggregated hardware telemetry.
    ///
    /// Power actions are always routed through a guarded path:
    ///  - The local host executes the Windows command directly after an explicit grace delay.
    ///  - A peer is commanded through its own PinayPal dashboard (/api/power/*), which the
    ///    peer independently authenticates. Nothing is blindly trusted across machines.
    /// </summary>
    public static class ComputerManagementService
    {
        private static readonly string ConfigFilePath = AppDataPaths.GetPath("computers.json");
        private static readonly object _fileLock = new();
        private static List<ComputerNode>? _nodes;

        private static readonly Dictionary<string, ComputerTelemetrySnapshot> _cache = new();
        private static readonly object _cacheLock = new();

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };

        // ==========================================
        // Registry persistence
        // ==========================================

        public static List<ComputerNode> GetNodes()
        {
            lock (_fileLock)
            {
                if (_nodes != null) return _nodes.ToList();

                try
                {
                    if (File.Exists(ConfigFilePath))
                    {
                        var json = File.ReadAllText(ConfigFilePath);
                        _nodes = JsonSerializer.Deserialize<List<ComputerNode>>(json) ?? new List<ComputerNode>();
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[Computers] Failed to load registry: {ex.Message}", "Warning", "SYSTEM");
                }

                _nodes ??= new List<ComputerNode>();
                EnsureLocalNodeRegistered(_nodes);
                return _nodes.ToList();
            }
        }

        public static void SaveNodes(IEnumerable<ComputerNode> nodes)
        {
            lock (_fileLock)
            {
                try
                {
                    _nodes = nodes.Select(SanitizeNode).ToList();
                    EnsureLocalNodeRegistered(_nodes);

                    var json = JsonSerializer.Serialize(_nodes, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(ConfigFilePath, json);
                    LogService.WriteSystemLog($"[Computers] Registry saved ({_nodes.Count} computers).", "Information", "SYSTEM");
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[Computers] Failed to save registry: {ex.Message}", "Error", "SYSTEM");
                }
            }
        }

        public static ComputerNode? FindNode(string id)
        {
            return GetNodes().FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Best-effort match by display name or role, used by conversational commands.</summary>
        public static ComputerNode? FindNodeByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var needle = name.Trim().ToLowerInvariant();
            var nodes = GetNodes();

            return nodes.FirstOrDefault(n => n.DisplayName.ToLowerInvariant().Contains(needle))
                ?? nodes.FirstOrDefault(n => needle.Contains(n.DisplayName.ToLowerInvariant()))
                ?? (needle.Contains("dev") ? nodes.FirstOrDefault(n => n.Role == ComputerRole.Dev) : null)
                ?? (needle.Contains("main") ? nodes.FirstOrDefault(n => n.Role == ComputerRole.Main) : null);
        }

        private static ComputerNode SanitizeNode(ComputerNode n)
        {
            n.DisplayName = string.IsNullOrWhiteSpace(n.DisplayName) ? "Computer" : n.DisplayName.Trim();
            n.MacAddress = NormalizeMac(n.MacAddress);
            n.BroadcastAddress = string.IsNullOrWhiteSpace(n.BroadcastAddress) ? "255.255.255.255" : n.BroadcastAddress.Trim();
            n.WolPort = n.WolPort is < 1 or > 65535 ? 9 : n.WolPort;
            n.ApiBaseUrl = (n.ApiBaseUrl ?? "").Trim().TrimEnd('/');
            return n;
        }
        /// <summary>Registers this machine automatically so "My Computers" is never empty.</summary>
        private static void EnsureLocalNodeRegistered(List<ComputerNode> nodes)
        {
            var local = nodes.FirstOrDefault(n => n.IsLocal);
            if (local != null)
            {
                if (string.IsNullOrWhiteSpace(local.DisplayName)) local.DisplayName = Environment.MachineName;
                return;
            }

            var node = new ComputerNode
            {
                DisplayName = Environment.MachineName,
                Role = ComputerRole.Main,
                MacAddress = GetPrimaryMacAddress(),
                IsLocal = true,
                Enabled = true,
                Notes = "This PC (auto-detected)"
            };

            // Preserve a previously configured friendly name / role if one already exists.
            var existing = nodes.FirstOrDefault(n =>
                string.Equals(n.DisplayName, Environment.MachineName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                node.DisplayName = existing.DisplayName;
                node.Role = existing.Role;
                node.Notes = existing.Notes;
            }

            nodes.Add(node);
        }

        public static string NormalizeMac(string? mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return "";
            var hex = new string(mac.Where(Uri.IsHexDigit).ToArray());
            return hex.Length == 12 ? hex.ToUpperInvariant() : "";
        }

        public static string FormatMac(string? mac)
        {
            var hex = NormalizeMac(mac);
            if (hex.Length != 12) return "";
            return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
        }

        public static string GetPrimaryMacAddress()
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .OrderByDescending(n => n.Speed)
                    .FirstOrDefault();

                if (nic != null) return NormalizeMac(nic.GetPhysicalAddress().ToString());
            }
            catch { }
            return "";
        }

        public static string GetLanIPv4Address()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Select(a => a.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                         && !System.Net.IPAddress.IsLoopback(a)
                                         && !a.ToString().StartsWith("169.254"))
                    ?.ToString() ?? "";
            }
            catch { return ""; }
        }
        // ==========================================
        // Telemetry aggregation
        // ==========================================

        public static async Task<List<ComputerView>> GetFleetAsync(bool forceRefresh = false)
        {
            var nodes = GetNodes().Where(n => n.Enabled).ToList();
            var views = new List<ComputerView>();

            foreach (var node in nodes)
            {
                ComputerTelemetrySnapshot? cached = null;
                if (!forceRefresh)
                {
                    lock (_cacheLock) { _cache.TryGetValue(node.Id, out cached); }
                }

                // Always re-probe when we have nothing cached or the peer looked unreachable.
                var snap = cached ?? await ProbeNodeAsync(node);
                if (forceRefresh || snap == null || !snap.IsOnline)
                {
                    snap = await ProbeNodeAsync(node);
                }

                views.Add(new ComputerView
                {
                    Node = node,
                    Telemetry = snap,
                    AvailableActions = GetAvailableActions(node)
                });
            }

            return views;
        }

        private static List<string> GetAvailableActions(ComputerNode node)
        {
            var actions = new List<string> { "wake", "refresh" };
            if (node.IsLocal || !string.IsNullOrWhiteSpace(node.ApiBaseUrl))
            {
                actions.AddRange(new[] { "restart", "shutdown", "lock", "sleep", "signout" });
            }
            return actions;
        }

        private static async Task<ComputerTelemetrySnapshot> ProbeNodeAsync(ComputerNode node)
        {
            var snap = new ComputerTelemetrySnapshot();

            try
            {
                if (node.IsLocal)
                {
                    var hw = await HardwareTelemetryService.GetTelemetryAsync();
                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

                    snap.IsOnline = true;
                    snap.Hostname = hw.Hostname;
                    snap.OsDescription = hw.OsDescription;
                    snap.LocalIp = GetLanIPv4Address();
                    snap.Version = BackupConfig.AppVersion;
                    snap.LatencyMs = 0;
                    snap.CpuUsagePercent = hw.CpuUsagePercent;
                    snap.CpuTempC = hw.CpuTempC;
                    snap.CpuName = hw.CpuName;
                    snap.GpuName = hw.GpuName;
                    snap.GpuTempC = hw.GpuTempC;
                    snap.GpuUsagePercent = hw.GpuUsagePercent;
                    snap.RamUsagePercent = hw.RamUsagePercent;
                    snap.RamFreeGB = hw.RamFreeGB;
                    snap.RamTotalGB = hw.RamTotalGB;
                    snap.AppRamUsageMB = hw.AppRamUsageMB;
                    snap.UpTime = uptime.Days > 0
                        ? $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m"
                        : $"{uptime.Hours}h {uptime.Minutes}m";
                    snap.LastSeenUtc = DateTime.UtcNow;
                }
                else
                {
                    snap = await ProbePeerAsync(node);
                }
            }
            catch (Exception ex)
            {
                snap.IsOnline = false;
                snap.Error = ex.Message;
                snap.LastSeenUtc = DateTime.UtcNow;
            }

            lock (_cacheLock) { _cache[node.Id] = snap; }
            return snap;
        }
        private static async Task<ComputerTelemetrySnapshot> ProbePeerAsync(ComputerNode node)
        {
            var snap = new ComputerTelemetrySnapshot();
            var baseUrl = (node.ApiBaseUrl ?? "").TrimEnd('/');

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                snap.Error = "No dashboard URL configured for this computer.";
                snap.LastSeenUtc = DateTime.UtcNow;
                return snap;
            }

            var sw = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/hardware/telemetry");
            if (!string.IsNullOrWhiteSpace(node.Pin))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", node.Pin);
            }

            using var cts = new CancellationTokenSource(5000);
            using var response = await _http.SendAsync(request, cts.Token);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                snap.Error = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Dashboard requires a matching access PIN."
                    : $"Dashboard responded HTTP {(int)response.StatusCode}.";
                snap.LastSeenUtc = DateTime.UtcNow;
                return snap;
            }

            var body = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            snap.IsOnline = true;
            snap.LatencyMs = (int)sw.ElapsedMilliseconds;
            snap.LastSeenUtc = DateTime.UtcNow;

            snap.Hostname = ReadString(root, "hostname");
            snap.OsDescription = ReadString(root, "osDescription");
            snap.CpuName = ReadString(root, "cpuName");
            snap.GpuName = ReadString(root, "gpuName");
            snap.CpuUsagePercent = ReadDouble(root, "cpuUsagePercent");
            snap.CpuTempC = ReadDouble(root, "cpuTempC");
            snap.GpuTempC = ReadDouble(root, "gpuTempC");
            snap.GpuUsagePercent = ReadDouble(root, "gpuUsagePercent");
            snap.RamUsagePercent = ReadDouble(root, "ramUsagePercent");
            snap.RamFreeGB = ReadDouble(root, "ramFreeGB");
            snap.RamTotalGB = ReadDouble(root, "ramTotalGB");
            snap.AppRamUsageMB = ReadDouble(root, "appRamUsageMB");

            return snap;
        }

        private static string ReadString(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        private static double? ReadDouble(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.Number when v.TryGetDouble(out var d) => d,
                JsonValueKind.String when double.TryParse(v.GetString(), out var parsed) => parsed,
                _ => null
            };
        }
        // ==========================================
        // Wake-on-LAN
        // ==========================================

        /// <summary>Sends a Wake-on-LAN magic packet for the given node.</summary>
        public static async Task<ComputerPowerResult> WakeAsync(string nodeId)
        {
            var node = FindNode(nodeId);
            if (node == null)
            {
                return new ComputerPowerResult { Success = false, Message = "Unknown computer." };
            }

            var cleanMac = NormalizeMac(node.MacAddress);
            if (cleanMac.Length != 12)
            {
                return new ComputerPowerResult
                {
                    Success = false,
                    TargetId = nodeId,
                    Action = "wake",
                    Message = $"A valid 12-digit MAC address is required to wake {node.DisplayName}. Add one in Settings → My Computers."
                };
            }

            var packet = BuildMagicPacket(cleanMac);
            var port = node.WolPort is < 1 or > 65535 ? 9 : node.WolPort;

            try
            {
                using var udp = new UdpClient();
                udp.EnableBroadcast = true;
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

                var broadcast = IPAddress.Parse(
                    string.IsNullOrWhiteSpace(node.BroadcastAddress) ? "255.255.255.255" : node.BroadcastAddress);

                await udp.SendAsync(packet, packet.Length, new IPEndPoint(broadcast, port));

                LogService.WriteSystemLog($"[Computers] Wake-on-LAN packet sent to {node.DisplayName} ({FormatMac(cleanMac)}).", "Information", "SYSTEM");

                return new ComputerPowerResult
                {
                    Success = true,
                    TargetId = nodeId,
                    Action = "wake",
                    Message = $"Wake-on-LAN signal sent to **{node.DisplayName}** via {broadcast} (port {port}). It usually powers on within 30 seconds."
                };
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[Computers] Wake-on-LAN failed for {node.DisplayName}: {ex.Message}", "Error", "SYSTEM");
                return new ComputerPowerResult
                {
                    Success = false,
                    TargetId = nodeId,
                    Action = "wake",
                    Message = $"Could not send the wake signal: {ex.Message}"
                };
            }
        }

        private static byte[] BuildMagicPacket(string cleanMac)
        {
            var macBytes = new byte[6];
            for (var i = 0; i < 6; i++)
            {
                macBytes[i] = Convert.ToByte(cleanMac.Substring(i * 2, 2), 16);
            }

            // 6 sync bytes of 0xFF followed by the 6-byte MAC repeated 16 times.
            var packet = new byte[6 + (16 * 6)];
            for (var i = 0; i < 6; i++) packet[i] = 0xFF;
            for (var repetition = 0; repetition < 16; repetition++)
            {
                Buffer.BlockCopy(macBytes, 0, packet, 6 + (repetition * 6), 6);
            }

            return packet;
        }
        // ==========================================
        // Power actions
        // ==========================================

        /// <summary>
        /// Dispatches a power action. Destructive actions always carry a grace delay so an
        /// accidental tap cannot instantly kill a working machine, and every call is logged.
        /// </summary>
        public static async Task<ComputerPowerResult> ExecutePowerAsync(string nodeId, string action, int delaySeconds = 10)
        {
            var node = FindNode(nodeId);
            if (node == null)
            {
                return new ComputerPowerResult { Success = false, Message = "Unknown computer." };
            }

            if (!Enum.TryParse<ComputerPowerAction>(action, ignoreCase: true, out var parsed))
            {
                return new ComputerPowerResult
                {
                    Success = false,
                    TargetId = nodeId,
                    Action = action,
                    Message = $"Unsupported action '{action}'."
                };
            }

            // Wake always happens locally because it needs a raw UDP broadcast.
            if (parsed == ComputerPowerAction.Wake)
            {
                return await WakeAsync(nodeId);
            }

            var delay = Math.Clamp(delaySeconds, 0, 300);

            try
            {
                var result = node.IsLocal
                    ? ExecuteLocalPower(parsed, delay)
                    : await SendPeerPowerAsync(node, parsed, delay);

                LogService.WriteSystemLog(
                    $"[Computers] Power '{parsed}' -> {node.DisplayName}: {(result.Success ? "OK" : "FAILED")} - {result.Message}",
                    result.Success ? "Information" : "Error", "SYSTEM");

                return new ComputerPowerResult
                {
                    Success = result.Success,
                    TargetId = nodeId,
                    Action = parsed.ToString().ToLowerInvariant(),
                    Message = result.Message
                };
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[Computers] Power action failed for {node.DisplayName}: {ex.Message}", "Error", "SYSTEM");
                return new ComputerPowerResult
                {
                    Success = false,
                    TargetId = nodeId,
                    Action = parsed.ToString().ToLowerInvariant(),
                    Message = $"Power action failed: {ex.Message}"
                };
            }
        }

        private static (bool Success, string Message) ExecuteLocalPower(ComputerPowerAction action, int delaySeconds)
        {
            switch (action)
            {
                case ComputerPowerAction.Shutdown:
                    // /t gives the user a grace window; /c documents the initiator in the event log.
                    StartHiddenProcess("shutdown.exe", $"/s /t {delaySeconds} /c \"PinayPal remote request\"");
                    return (true, $"This PC will **shut down in {delaySeconds} seconds**. Run `shutdown /a` to abort.");

                case ComputerPowerAction.Restart:
                    StartHiddenProcess("shutdown.exe", $"/r /t {delaySeconds} /c \"PinayPal remote request\"");
                    return (true, $"This PC will **restart in {delaySeconds} seconds**. Run `shutdown /a` to abort.");

                case ComputerPowerAction.Lock:
                    StartHiddenProcess("rundll32.exe", "user32.dll,LockWorkStation");
                    return (true, "Workstation locked.");

                case ComputerPowerAction.SignOut:
                    StartHiddenProcess("shutdown.exe", "/l");
                    return (true, "User signed out.");

                case ComputerPowerAction.Sleep:
                    // SetSuspendState(0,1,0) hibernates, which resumes faster and preserves the session.
                    StartHiddenProcess("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                    return (true, "Sleep requested.");

                default:
                    return (false, $"Action {action} is not supported on the local host.");
            }
        }
        /// <summary>Proxies a power action to a peer PC through that peer's own PinayPal dashboard.</summary>
        private static async Task<(bool Success, string Message)> SendPeerPowerAsync(ComputerNode node, ComputerPowerAction action, int delaySeconds)
        {
            var baseUrl = (node.ApiBaseUrl ?? "").TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return (false, $"{node.DisplayName} needs a dashboard URL before remote power actions can be used. Add one in Settings → My Computers.");
            }

            var url = $"{baseUrl}/api/power/{action.ToString().ToLowerInvariant()}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { delaySeconds, source = "pinaypal-fleet" }),
                    Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(node.Pin))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", node.Pin);
            }

            using var cts = new CancellationTokenSource(8000);
            using var response = await _http.SendAsync(request, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);

            if (response.IsSuccessStatusCode)
            {
                return (true, $"{node.DisplayName} accepted the {action.ToString().ToLowerInvariant()} request.");
            }

            var detail = ExtractError(body);
            var hint = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? " The access PIN does not match on that PC."
                : "";

            return (false, detail ?? $"{node.DisplayName} rejected the request (HTTP {(int)response.StatusCode}).{hint}");
        }

        private static string? ExtractError(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                {
                    return msg.GetString();
                }
            }
            catch { }
            return null;
        }

        private static void StartHiddenProcess(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }

        // ==========================================
        // Formatting helpers for conversational replies
        // ==========================================

        // ==========================================
        // Presentation helpers (kept UI-framework agnostic on purpose)
        // ==========================================

        /// <summary>A short human label for a role, e.g. "Dev PC".</summary>
        public static string RoleLabel(ComputerRole role) => role switch
        {
            ComputerRole.Main => "Main PC",
            ComputerRole.Dev => "Dev PC",
            _ => "Auxiliary"
        };

        /// <summary>Which quick actions can be offered for a node, given how it is configured.</summary>
        public static List<string> AvailableActions(ComputerNode node)
        {
            var actions = new List<string> { "wake", "refresh" };
            if (node.IsLocal || !string.IsNullOrWhiteSpace(node.ApiBaseUrl))
            {
                actions.AddRange(new[] { "restart", "shutdown", "lock", "sleep", "signout" });
            }
            return actions;
        }

        /// <summary>Builds the public (PIN-free) projection of a node for API clients.</summary>
        public static object ToPublicNode(ComputerNode n) => new
        {
            n.Id,
            n.DisplayName,
            Role = RoleLabel(n.Role),
            n.MacAddress,
            n.BroadcastAddress,
            n.WolPort,
            n.ApiBaseUrl,
            n.IsLocal,
            n.Enabled,
            n.Notes
        };

        public static string DescribeFleet(IEnumerable<ComputerView> views)
        {
            var sb = new StringBuilder();
            sb.AppendLine("### 💻 Your Computers");

            foreach (var v in views)
            {
                var t = v.Telemetry;
                var badge = t.IsOnline ? "🟢 Online" : "🔴 Offline";
                var role = v.Node.Role == ComputerRole.Main ? "Main PC"
                    : v.Node.Role == ComputerRole.Dev ? "Dev PC" : "Auxiliary";

                sb.AppendLine($"- **{v.Node.DisplayName}** ({role}) — {badge}");
                if (t.IsOnline)
                {
                    var cpu = t.CpuUsagePercent.HasValue ? $"{t.CpuUsagePercent.Value:F0}% CPU" : "CPU n/a";
                    var ram = t.RamUsagePercent.HasValue ? $"{t.RamUsagePercent.Value:F0}% RAM" : "RAM n/a";
                    var temp = t.CpuTempC.HasValue ? $" · {t.CpuTempC.Value:F0}°C" : "";
                    sb.AppendLine($"  - {t.Hostname} · {cpu} · {ram}{temp}");
                }
                else if (!string.IsNullOrWhiteSpace(t.Error))
                {
                    sb.AppendLine($"  - {t.Error}");
                }
            }

            return sb.ToString();
        }
    }
}