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

        // ── Auto-restart watchdog state ────────────────────────────────────────
        // Bumped whenever a tunnel process is intentionally stopped or replaced so stale
        // Exited handlers can never trigger a spurious restart.
        private static int _generation;
        // True when a tunnel is expected to be up (started successfully or being resurrected).
        private static volatile bool _expectedRunning;
        // Single-flight gate preventing overlapping cloudflared processes.
        private static readonly SemaphoreSlim _startLock = new(1, 1);
        private static int _autoRestartLoopActive;
        private static int _restartAttempts;
        private static bool _watchdogInitialized;
        private static readonly object _initLock = new();

        public static event Action<bool, string?>? OnTunnelStatusChanged;
        public static bool IsStarting { get; private set; }
        public static string Status => IsRunning ? "online" : (IsStarting ? "starting" : "stopped");

        /// <summary>Whether automatic recreation of the Quick Tunnel is enabled in settings.</summary>
        public static bool AutoRestartEnabled => ConfigService.Current?.HttpServer?.AutoRestartTunnel != false;

        /// <summary>True when the watchdog considers the tunnel expected to be up (i.e. not user-stopped).</summary>
        public static bool IsAutoManaged => _expectedRunning;

        /// <summary>
        /// Wires up the watchdog: recreates the Quick Tunnel whenever internet connectivity is
        /// restored and resurrects the previous session's tunnel on app launch. Safe to call twice.
        /// </summary>
        public static void Initialize()
        {
            lock (_initLock)
            {
                if (_watchdogInitialized) return;
                _watchdogInitialized = true;
            }

            NetworkConnectivityService.OnConnectivityChanged += HandleConnectivityChanged;

            try
            {
                // Auto-start on launch. Previously this only fired when a persisted
                // quick tunnel URL existed, which meant the tunnel never came up on a
                // fresh install or after the URL had been cleared -- you had to press
                // "Start Quick Tunnel" by hand every launch. AutoRestartEnabled is the
                // setting that already documents this intent.
                if (AutoRestartEnabled)
                {
                    var persistedUrl = ConfigService.Current?.HttpServer?.CloudflareUrl;
                    var hadExistingTunnel = !string.IsNullOrEmpty(persistedUrl);

                    _expectedRunning = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(5000);
                            if (_expectedRunning && !IsRunning && !IsStarting)
                            {
                                LogService.WriteSystemLog(
                                    hadExistingTunnel
                                        ? "[CloudflareTunnel] Recreating Quick Tunnel from previous session..."
                                        : "[CloudflareTunnel] Auto-starting Quick Tunnel on app launch...",
                                    "Information", "SYSTEM");
                                var (ok, _, _) = await StartQuickTunnelAsync();
                                if (!ok) ScheduleAutoRestart();
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.WriteSystemLog($"[CloudflareTunnel] Launch task error: {ex.Message}", "Warning", "SYSTEM");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[CloudflareTunnel] Launch start failed: {ex.Message}", "Warning", "SYSTEM");
            }
        }

        private static void HandleConnectivityChanged(bool online)
        {
            if (!online) return;
            if (!AutoRestartEnabled || !_expectedRunning || IsRunning || IsStarting) return;

            LogService.WriteSystemLog("[CloudflareTunnel] Network connection restored. Recreating Cloudflare Quick Tunnel...", "Information", "SYSTEM");
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000);
                    if (_expectedRunning && !IsRunning && !IsStarting)
                    {
                        var (ok, _, _) = await StartQuickTunnelAsync();
                        if (!ok) ScheduleAutoRestart();
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[CloudflareTunnel] Connectivity recovery task error: {ex.Message}", "Warning", "SYSTEM");
                }
            });
        }

        /// <summary>
        /// Force-recreates the Quick Tunnel (issues a fresh trycloudflare.com URL).
        /// Used by the dashboard and by the iOS app over Tailscale when the tunnel died.
        /// </summary>
        public static async Task<(bool success, string? url, string? error)> RestartQuickTunnelAsync(int localPort = 0)
        {
            LogService.WriteSystemLog("[CloudflareTunnel] Restart requested. Recreating Quick Tunnel...", "Information", "SYSTEM");
            _expectedRunning = true;
            StopCore();
            var result = await StartQuickTunnelAsync(localPort);
            if (!result.success) ScheduleAutoRestart();
            return result;
        }

        /// <summary>
        /// Restarts the tunnel with exponential backoff while it is expected to be running.
        /// Covers crashed cloudflared processes and failed start attempts while offline.
        /// </summary>
        private static void ScheduleAutoRestart()
        {
            if (!AutoRestartEnabled || !_expectedRunning) return;
            if (Interlocked.Exchange(ref _autoRestartLoopActive, 1) == 1) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (_expectedRunning && AutoRestartEnabled && !IsRunning && !IsStarting)
                    {
                        int attempt = Interlocked.Increment(ref _restartAttempts);
                        int delaySeconds = Math.Min(60, 5 * (1 << Math.Min(attempt - 1, 4))); // 5, 10, 20, 40, 60...
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

                        if (!_expectedRunning || IsRunning || IsStarting) break;

                        LogService.WriteSystemLog($"[CloudflareTunnel] Auto-restart attempt {attempt} after {delaySeconds}s backoff...", "Information", "SYSTEM");
                        var (ok, _, _) = await StartQuickTunnelAsync();
                        if (ok)
                        {
                            LogService.WriteSystemLog("[CloudflareTunnel] Quick Tunnel auto-restarted successfully.", "Information", "SYSTEM");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[CloudflareTunnel] Auto-restart loop error: {ex.Message}", "Warning", "SYSTEM");
                }
                finally
                {
                    Interlocked.Exchange(ref _autoRestartLoopActive, 0);
                }
            });
        }

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

            // Single-flight gate: watchdog, connectivity-restored hook, dashboard button and the
            // iOS /api/tunnel/quick/restart call can never spawn overlapping cloudflared processes.
            if (!await _startLock.WaitAsync(TimeSpan.FromSeconds(45)))
            {
                lock (_lock)
                {
                    if (IsRunning) return (true, _activeUrl, null);
                }
                return (false, null, "Another tunnel start attempt is already in progress.");
            }

            try
            {
                IsStarting = true;
                return await StartQuickTunnelCoreAsync(localPort);
            }
            finally
            {
                IsStarting = false;
                _startLock.Release();
            }
        }

        private static async Task<(bool success, string? url, string? error)> StartQuickTunnelCoreAsync(int localPort)
        {
            if (localPort <= 0)
            {
                localPort = ConfigService.Current.HttpServer?.Port ?? 8080;
            }

            var binary = await EnsureCloudflaredAsync();
            if (string.IsNullOrEmpty(binary))
            {
                return (false, null, _lastError ?? "cloudflared binary not found and could not be downloaded.");
            }

            // Invalidate exit handlers of any previous tunnel instance before replacing it.
            StopCore();

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

                // Generation guard: intentionally stopped/replaced processes must never
                // trigger the auto-restart watchdog.
                int handlerGeneration = Volatile.Read(ref _generation);

                proc.Exited += (s, e) =>
                {
                    if (handlerGeneration != Volatile.Read(ref _generation))
                    {
                        return; // superseded by an intentional stop or a newer tunnel instance
                    }

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
                        LogService.WriteSystemLog("[CloudflareTunnel] Quick Tunnel process exited unexpectedly. Auto-restart watchdog engaged.", "Warning", "SYSTEM");
                    }
                    ScheduleAutoRestart();
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

                    // The tunnel is healthy again: re-arm the watchdog for future failures.
                    _expectedRunning = true;
                    Interlocked.Exchange(ref _restartAttempts, 0);

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
                    StopCore();
                    IsStarting = false;
                    return (false, null, _lastError ?? "Timed out waiting for Cloudflare trycloudflare.com URL.");
                }
            }
            catch (Exception ex)
            {
                StopCore();
                IsStarting = false;
                _lastError = ex.Message;
                LogService.WriteSystemLog($"[CloudflareTunnel] Start failed: {ex.Message}", "Error", "SYSTEM");
                return (false, null, ex.Message);
            }
        }

        public static void StopQuickTunnel()
        {
            // Explicit stop (dashboard/API): disengage the auto-restart watchdog and clear the
            // persisted quick tunnel URL so launch-time resurrection does not revive it.
            _expectedRunning = false;
            _restartAttempts = 0;
            try
            {
                if (ConfigService.Current?.HttpServer != null && !string.IsNullOrEmpty(ConfigService.Current.HttpServer.CloudflareUrl))
                {
                    ConfigService.Current.HttpServer.CloudflareUrl = "";
                    ConfigService.Save();
                }
            }
            catch { }

            StopCore();
        }

        /// <summary>
        /// Stops the tunnel process without touching watchdog state (used by internal restarts),
        /// and invalidates the current process's exit handler so the kill never looks like a crash.
        /// </summary>
        private static void StopCore()
        {
            Interlocked.Increment(ref _generation);

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
