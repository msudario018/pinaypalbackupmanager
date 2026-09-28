using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class CloudflareTunnelService
    {
        private static Process? _tunnelProcess;
        private static readonly object _lock = new();
        private static string? _activeUrl;
        private static DateTime? _startedAt;
        private static string? _lastError;
        private static readonly Regex TunnelUrlRegex = new(@"https://[a-zA-Z0-9-]+\.trycloudflare\.com", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static event Action<bool, string?>? OnTunnelStatusChanged;
        public static bool IsStarting { get; private set; }
        public static string Status => IsRunning ? "online" : (IsStarting ? "starting" : "stopped");

        public static Task StopTunnelAsync()
        {
            StopQuickTunnel();
            return Task.CompletedTask;
        }

        public static bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    return _tunnelProcess != null && !_tunnelProcess.HasExited && !string.IsNullOrEmpty(_activeUrl);
                }
            }
        }

        public static string? ActiveUrl
        {
            get
            {
                lock (_lock)
                {
                    return _activeUrl;
                }
            }
        }

        public static DateTime? StartedAt
        {
            get
            {
                lock (_lock)
                {
                    return _startedAt;
                }
            }
        }

        public static string? LastError
        {
            get
            {
                lock (_lock)
                {
                    return _lastError;
                }
            }
        }

        public static string? FindCloudflaredBinary()
        {
            // 1. Check local AppData folder
            var appDataBin = AppDataPaths.GetDataPath("cloudflared.exe");
            if (File.Exists(appDataBin)) return appDataBin;

            // 2. Check base directory
            var baseDirBin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudflared.exe");
            if (File.Exists(baseDirBin)) return baseDirBin;

            // 3. Check common Windows installation paths
            string[] commonPaths =
            {
                @"C:\Program Files\cloudflared\cloudflared.exe",
                @"C:\Program Files (x86)\cloudflared\cloudflared.exe",
                @"C:\cloudflared\cloudflared.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "cloudflared.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links\cloudflared.exe")
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path)) return path;
            }

            // 4. Check PATH environment variable
            var envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                var paths = envPath.Split(Path.PathSeparator);
                foreach (var p in paths)
                {
                    try
                    {
                        var candidate = Path.Combine(p.Trim(), "cloudflared.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }

            return null;
        }

        public static async Task<string?> EnsureCloudflaredAsync()
        {
            var binary = FindCloudflaredBinary();
            if (!string.IsNullOrEmpty(binary)) return binary;

            // Auto-download cloudflared standalone binary
            var targetPath = AppDataPaths.GetDataPath("cloudflared.exe");
            try
            {
                LogService.WriteSystemLog("[CloudflareTunnel] Downloading cloudflared.exe...", "Information", "SYSTEM");
                const string downloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromMinutes(2);
                var bytes = await http.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(targetPath, bytes);
                LogService.WriteSystemLog("[CloudflareTunnel] cloudflared.exe downloaded successfully", "Information", "SYSTEM");
                return targetPath;
            }
            catch (Exception ex)
            {
                _lastError = $"Failed to download cloudflared: {ex.Message}";
                LogService.WriteSystemLog($"[CloudflareTunnel] Download failed: {ex.Message}", "Error", "SYSTEM");
                return null;
            }
        }

        public static async Task<(bool success, string? url, string? error)> StartQuickTunnelAsync(int localPort = 0)
        {
            lock (_lock)
            {
                if (IsRunning)
                {
                    return (true, _activeUrl, null);
                }
            }

            if (localPort <= 0)
            {
                localPort = ConfigService.Current.HttpServer?.Port ?? 8080;
            }

            var binary = await EnsureCloudflaredAsync();
            if (string.IsNullOrEmpty(binary))
            {
                return (false, null, _lastError ?? "cloudflared binary not found and could not be downloaded.");
            }

            StopQuickTunnel();

            var tcs = new TaskCompletionSource<string?>();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            cts.Token.Register(() => tcs.TrySetResult(null));

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = binary,
                    Arguments = $"tunnel --url http://localhost:{localPort} --http-host-header localhost",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                DataReceivedEventHandler lineHandler = (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    var match = TunnelUrlRegex.Match(e.Data);
                    if (match.Success)
                    {
                        var url = match.Value.Trim();
                        tcs.TrySetResult(url);
                    }

                    if (e.Data.Contains("ERR", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        _lastError = e.Data;
                    }
                };

                proc.OutputDataReceived += lineHandler;
                proc.ErrorDataReceived += lineHandler;

                proc.Exited += (s, e) =>
                {
                    bool wasRunning = false;
                    lock (_lock)
                    {
                        wasRunning = !string.IsNullOrEmpty(_activeUrl);
                        _activeUrl = null;
                        _startedAt = null;
                        _tunnelProcess = null;
                    }
                    OnTunnelStatusChanged?.Invoke(false, null);
                    tcs.TrySetResult(null);
                    if (wasRunning)
                    {
                        NotificationService.SendDisconnectAlertEmail("Cloudflare Quick Tunnel (trycloudflare.com) disconnected.");
                    }
                };

                if (!proc.Start())
                {
                    return (false, null, "Failed to launch cloudflared process.");
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                lock (_lock)
                {
                    _tunnelProcess = proc;
                }

                var discoveredUrl = await tcs.Task;

                if (!string.IsNullOrEmpty(discoveredUrl))
                {
                    lock (_lock)
                    {
                        _activeUrl = discoveredUrl;
                        _startedAt = DateTime.UtcNow;
                        _lastError = null;
                    }

                    // Save to server config
                    try
                    {
                        if (ConfigService.Current?.HttpServer != null)
                        {
                            ConfigService.Current.HttpServer.CloudflareUrl = discoveredUrl;
                            ConfigService.Save();
                        }
                    }
                    catch { }

                    LogService.WriteSystemLog($"[CloudflareTunnel] Quick Tunnel online: {discoveredUrl}", "Information", "SYSTEM");
                    OnTunnelStatusChanged?.Invoke(true, discoveredUrl);
                    IsStarting = false;
                    return (true, discoveredUrl, null);
                }
                else
                {
                    StopQuickTunnel();
                    IsStarting = false;
                    return (false, null, _lastError ?? "Timed out waiting for Cloudflare trycloudflare.com URL.");
                }
            }
            catch (Exception ex)
            {
                StopQuickTunnel();
                IsStarting = false;
                _lastError = ex.Message;
                LogService.WriteSystemLog($"[CloudflareTunnel] Start failed: {ex.Message}", "Error", "SYSTEM");
                return (false, null, ex.Message);
            }
        }

        public static void StopQuickTunnel()
        {
            lock (_lock)
            {
                if (_tunnelProcess != null)
                {
                    try
                    {
                        if (!_tunnelProcess.HasExited)
                        {
                            _tunnelProcess.Kill(true);
                            _tunnelProcess.WaitForExit(3000);
                        }
                    }
                    catch { }
                    finally
                    {
                        _tunnelProcess.Dispose();
                        _tunnelProcess = null;
                    }
                }

                _activeUrl = null;
                _startedAt = null;
            }

            OnTunnelStatusChanged?.Invoke(false, null);
            LogService.WriteSystemLog("[CloudflareTunnel] Quick Tunnel stopped.", "Information", "SYSTEM");
        }
    }
}
