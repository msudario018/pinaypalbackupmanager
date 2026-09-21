using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;
using QRCoder;

namespace PinayPalBackupManager.Services
{
    public static class WebDashboardService
    {
        /// <summary>Provided by the desktop shell so remote emergency-stop requests cancel real work.</summary>
        public static Action? EmergencyStopExecutor { get; set; }

        public static async Task HandleWebRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var path = request.Url?.AbsolutePath ?? "/";

            // CORS headers for API access
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            // Public endpoints that do not require auth check:
            bool isPublicEndpoint = path == "/api/ping" || path == "/api/user-login" || path == "/login";

            // PIN or Session Token Authentication check if enabled
            if (!isPublicEndpoint && ConfigService.Current.HttpServer.RequireAuth && !string.IsNullOrWhiteSpace(ConfigService.Current.HttpServer.WebPin))
            {
                if (!IsAuthorized(request))
                {
                    if (path.StartsWith("/api/"))
                    {
                        await SendJsonAsync(response, 401, new { error = "Unauthorized. Please authenticate first." });
                        return;
                    }

                    await ServeLoginHtmlAsync(response);
                    return;
                }
            }

            try
            {
                if (path == "/" || path == "/index.html" || path == "/dashboard")
                {
                    await ServeDashboardHtmlAsync(response);
                }
                else if (path == "/api/ping")
                {
                    var localIp = GetLocalIpAddress();
                    await SendJsonAsync(response, 200, new
                    {
                        appName = "PinayPal Backup Manager",
                        version = "3.3.4",
                        status = "online",
                        hostname = Environment.MachineName,
                        localIp = localIp,
                        port = ConfigService.Current.HttpServer.Port,
                        hasUsers = AuthService.HasAnyUsers(),
                        requireAuth = ConfigService.Current.HttpServer.RequireAuth,
                        serverTime = DateTime.UtcNow.ToString("o")
                    });
                }
                else if (path == "/api/user-login" && request.HttpMethod == "POST")
                {
                    await HandleUserLoginPostAsync(context);
                }
                else if (path == "/api/user-logout" && request.HttpMethod == "POST")
                {
                    await HandleUserLogoutPostAsync(context);
                }
                else if (path == "/login" && request.HttpMethod == "POST")
                {
                    await HandleLoginPostAsync(context);
                }
                else if (path == "/api/status")
                {
                    await ServeStatusApiAsync(response);
                }
                else if (path == "/api/health")
                {
                    await ServeHealthApiAsync(response);
                }
                else if (path == "/api/history")
                {
                    await ServeHistoryApiAsync(response);
                }
                else if (path == "/api/logs")
                {
                    await ServeLogsApiAsync(response);
                }
                else if (path.StartsWith("/api/backup/") && request.HttpMethod == "POST")
                {
                    var service = path.Substring("/api/backup/".Length).Trim().ToLowerInvariant();
                    await HandleBackupTriggerAsync(response, service);
                }
                else if (path == "/api/health/run" && request.HttpMethod == "POST")
                {
                    var result = await HealthCheckService.RunHealthCheckAsync();
                    await SendJsonAsync(response, 200, result);
                }
                else if (path == "/api/settings")
                {
                    if (request.HttpMethod == "GET")
                    {
                        await ServeSettingsApiAsync(response);
                    }
                    else if (request.HttpMethod == "POST")
                    {
                        await HandleSettingsPostAsync(context);
                    }
                    else
                    {
                        await SendJsonAsync(response, 405, new { error = "Method not allowed" });
                    }
                }
                else if (path == "/api/emergency-stop" && request.HttpMethod == "POST")
                {
                    await HandleEmergencyStopAsync(response);
                }
                else if (path == "/api/connection-qr")
                {
                    await ServeConnectionQrAsync(response);
                }
                else if (path == "/api/active-backup")
                {
                    await ServeActiveBackupStatusAsync(response);
                }
                else if (path == "/api/network/enable-lan" && request.HttpMethod == "POST")
                {
                    var port = ConfigService.Current.HttpServer.Port > 0 ? ConfigService.Current.HttpServer.Port : 8080;
                    var (success, msg) = await NetworkAccessHelper.ConfigureLanAccessAsync(port);
                    if (success)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(1000);
                            await FileDownloadService.RestartAsync();
                        });
                    }
                    await SendJsonAsync(response, 200, new { success, message = msg });
                }
                else
                {
                    await SendJsonAsync(response, 404, new { error = "Not found" });
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[WebDashboard] Error handling {path}: {ex.Message}", "Error", "SYSTEM");
                await SendJsonAsync(response, 500, new { error = ex.Message });
            }
        }

        private static readonly ConcurrentDictionary<string, (AppUser User, DateTime Expiry)> _activeSessions = new();

        private static bool IsAuthorized(HttpListenerRequest request)
        {
            var expectedPin = ConfigService.Current.HttpServer.WebPin?.Trim() ?? "";

            // Check Authorization header: Bearer <token_or_pin>
            var authHeader = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(authHeader))
            {
                var token = authHeader.Replace("Bearer ", "").Trim();
                if (_activeSessions.TryGetValue(token, out var sess))
                {
                    if (DateTime.UtcNow < sess.Expiry) return true;
                    _activeSessions.TryRemove(token, out _);
                }
                if (!string.IsNullOrEmpty(expectedPin) && token == expectedPin) return true;
            }

            // Check query parameter ?pin=... or ?token=...
            var pinQuery = request.QueryString["pin"];
            if (!string.IsNullOrEmpty(pinQuery) && !string.IsNullOrEmpty(expectedPin) && pinQuery == expectedPin) return true;

            var tokenQuery = request.QueryString["token"];
            if (!string.IsNullOrEmpty(tokenQuery) && _activeSessions.TryGetValue(tokenQuery, out var qSess) && DateTime.UtcNow < qSess.Expiry)
            {
                return true;
            }

            // Check Cookie pp_token
            var tokenCookie = request.Cookies["pp_token"];
            if (tokenCookie != null && _activeSessions.TryGetValue(tokenCookie.Value, out var cSess) && DateTime.UtcNow < cSess.Expiry)
            {
                return true;
            }

            // Check Cookie pp_pin
            var cookie = request.Cookies["pp_pin"];
            if (cookie != null && !string.IsNullOrEmpty(expectedPin) && cookie.Value == expectedPin) return true;

            if (string.IsNullOrEmpty(expectedPin) && !ConfigService.Current.HttpServer.RequireAuth) return true;

            return false;
        }

        private static async Task HandleUserLoginPostAsync(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync();

            string username = "";
            string password = "";

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("username", out var uElem))
                    username = uElem.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("password", out var pElem))
                    password = pElem.GetString() ?? "";
            }
            catch
            {
                await SendJsonAsync(context.Response, 400, new { success = false, message = "Invalid JSON payload" });
                return;
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                await SendJsonAsync(context.Response, 400, new { success = false, message = "Username and password are required" });
                return;
            }

            var (success, user, message) = await AuthService.VerifyCredentialsAsync(username, password);
            if (!success || user == null)
            {
                await SendJsonAsync(context.Response, 401, new { success = false, message });
                return;
            }

            // Generate session token (valid 30 days)
            var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            _activeSessions[sessionToken] = (user, DateTime.UtcNow.AddDays(30));

            context.Response.AppendCookie(new Cookie("pp_token", sessionToken, "/") { Expires = DateTime.Now.AddDays(30) });

            LogService.WriteSystemLog($"[WebDashboard] User '{user.Username}' authenticated successfully via remote API", "Information", "SYSTEM");

            await SendJsonAsync(context.Response, 200, new
            {
                success = true,
                token = sessionToken,
                message = "Authenticated successfully",
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email ?? "",
                    role = user.Role,
                    fullName = user.Username,
                    avatarUrl = ""
                }
            });
        }

        private static async Task HandleUserLogoutPostAsync(HttpListenerContext context)
        {
            var authHeader = context.Request.Headers["Authorization"]?.Replace("Bearer ", "").Trim();
            var cookieToken = context.Request.Cookies["pp_token"]?.Value;

            if (!string.IsNullOrEmpty(authHeader)) _activeSessions.TryRemove(authHeader, out _);
            if (!string.IsNullOrEmpty(cookieToken)) _activeSessions.TryRemove(cookieToken, out _);

            context.Response.AppendCookie(new Cookie("pp_token", "", "/") { Expires = DateTime.Now.AddDays(-1) });
            await SendJsonAsync(context.Response, 200, new { success = true, message = "Logged out successfully" });
        }

        private static string GetLocalIpAddress()
        {
            return FileDownloadService.GetLocalIpAddress();
        }

        private static object ComputeServiceFreshness(string serviceName, string folderPath, int thresholdHours = 24)
        {
            try
            {
                var history = BackupHistoryService.GetHistory();
                var lastSuccess = history?
                    .Where(h => h.Service.Equals(serviceName, StringComparison.OrdinalIgnoreCase) &&
                                (h.Status.Equals("Success", StringComparison.OrdinalIgnoreCase) || h.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(h => h.Timestamp)
                    .FirstOrDefault();

                DateTime? lastBackupTimeUtc = lastSuccess?.Timestamp;

                // Fallback: check newest modified file in backup folder
                if (lastBackupTimeUtc == null && !string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    try
                    {
                        var dir = new DirectoryInfo(folderPath);
                        var newestFile = dir.GetFiles("*", SearchOption.AllDirectories)
                            .Where(f => !f.Name.Equals("backuplog.txt", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault();

                        if (newestFile != null)
                        {
                            lastBackupTimeUtc = newestFile.LastWriteTimeUtc;
                        }
                    }
                    catch { }
                }

                if (lastBackupTimeUtc.HasValue)
                {
                    var age = DateTime.UtcNow - lastBackupTimeUtc.Value;
                    var ageHours = Math.Max(0, age.TotalHours);
                    var isOutdated = ageHours > thresholdHours;

                    string relStr;
                    if (age.TotalMinutes < 2) relStr = "Just now";
                    else if (age.TotalMinutes < 60) relStr = $"{(int)age.TotalMinutes}m ago";
                    else if (age.TotalHours < 24) relStr = $"{(int)age.TotalHours}h ago";
                    else if (age.TotalDays < 30) relStr = $"{(int)age.TotalDays}d ago";
                    else relStr = $"{(int)(age.TotalDays / 30)}mo ago";

                    return new
                    {
                        status = isOutdated ? "outdated" : "updated",
                        isOutdated = isOutdated,
                        isUpdated = !isOutdated,
                        badgeText = isOutdated ? $"Outdated ({relStr})" : $"Updated ({relStr})",
                        lastBackupTime = lastBackupTimeUtc.Value.ToString("o"),
                        relativeTime = relStr,
                        ageHours = Math.Round(ageHours, 1),
                        thresholdHours = thresholdHours
                    };
                }

                return new
                {
                    status = "never",
                    isOutdated = true,
                    isUpdated = false,
                    badgeText = "Never Backed Up",
                    lastBackupTime = (string?)null,
                    relativeTime = "Never",
                    ageHours = -1.0,
                    thresholdHours = thresholdHours
                };
            }
            catch
            {
                return new
                {
                    status = "never",
                    isOutdated = true,
                    isUpdated = false,
                    badgeText = "Unknown",
                    lastBackupTime = (string?)null,
                    relativeTime = "Unknown",
                    ageHours = -1.0,
                    thresholdHours = thresholdHours
                };
            }
        }

        private static async Task HandleLoginPostAsync(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            var expectedPin = ConfigService.Current.HttpServer.WebPin.Trim();

            string pin = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("pin", out var pinElem))
                    pin = pinElem.GetString() ?? "";
            }
            catch
            {
                pin = body.Replace("pin=", "").Trim();
            }

            if (pin == expectedPin)
            {
                context.Response.AppendCookie(new Cookie("pp_pin", pin, "/") { Expires = DateTime.Now.AddDays(7) });
                await SendJsonAsync(context.Response, 200, new { success = true });
            }
            else
            {
                await SendJsonAsync(context.Response, 401, new { success = false, message = "Invalid PIN" });
            }
        }

        private static async Task HandleBackupTriggerAsync(HttpListenerResponse response, string service)
        {
            LogService.WriteSystemLog($"[WebDashboard] Remote backup triggered for service: {service}", "Information", "SYSTEM");

            if (service is not ("ftp" or "sql" or "mailchimp" or "all"))
            {
                await SendJsonAsync(response, 400, new { success = false, message = "Unknown backup service." });
                return;
            }
            
            if (BackupSchedulingService.BackupExecutor != null)
            {
                if (!BackupStateTracker.TrySetRunning(service, $"Queueing {service.ToUpperInvariant()} backup..."))
                {
                    var active = BackupStateTracker.CurrentState;
                    await SendJsonAsync(response, 409, new
                    {
                        success = false,
                        message = $"A {active.Service.ToUpperInvariant()} backup is already in progress.",
                        activeBackup = active
                    });
                    return;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await BackupSchedulingService.BackupExecutor(service, "WEB_DASHBOARD");
                    }
                    catch (Exception ex)
                    {
                        LogService.WriteSystemLog($"[WebDashboard] Backup error for {service}: {ex.Message}", "Error", "SYSTEM");
                    }
                });

                await SendJsonAsync(response, 202, new { success = true, message = $"Backup queued for {service}" });
            }
            else
            {
                await SendJsonAsync(response, 503, new { success = false, message = "Backup engine is currently offline" });
            }
        }

        private static async Task ServeStatusApiAsync(HttpListenerResponse response)
        {
            var health = HealthCheckService.GetLastResult();
            if (health == null)
            {
                health = await HealthCheckService.RunHealthCheckAsync();
            }

            var history = BackupHistoryService.GetHistory();
            var lastSuccess = history?.Where(h => h.Status == "Success").OrderByDescending(h => h.Timestamp).FirstOrDefault();

            string localIp = FileDownloadService.GetLocalIpAddress();

            var sysUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            string sysUptimeStr = sysUptime.Days > 0 ? $"{sysUptime.Days}d {sysUptime.Hours}h {sysUptime.Minutes}m" : $"{sysUptime.Hours}h {sysUptime.Minutes}m";

            string appUptimeStr = "--";
            try
            {
                var procUptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
                appUptimeStr = procUptime.Days > 0 ? $"{procUptime.Days}d {procUptime.Hours}h {procUptime.Minutes}m" : $"{procUptime.Hours}h {procUptime.Minutes}m";
            }
            catch { }

            var sched = ConfigService.Current.Schedule;

            // Per-service folder stats
            var ftpFolderInfo = health.Resources.BackupFolders.FirstOrDefault(f => f.Service.Contains("FTP", StringComparison.OrdinalIgnoreCase));
            var sqlFolderInfo = health.Resources.BackupFolders.FirstOrDefault(f => f.Service.Contains("SQL", StringComparison.OrdinalIgnoreCase));
            var mcFolderInfo = health.Resources.BackupFolders.FirstOrDefault(f => f.Service.Contains("Mailchimp", StringComparison.OrdinalIgnoreCase));

            // Freshness computation (threshold: 24h)
            var ftpFreshness = ComputeServiceFreshness("FTP", BackupConfig.FtpLocalFolder, 24);
            var sqlFreshness = ComputeServiceFreshness("SQL", BackupConfig.SqlLocalFolder, 24);
            var mcFreshness = ComputeServiceFreshness("Mailchimp", BackupConfig.MailchimpFolder, 24);

            var active = BackupStateTracker.CurrentState;
            var status = new
            {
                appName = "PinayPal Backup Manager",
                version = BackupConfig.AppVersion,
                timestamp = DateTime.UtcNow,
                isOnline = true,
                system = new
                {
                    hostname = Environment.MachineName,
                    os = RuntimeInformation.OSDescription,
                    architecture = RuntimeInformation.OSArchitecture.ToString(),
                    cores = Environment.ProcessorCount,
                    systemUptime = sysUptimeStr,
                    appUptime = appUptimeStr,
                    localIp = localIp,
                    allLocalIps = FileDownloadService.GetAllLocalIPv4Addresses(),
                    isBoundToAll = FileDownloadService.IsBoundToAllInterfaces,
                    boundPrefixes = FileDownloadService.BoundPrefixes
                },
                schedules = new
                {
                    ftpDaily = FormatTime12h(sched.FtpDailySyncHourMnl, sched.FtpDailySyncMinuteMnl) + " MNL",
                    sqlDaily = FormatTime12h(sched.SqlDailySyncHourMnl, sched.SqlDailySyncMinuteMnl) + " MNL",
                    mailchimpDaily = FormatTime12h(sched.MailchimpDailySyncHourMnl, sched.MailchimpDailySyncMinuteMnl) + " MNL",
                    healthDaily = FormatTime12h(ConfigService.Current.Operation.DailyHealthCheckHour, 0),
                    ftpInterval = $"{sched.FtpAutoScanHours}h {sched.FtpAutoScanMinutes}m",
                    sqlInterval = $"{sched.SqlAutoScanHours}h {sched.SqlAutoScanMinutes}m",
                    mailchimpInterval = $"{sched.MailchimpAutoScanHours}h {sched.MailchimpAutoScanMinutes}m"
                },
                services = new
                {
                    ftp = new
                    {
                        name = "FTP Website Sync",
                        host = BackupConfig.FtpHost,
                        port = BackupConfig.FtpPort,
                        user = BackupConfig.FtpUser,
                        folder = BackupConfig.FtpLocalFolder,
                        configured = !string.IsNullOrEmpty(BackupConfig.FtpHost),
                        fileCount = ftpFolderInfo?.FileCount ?? 0,
                        sizeBytes = ftpFolderInfo?.TotalSizeBytes ?? 0,
                        freshness = ftpFreshness
                    },
                    sql = new
                    {
                        name = "SQL Database",
                        host = BackupConfig.FtpHost,
                        user = BackupConfig.SqlUser,
                        remotePath = BackupConfig.SqlRemotePath,
                        folder = BackupConfig.SqlLocalFolder,
                        configured = !string.IsNullOrEmpty(BackupConfig.SqlUser),
                        fileCount = sqlFolderInfo?.FileCount ?? 0,
                        sizeBytes = sqlFolderInfo?.TotalSizeBytes ?? 0,
                        freshness = sqlFreshness
                    },
                    mailchimp = new
                    {
                        name = "Mailchimp Sync",
                        audienceId = ConfigService.Current.Mailchimp.AudienceId,
                        folder = BackupConfig.MailchimpFolder,
                        configured = !string.IsNullOrEmpty(BackupConfig.McApiKey),
                        fileCount = mcFolderInfo?.FileCount ?? 0,
                        sizeBytes = mcFolderInfo?.TotalSizeBytes ?? 0,
                        freshness = mcFreshness
                    }
                },
                health = new
                {
                    status = health.Status,
                    isHealthy = health.IsHealthy,
                    lastCheck = health.Timestamp,
                    cpu = health.Resources.CpuUsagePercent,
                    memory = new
                    {
                        percent = health.Resources.MemoryUsagePercent,
                        totalBytes = health.Resources.TotalMemoryBytes,
                        usedBytes = health.Resources.UsedMemoryBytes,
                        availableBytes = health.Resources.AvailableMemoryBytes,
                        appBytes = health.Resources.AppMemoryBytes
                    },
                    disk = new
                    {
                        percent = health.Resources.DiskUsagePercent,
                        primaryDriveLetter = health.Resources.PrimaryDriveLetter,
                        primaryDriveLabel = health.Resources.PrimaryDriveLabel,
                        totalGB = health.Resources.TotalDiskSpaceGB,
                        availableGB = health.Resources.AvailableDiskSpaceGB,
                        usedGB = health.Resources.UsedDiskSpaceGB,
                        backupPath = health.Resources.BackupPath
                    },
                    drives = health.Resources.Drives,
                    backupFolders = health.Resources.BackupFolders,
                    backupPathSizeMB = health.Resources.BackupPathSizeMB
                },
                lastBackup = lastSuccess != null ? new
                {
                    service = lastSuccess.Service,
                    time = lastSuccess.Timestamp,
                    duration = lastSuccess.Duration.TotalSeconds,
                    sizeBytes = lastSuccess.SizeBytes
                } : null,
                activeBackup = new
                {
                    isBusy = active.IsBusy,
                    service = active.Service,
                    statusText = active.StatusText,
                    progress = active.Progress,
                    startedAt = active.StartedAt?.ToString("o"),
                    lastUpdatedAt = active.LastUpdatedAt?.ToString("o"),
                    activeServices = active.ActiveServices.Select(item => new
                    {
                        service = item.Service,
                        statusText = item.StatusText,
                        progress = item.Progress,
                        startedAt = item.StartedAt.ToString("o"),
                        lastUpdatedAt = item.LastUpdatedAt.ToString("o")
                    })
                }
            };

            await SendJsonAsync(response, 200, status);
        }

        private static async Task ServeLogsApiAsync(HttpListenerResponse response)
        {
            var logs = LogService.ImportLatestLogs(AppDataPaths.SystemLogPath, 40);
            logs.Reverse();
            await SendJsonAsync(response, 200, logs);
        }

        private static async Task ServeHealthApiAsync(HttpListenerResponse response)
        {
            var health = HealthCheckService.GetLastResult();
            if (health == null)
            {
                health = await HealthCheckService.RunHealthCheckAsync();
            }
            await SendJsonAsync(response, 200, health);
        }

        private static async Task ServeHistoryApiAsync(HttpListenerResponse response)
        {
            var history = BackupHistoryService.GetHistory()
                .OrderByDescending(h => h.Timestamp)
                .Take(25)
                .Select(h => new
                {
                    id = h.Id,
                    service = h.Service,
                    type = h.Type,
                    status = h.Status,
                    time = h.Timestamp,
                    durationSeconds = h.Duration.TotalSeconds,
                    sizeBytes = h.SizeBytes,
                    filename = Path.GetFileName(h.FilePath),
                    hasFile = !string.IsNullOrEmpty(h.FilePath) && File.Exists(h.FilePath)
                });

            await SendJsonAsync(response, 200, history);
        }

        private static async Task ServeSettingsApiAsync(HttpListenerResponse response)
        {
            var sched = ConfigService.Current.Schedule;
            var op = ConfigService.Current.Operation;

            var data = new
            {
                ftpDailySyncHourMnl = sched.FtpDailySyncHourMnl,
                ftpDailySyncMinuteMnl = sched.FtpDailySyncMinuteMnl,
                sqlDailySyncHourMnl = sched.SqlDailySyncHourMnl,
                sqlDailySyncMinuteMnl = sched.SqlDailySyncMinuteMnl,
                mailchimpDailySyncHourMnl = sched.MailchimpDailySyncHourMnl,
                mailchimpDailySyncMinuteMnl = sched.MailchimpDailySyncMinuteMnl,
                retentionDays = op.RetentionDays,
                dailyHealthCheckEnabled = op.DailyHealthCheckEnabled,
                dailyHealthCheckHour = op.DailyHealthCheckHour,
                autoStartWindows = op.AutoStartWindows,
                notificationSound = op.NotificationSound
            };

            await SendJsonAsync(response, 200, data);
        }

        private static async Task HandleSettingsPostAsync(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(body))
            {
                await SendJsonAsync(context.Response, 400, new { success = false, message = "Empty request payload" });
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var sched = ConfigService.Current.Schedule;
                var op = ConfigService.Current.Operation;
                bool schedChanged = false;
                bool opChanged = false;

                if (root.TryGetProperty("ftpDailySyncHourMnl", out var fH) && fH.TryGetInt32(out int fhVal)) { sched.FtpDailySyncHourMnl = Math.Clamp(fhVal, 0, 23); schedChanged = true; }
                if (root.TryGetProperty("ftpDailySyncMinuteMnl", out var fM) && fM.TryGetInt32(out int fmVal)) { sched.FtpDailySyncMinuteMnl = Math.Clamp(fmVal, 0, 59); schedChanged = true; }
                if (root.TryGetProperty("sqlDailySyncHourMnl", out var sH) && sH.TryGetInt32(out int shVal)) { sched.SqlDailySyncHourMnl = Math.Clamp(shVal, 0, 23); schedChanged = true; }
                if (root.TryGetProperty("sqlDailySyncMinuteMnl", out var sM) && sM.TryGetInt32(out int smVal)) { sched.SqlDailySyncMinuteMnl = Math.Clamp(smVal, 0, 59); schedChanged = true; }
                if (root.TryGetProperty("mailchimpDailySyncHourMnl", out var mH) && mH.TryGetInt32(out int mhVal)) { sched.MailchimpDailySyncHourMnl = Math.Clamp(mhVal, 0, 23); schedChanged = true; }
                if (root.TryGetProperty("mailchimpDailySyncMinuteMnl", out var mM) && mM.TryGetInt32(out int mmVal)) { sched.MailchimpDailySyncMinuteMnl = Math.Clamp(mmVal, 0, 59); schedChanged = true; }

                if (root.TryGetProperty("retentionDays", out var rD) && rD.TryGetInt32(out int rdVal)) { op.RetentionDays = Math.Max(1, rdVal); opChanged = true; }
                if (root.TryGetProperty("dailyHealthCheckEnabled", out var hE)) { op.DailyHealthCheckEnabled = hE.GetBoolean(); opChanged = true; }
                if (root.TryGetProperty("dailyHealthCheckHour", out var hH) && hH.TryGetInt32(out int hhVal)) { op.DailyHealthCheckHour = Math.Clamp(hhVal, 0, 23); opChanged = true; }
                if (root.TryGetProperty("autoStartWindows", out var aS)) { op.AutoStartWindows = aS.GetBoolean(); opChanged = true; }
                if (root.TryGetProperty("notificationSound", out var nS)) { op.NotificationSound = nS.GetBoolean(); opChanged = true; }

                if (schedChanged)
                {
                    ConfigService.SaveSchedule();
                    ConfigService.TriggerScheduleChanged();
                }

                if (opChanged)
                {
                    ConfigService.SaveOperation();
                }

                LogService.WriteSystemLog($"[WebDashboard] Remote settings updated from iOS Dashboard (FTP: {sched.FtpDailySyncHourMnl:D2}:{sched.FtpDailySyncMinuteMnl:D2}, SQL: {sched.SqlDailySyncHourMnl:D2}:{sched.SqlDailySyncMinuteMnl:D2}, MC: {sched.MailchimpDailySyncHourMnl:D2}:{sched.MailchimpDailySyncMinuteMnl:D2}, Retention: {op.RetentionDays}d)", "Information", "SYSTEM");

                _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    NotificationService.ShowBackupToast("Remote Settings Synced", "Schedules & retention updated in real-time from iOS app", "Info");
                });

                await SendJsonAsync(context.Response, 200, new { success = true, message = "Settings updated successfully and synced to desktop app in real-time" });
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[WebDashboard] Error updating remote settings: {ex.Message}", "Error", "SYSTEM");
                await SendJsonAsync(context.Response, 500, new { success = false, error = ex.Message });
            }
        }

        private static async Task HandleEmergencyStopAsync(HttpListenerResponse response)
        {
            LogService.WriteSystemLog("[EMERGENCY] Remote Emergency Stop triggered from iOS Dashboard!", "Warning", "SYSTEM");

            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                EmergencyStopExecutor?.Invoke();
                NotificationService.ShowBackupToast("EMERGENCY STOP", "Emergency stop triggered remotely from iOS app!", "Warning");
            });

            await SendJsonAsync(response, 200, new { success = true, message = "Emergency stop broadcasted to desktop system" });
        }

        // ─── Helpers ────────────────────────────────────────────────────────────────

        private static string FormatTime12h(int hour, int minute)
        {
            var h12 = hour % 12;
            if (h12 == 0) h12 = 12;
            var ampm = hour < 12 ? "AM" : "PM";
            return $"{h12}:{minute:D2} {ampm}";
        }

        // ─── QR Connection Pairing Endpoint ─────────────────────────────────────────

        private static async Task ServeConnectionQrAsync(HttpListenerResponse response)
        {
            try
            {
                var localIp = GetLocalIpAddress();
                var port = ConfigService.Current.HttpServer.Port;
                var pin = ConfigService.Current.HttpServer.WebPin ?? "";
                var cloudflare = ConfigService.Current.HttpServer?.CloudflareUrl ?? "";
                var hostname = Environment.MachineName;

                var allIps = FileDownloadService.GetAllLocalIPv4Addresses();
                var allUrls = allIps.Select(ip => $"http://{ip}:{port}").ToList();

                var payload = JsonSerializer.Serialize(new
                {
                    localUrl = $"http://{localIp}:{port}",
                    allLocalUrls = allUrls,
                    fallbackUrl = cloudflare,
                    pin = pin,
                    hostname = hostname,
                    version = "3.3.4"
                });

                var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
                var pngQr = new PngByteQRCode(data);
                var bytes = pngQr.GetGraphic(8, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, true);

                response.StatusCode = 200;
                response.ContentType = "image/png";
                response.ContentLength64 = bytes.Length;
                response.AddHeader("Cache-Control", "no-cache");
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[WebDashboard] QR generation failed: {ex.Message}", "Error", "SYSTEM");
                await SendJsonAsync(response, 500, new { error = ex.Message });
            }
        }

        // ─── Active Backup Status ────────────────────────────────────────────────────

        private static async Task ServeActiveBackupStatusAsync(HttpListenerResponse response)
        {
            var status = BackupStateTracker.CurrentState;
            await SendJsonAsync(response, 200, new
            {
                isBusy = status.IsBusy,
                service = status.Service,
                statusText = status.StatusText,
                progress = status.Progress,
                startedAt = status.StartedAt?.ToString("o"),
                lastUpdatedAt = status.LastUpdatedAt?.ToString("o"),
                activeServices = status.ActiveServices.Select(item => new
                {
                    service = item.Service,
                    statusText = item.StatusText,
                    progress = item.Progress,
                    startedAt = item.StartedAt.ToString("o"),
                    lastUpdatedAt = item.LastUpdatedAt.ToString("o")
                })
            });
        }

        private static async Task SendJsonAsync(HttpListenerResponse response, int statusCode, object data)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
            var bytes = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }

        private static async Task ServeLoginHtmlAsync(HttpListenerResponse response)
        {
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            var html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>PinayPal Backup Manager - Login</title>
    <script>
        (function() {
            var theme = localStorage.getItem('pinaypal_theme') || (window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
            document.documentElement.setAttribute('data-theme', theme);
        })();
    </script>
    <style>
        :root {
            --bg: #0B0E14; --surface: #161B22; --card: #1B212C; --border: #30363D;
            --text: #F0F6FC; --muted: #8B949E; --gold: #FCA311; --green: #3FB950;
            --inner-bg: #0D1117;
        }
        [data-theme=""light""] {
            --bg: #F4F6F9; --surface: #FFFFFF; --card: #FFFFFF; --border: #E2E8F0;
            --text: #0F172A; --muted: #64748B; --gold: #D97706; --green: #10B981;
            --inner-bg: #F8FAFC;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }
        body { background: var(--bg); color: var(--text); display: flex; align-items: center; justify-content: center; min-height: 100vh; transition: background-color 0.25s, color 0.25s; }
        .card { background: var(--surface); border: 1px solid var(--border); border-radius: 16px; padding: 36px; width: 100%; max-width: 380px; text-align: center; box-shadow: 0 20px 40px rgba(0,0,0,0.4); }
        .logo { font-size: 32px; font-weight: 800; color: var(--gold); margin-bottom: 8px; }
        .sub { color: var(--muted); font-size: 13px; margin-bottom: 28px; }
        input { width: 100%; padding: 14px; background: var(--inner-bg); border: 1px solid var(--border); border-radius: 8px; color: var(--text); font-size: 16px; text-align: center; letter-spacing: 4px; margin-bottom: 20px; outline: none; transition: border 0.2s; }
        input:focus { border-color: var(--gold); }
        button { width: 100%; padding: 14px; background: var(--gold); color: #000; font-weight: 700; border: none; border-radius: 8px; font-size: 14px; cursor: pointer; transition: opacity 0.2s; }
        button:hover { opacity: 0.9; }
        .err { color: #F85149; font-size: 13px; margin-top: 14px; min-height: 18px; }
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""logo"">🛡️ PinayPal</div>
        <div class=""sub"">Web Dashboard Access</div>
        <input type=""password"" id=""pin"" placeholder=""ENTER PIN"" autofocus onkeydown=""if(event.key==='Enter')login()"">
        <button onclick=""login()"">Unlock Dashboard</button>
        <div class=""err"" id=""err""></div>
    </div>
    <script>
        async function login() {
            const pin = document.getElementById('pin').value;
            const err = document.getElementById('err');
            err.textContent = '';
            try {
                const res = await fetch('/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ pin }) });
                const data = await res.json();
                if (data.success) { window.location.reload(); } else { err.textContent = data.message || 'Incorrect PIN'; }
            } catch(e) { err.textContent = 'Server error'; }
        }
    </script>
</body>
</html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }

        private static async Task ServeDashboardHtmlAsync(HttpListenerResponse response)
        {
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            var html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>PinayPal Backup Manager</title>
    <script>
        (function() {
            var theme = localStorage.getItem('pinaypal_theme') || (window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
            document.documentElement.setAttribute('data-theme', theme);
        })();
    </script>
    <style>
        :root {
            --bg: #0B0E14; --surface: #161B22; --card: #1B212C; --border: #30363D;
            --text: #F0F6FC; --muted: #8B949E; --gold: #FCA311; --green: #3FB950;
            --blue: #58A6FF; --cyan: #48CAE4; --purple: #A371F7; --red: #F85149;
            --inner-bg: #0D1117;
        }
        [data-theme=""light""] {
            --bg: #F4F6F9; --surface: #FFFFFF; --card: #FFFFFF; --border: #E2E8F0;
            --text: #0F172A; --muted: #64748B; --gold: #D97706; --green: #10B981;
            --blue: #2563EB; --cyan: #0891B2; --purple: #7C3AED; --red: #DC2626;
            --inner-bg: #F8FAFC;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }
        body { background: var(--bg); color: var(--text); padding: 24px; min-height: 100vh; transition: background-color 0.25s, color 0.25s; }
        .container { max-width: 1260px; margin: 0 auto; }
        
        /* Header */
        header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; padding-bottom: 18px; border-bottom: 1px solid var(--border); flex-wrap: wrap; gap: 14px; }
        .header-left { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
        .logo { font-size: 26px; font-weight: 900; color: var(--gold); letter-spacing: -0.5px; }
        .version-badge { background: #21262D; color: var(--muted); border: 1px solid var(--border); border-radius: 6px; padding: 3px 8px; font-size: 11px; font-weight: 700; }
        .badge-online { background: rgba(63, 185, 80, 0.15); color: var(--green); border: 1px solid rgba(63,185,80,0.3); border-radius: 20px; padding: 4px 12px; font-size: 11px; font-weight: 700; display: inline-flex; align-items: center; gap: 6px; }
        .badge-online::before { content: ''; width: 8px; height: 8px; background: var(--green); border-radius: 50%; display: inline-block; box-shadow: 0 0 8px var(--green); }
        .sys-badge { background: #161B22; border: 1px solid var(--border); border-radius: 20px; padding: 4px 12px; font-size: 11px; color: var(--muted); }
        
        .header-actions { display: flex; gap: 10px; }
        .btn-primary { background: var(--gold); color: #000; border: none; font-weight: 700; border-radius: 8px; padding: 10px 18px; cursor: pointer; transition: all 0.2s; font-size: 13px; }
        .btn-primary:hover { opacity: 0.9; transform: translateY(-1px); }
        .btn-secondary { background: var(--surface); color: var(--text); border: 1px solid var(--border); font-weight: 600; border-radius: 8px; padding: 8px 14px; cursor: pointer; transition: all 0.2s; font-size: 12px; }
        .btn-secondary:hover { border-color: var(--gold); color: var(--gold); }
        
        /* Grids & Cards */
        .grid-3 { display: grid; grid-template-columns: repeat(auto-fit, minmax(330px, 1fr)); gap: 20px; margin-bottom: 24px; }
        .grid-2 { display: grid; grid-template-columns: repeat(auto-fit, minmax(480px, 1fr)); gap: 20px; margin-bottom: 24px; }
        .card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 20px; position: relative; overflow: hidden; }
        .service-card { cursor: pointer; transition: border-color .2s ease, transform .2s ease; }
        .service-card:hover { border-color: var(--gold); transform: translateY(-2px); }
        .service-detail { display: none; margin-top: 14px; padding-top: 14px; border-top: 1px solid var(--border); cursor: default; }
        .service-card.expanded .service-detail { display: block; }
        .service-console { max-height: 180px; overflow: auto; background: #080b10; border-radius: 8px; padding: 10px; font: 11px/1.45 ui-monospace, SFMono-Regular, Menlo, monospace; }
        .service-detail-title { font-size: 11px; text-transform: uppercase; font-weight: 700; color: var(--muted); margin-bottom: 8px; }
        .card::before { content: ''; position: absolute; top: 0; left: 0; right: 0; height: 3px; }
        .card-ftp::before { background: var(--green); }
        .card-sql::before { background: var(--gold); }
        .card-mc::before { background: var(--cyan); }
        .card-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
        .card-title { font-size: 15px; font-weight: 700; }
        .card-meta { font-size: 12px; color: var(--muted); margin-bottom: 14px; min-height: 48px; line-height: 1.5; }
        .card-pills { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 14px; }
        .pill { background: #0D1117; border: 1px solid var(--border); border-radius: 6px; padding: 3px 8px; font-size: 11px; font-weight: 600; }
        .pill-gold { color: var(--gold); border-color: rgba(252,163,17,0.3); }
        .pill-blue { color: var(--blue); border-color: rgba(88,166,255,0.3); }
        .pill-green { color: var(--green); border-color: rgba(63,185,80,0.3); }

        /* Health & Resource Top Cards */
        .health-card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 20px; margin-bottom: 24px; }
        .resources { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px; margin-top: 16px; }
        .res-item { background: var(--card); padding: 16px; border-radius: 8px; border: 1px solid var(--border); display: flex; flex-direction: column; justify-content: space-between; }
        .res-top { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 8px; }
        .res-name { font-size: 11px; color: var(--muted); font-weight: 700; text-transform: uppercase; letter-spacing: 0.5px; }
        .res-target { font-size: 10px; font-weight: 700; background: #0D1117; padding: 2px 6px; border-radius: 4px; border: 1px solid var(--border); }
        .res-val { font-size: 26px; font-weight: 800; color: var(--text); line-height: 1.1; }
        .res-sub { font-size: 12px; color: var(--muted); margin-top: 4px; }
        .res-pills { display: flex; gap: 6px; flex-wrap: wrap; margin-top: 8px; }
        .progress-bar { width: 100%; height: 7px; background: #0D1117; border-radius: 4px; margin-top: 10px; overflow: hidden; }
        .progress-fill { height: 100%; border-radius: 4px; transition: width 0.4s; }

        /* Tables & Detailed Breakdown */
        .table-card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 20px; margin-bottom: 24px; }
        table { width: 100%; border-collapse: collapse; margin-top: 12px; }
        th { text-align: left; padding: 10px 12px; font-size: 11px; color: var(--muted); border-bottom: 1px solid var(--border); text-transform: uppercase; font-weight: 700; }
        td { padding: 12px; font-size: 13px; border-bottom: 1px solid rgba(48, 54, 61, 0.4); }
        .tag { display: inline-block; padding: 3px 8px; border-radius: 6px; font-size: 10px; font-weight: 700; letter-spacing: 0.3px; transition: all 0.25s ease; }
        .tag-success { background: rgba(63,185,80,0.15); color: var(--green); border: 1px solid rgba(63,185,80,0.35); }
        .tag-failed { background: rgba(248,81,73,0.15); color: var(--red); border: 1px solid rgba(248,81,73,0.35); }
        .tag-warning { background: rgba(245,158,11,0.18); color: var(--gold); border: 1px solid rgba(245,158,11,0.45); }
        .tag-neutral { background: rgba(148,163,184,0.12); color: var(--muted); border: 1px solid rgba(148,163,184,0.3); }
        .tag-sys { background: rgba(88,166,255,0.15); color: var(--blue); border: 1px solid rgba(88,166,255,0.3); }
        .tag-backup { background: rgba(252,163,17,0.15); color: var(--gold); border: 1px solid rgba(252,163,17,0.3); }
        .pulse-badge { animation: pulseWarning 2.2s infinite; }
        @keyframes pulseWarning {
            0% { box-shadow: 0 0 0 0 rgba(245, 158, 11, 0.4); }
            70% { box-shadow: 0 0 0 7px rgba(245, 158, 11, 0); }
            100% { box-shadow: 0 0 0 0 rgba(245, 158, 11, 0); }
        }
        .card-outdated-alert { border-color: rgba(245, 158, 11, 0.45) !important; box-shadow: 0 6px 20px rgba(245, 158, 11, 0.08) !important; }
        a.dl { color: var(--blue); text-decoration: none; font-weight: 600; }
        a.dl:hover { text-decoration: underline; }

        /* Terminal Logs Card */
        .term-box { background: #0D1117; border: 1px solid var(--border); border-radius: 8px; padding: 14px; height: 260px; overflow-y: auto; font-family: 'Consolas', 'Courier New', monospace; font-size: 12px; line-height: 1.6; color: #C9D1D9; }
        .term-line { white-space: pre-wrap; word-break: break-all; margin-bottom: 3px; }
        .log-info { color: #58A6FF; }
        .log-warn { color: #E3B341; }
        .log-err { color: #F85149; }
        .log-ok { color: #3FB950; }

        .tip-box { background: rgba(88,166,255,0.08); border: 1px solid rgba(88,166,255,0.25); border-radius: 8px; padding: 12px 14px; font-size: 12px; color: #C9D1D9; line-height: 1.5; margin-top: 14px; }
        .tip-code { background: #0D1117; padding: 2px 6px; border-radius: 4px; font-family: monospace; color: var(--gold); }

        .toast { position: fixed; bottom: 24px; right: 24px; background: var(--card); border: 1px solid var(--gold); color: #FFF; padding: 14px 20px; border-radius: 10px; box-shadow: 0 10px 30px rgba(0,0,0,0.8); display: none; font-weight: 600; z-index: 1000; }

        /* Active Backup Live Banner */
        .active-backup-banner {
            display: none;
            background: linear-gradient(90deg, rgba(252,163,17,0.18), rgba(88,166,255,0.18));
            border: 1px solid var(--gold);
            border-radius: 12px;
            padding: 14px 20px;
            margin-bottom: 20px;
            align-items: center;
            justify-content: space-between;
            animation: pulseGlow 2s infinite ease-in-out;
        }
        @keyframes pulseGlow {
            0%, 100% { box-shadow: 0 0 15px rgba(252,163,17,0.2); }
            50% { box-shadow: 0 0 25px rgba(252,163,17,0.45); }
        }
        .active-pulse-dot {
            width: 10px; height: 10px; background: var(--gold); border-radius: 50%; display: inline-block;
            box-shadow: 0 0 8px var(--gold);
            animation: blinkDot 1s infinite alternate;
        }
        @keyframes blinkDot {
            from { opacity: 0.4; transform: scale(0.85); }
            to { opacity: 1; transform: scale(1.15); }
        }

        /* QR Modal */
        .modal-overlay {
            display: none;
            position: fixed;
            top: 0; left: 0; right: 0; bottom: 0;
            background: rgba(0,0,0,0.75);
            backdrop-filter: blur(8px);
            z-index: 2000;
            align-items: center;
            justify-content: center;
        }
        .modal-card {
            background: var(--surface);
            border: 1px solid var(--gold);
            border-radius: 16px;
            padding: 24px;
            max-width: 440px;
            width: 90%;
            text-align: center;
            box-shadow: 0 20px 50px rgba(0,0,0,0.8);
        }
        .modal-qr-img {
            width: 220px;
            height: 220px;
            background: #fff;
            border-radius: 12px;
            padding: 10px;
            margin: 16px auto;
            display: block;
            box-shadow: 0 4px 15px rgba(0,0,0,0.3);
        }

        /* Mobile / iOS WebView layout. Keep actions large enough for touch and
           prevent desktop tables/cards from forcing horizontal page overflow. */
        @media (max-width: 700px) {
            body { padding: 12px; padding-bottom: calc(20px + env(safe-area-inset-bottom)); }
            .container { max-width: none; }
            header { align-items: stretch; margin-bottom: 16px; padding-bottom: 14px; gap: 12px; }
            .header-left { gap: 8px; }
            .logo { font-size: 22px; }
            .sys-badge { width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
            .header-actions { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); width: 100%; gap: 8px; }
            .header-actions .btn-primary { grid-column: 1 / -1; }
            .btn-primary, .btn-secondary { min-height: 42px; padding: 10px 12px; font-size: 12px; }
            .grid-3, .grid-2 { grid-template-columns: minmax(0, 1fr); gap: 12px; margin-bottom: 16px; }
            .card, .health-card, .table-card { padding: 14px; border-radius: 14px; margin-bottom: 16px; }
            .service-card:hover { transform: none; }
            .card-header { gap: 8px; align-items: flex-start; }
            .card-title { font-size: 14px; }
            .card-meta { min-height: 0; margin-bottom: 12px; }
            .card-actions .btn-secondary { width: 100%; }
            .service-console { max-height: 220px; font-size: 10px; }
            .active-backup-banner { padding: 12px; margin-bottom: 14px; gap: 10px; align-items: stretch; flex-direction: column; }
            .active-backup-banner > div:last-child .btn-secondary { width: 100%; }
            .resources { grid-template-columns: minmax(0, 1fr); gap: 10px; margin-top: 12px; }
            .res-item { padding: 13px; }
            .res-val { font-size: 23px; }
            .table-card { overflow-x: auto; -webkit-overflow-scrolling: touch; }
            table { min-width: 540px; }
            .term-box { height: 220px; padding: 10px; font-size: 11px; }
            .modal-card { width: calc(100% - 24px); padding: 18px 14px; }
            .modal-qr-img { width: min(220px, 72vw); height: min(220px, 72vw); }
            .toast { left: 12px; right: 12px; bottom: calc(12px + env(safe-area-inset-bottom)); text-align: center; }
        }
    </style>
</head>
<body>
    <div class=""container"">
        <!-- Header -->
        <header>
            <div class=""header-left"">
                <div class=""logo"">🛡️ PinayPal</div>
                <span class=""version-badge"" id=""app-version"">v3.3.4</span>
                <div class=""badge-online"">ONLINE</div>
                <div class=""sys-badge"" id=""header-sys-info"">Loading system info...</div>
            </div>
            <div class=""header-actions"">
                <button class=""btn-secondary"" id=""theme-btn"" onclick=""toggleTheme()"">☀️ Light</button>
                <button class=""btn-secondary"" onclick=""openQrModal()"">📱 Pair iOS App</button>
                <button class=""btn-secondary"" onclick=""runHealthCheck()"">⚡ Diagnostics</button>
                <button class=""btn-primary"" onclick=""triggerBackup('all')"">🚀 Run All Backups</button>
            </div>
        </header>

        <!-- Active Backup Realtime Banner -->
        <div class=""active-backup-banner"" id=""active-backup-banner"">
            <div style=""display: flex; align-items: center; gap: 12px;"">
                <span class=""active-pulse-dot""></span>
                <div>
                    <div style=""font-size: 14px; font-weight: 800; color: var(--gold);"">
                        BACKUP IN PROGRESS: <span id=""active-service-name"">--</span>
                    </div>
                    <div style=""font-size: 12px; color: var(--text); margin-top: 2px;"" id=""active-service-status"">
                        Processing sync operation...
                    </div>
                </div>
            </div>
            <div style=""display: flex; gap: 8px;"">
                <button class=""btn-secondary"" style=""border-color: var(--red); color: var(--red); font-size: 11px;"" onclick=""fetch('/api/emergency-stop', {method:'POST'}).then(loadData)"">🛑 Emergency Stop</button>
            </div>
        </div>

        <!-- Global Freshness & LAN Diagnostics Strip -->
        <div style=""display: flex; justify-content: space-between; align-items: center; background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 12px 18px; margin-bottom: 20px; flex-wrap: wrap; gap: 12px;"">
            <div style=""display: flex; align-items: center; gap: 10px;"">
                <span id=""global-freshness-icon"" style=""font-size: 18px;"">🛡️</span>
                <span id=""global-freshness-text"" style=""font-size: 13px; font-weight: 600; color: var(--text);"">Checking service backup freshness...</span>
            </div>
            <div style=""display: flex; align-items: center; gap: 10px;"">
                <div id=""lan-access-container"" style=""display: flex; align-items: center; gap: 6px;"">
                    <span id=""lan-access-status"" class=""tag tag-sys"">IP: Loading...</span>
                </div>
                <button class=""btn-primary"" style=""padding: 7px 14px; font-size: 12px; font-weight: 700;"" onclick=""backupAll()"">⚡ Backup All</button>
            </div>
        </div>

        <!-- Services Cards -->
        <div class=""grid-3"">
            <!-- FTP Website -->
            <div class=""card card-ftp service-card"" onclick=""toggleServiceCard('ftp')"" role=""button"" tabindex=""0"">
                <div class=""card-header"">
                    <div class=""card-title"" style=""color: var(--green)"">🌐 FTP Website Sync</div>
                    <span id=""ftp-badge"" class=""tag tag-success"">READY</span>
                </div>
                <div class=""card-meta"" id=""ftp-meta"">Loading server configuration...</div>
                <div class=""card-pills"">
                    <span class=""pill pill-green"" id=""ftp-storage"">Files: -- | Size: --</span>
                    <span class=""pill pill-blue"" id=""ftp-sched"">Daily: 10:00 PM MNL</span>
                </div>
                <div class=""card-actions"">
                    <button class=""btn-secondary"" onclick=""event.stopPropagation(); triggerBackup('ftp')"">Backup Website</button>
                </div>
                <div class=""service-detail"" id=""ftp-detail""><div class=""service-detail-title"">FTP service console</div><div class=""service-console"" id=""ftp-console"">Loading FTP logs…</div></div>
            </div>

            <!-- SQL Database -->
            <div class=""card card-sql service-card"" onclick=""toggleServiceCard('sql')"" role=""button"" tabindex=""0"">
                <div class=""card-header"">
                    <div class=""card-title"" style=""color: var(--gold)"">🗄️ SQL Database</div>
                    <span id=""sql-badge"" class=""tag tag-success"">READY</span>
                </div>
                <div class=""card-meta"" id=""sql-meta"">Loading database configuration...</div>
                <div class=""card-pills"">
                    <span class=""pill pill-gold"" id=""sql-storage"">Files: -- | Size: --</span>
                    <span class=""pill pill-blue"" id=""sql-sched"">Daily: 05:00 PM MNL</span>
                </div>
                <div class=""card-actions"">
                    <button class=""btn-secondary"" onclick=""event.stopPropagation(); triggerBackup('sql')"">Backup Database</button>
                </div>
                <div class=""service-detail"" id=""sql-detail""><div class=""service-detail-title"">SQL service console</div><div class=""service-console"" id=""sql-console"">Loading SQL logs…</div></div>
            </div>

            <!-- Mailchimp -->
            <div class=""card card-mc service-card"" onclick=""toggleServiceCard('mailchimp')"" role=""button"" tabindex=""0"">
                <div class=""card-header"">
                    <div class=""card-title"" style=""color: var(--cyan)"">🐵 Mailchimp Sync</div>
                    <span id=""mc-badge"" class=""tag tag-success"">READY</span>
                </div>
                <div class=""card-meta"" id=""mc-meta"">Audience, templates, and campaign archives</div>
                <div class=""card-pills"">
                    <span class=""pill pill-blue"" id=""mc-storage"">Files: -- | Size: --</span>
                    <span class=""pill pill-blue"" id=""mc-sched"">Daily: 06:00 PM MNL</span>
                </div>
                <div class=""card-actions"">
                    <button class=""btn-secondary"" onclick=""event.stopPropagation(); triggerBackup('mailchimp')"">Backup Mailchimp</button>
                </div>
                <div class=""service-detail"" id=""mailchimp-detail""><div class=""service-detail-title"">Mailchimp service console</div><div class=""service-console"" id=""mailchimp-console"">Loading Mailchimp logs…</div></div>
            </div>
        </div>

        <!-- Health & Resources Section -->
        <div class=""health-card"">
            <div style=""display: flex; justify-content: space-between; align-items: center;"">
                <div style=""font-weight: 700; font-size: 15px; letter-spacing: 0.5px;"">⚡ SYSTEM HEALTH &amp; HARDWARE METRICS</div>
                <div style=""font-size: 12px; color: var(--muted);"" id=""last-check-text"">Last check: --</div>
            </div>

            <div class=""resources"">
                <!-- Status -->
                <div class=""res-item"">
                    <div class=""res-top"">
                        <div class=""res-name"">Status</div>
                        <span class=""tag tag-success"" id=""health-badge"">OK</span>
                    </div>
                    <div class=""res-val"" id=""health-status"" style=""color: var(--green);"">Healthy</div>
                    <div class=""res-sub"" id=""health-sub"">All components operational</div>
                    <div class=""progress-bar""><div class=""progress-fill"" style=""background: var(--green); width: 100%;""></div></div>
                </div>

                <!-- CPU -->
                <div class=""res-item"">
                    <div class=""res-top"">
                        <div class=""res-name"">CPU Usage</div>
                        <span class=""res-target"" id=""cpu-cores"">-- Cores</span>
                    </div>
                    <div class=""res-val"" id=""cpu-val"">0%</div>
                    <div class=""res-sub"">Processor Load</div>
                    <div class=""progress-bar""><div class=""progress-fill"" id=""cpu-fill"" style=""background: var(--blue); width: 0%;""></div></div>
                </div>

                <!-- Memory -->
                <div class=""res-item"">
                    <div class=""res-top"">
                        <div class=""res-name"">Memory Usage</div>
                        <span class=""res-target"" id=""mem-target"">Physical RAM</span>
                    </div>
                    <div class=""res-val"" id=""mem-val"">0%</div>
                    <div class=""res-sub"" id=""mem-size"">0 GB / 0 GB</div>
                    <div class=""res-pills"">
                        <span class=""pill pill-green"" id=""mem-free"">-- Free</span>
                        <span class=""pill pill-gold"" id=""mem-app"">App: --</span>
                    </div>
                    <div class=""progress-bar""><div class=""progress-fill"" id=""mem-fill"" style=""background: var(--purple); width: 0%;""></div></div>
                </div>

                <!-- Disk -->
                <div class=""res-item"">
                    <div class=""res-top"">
                        <div class=""res-name"">Disk Usage</div>
                        <span class=""res-target"" id=""disk-drive-name"">Drive --</span>
                    </div>
                    <div class=""res-val"" id=""disk-val"">0%</div>
                    <div class=""res-sub"" id=""disk-size"">0 GB Free of 0 GB</div>
                    <div class=""res-pills"">
                        <span class=""pill pill-gold"" id=""disk-label"">Backup Drive</span>
                    </div>
                    <div class=""progress-bar""><div class=""progress-fill"" id=""disk-fill"" style=""background: var(--green); width: 0%;""></div></div>
                </div>
            </div>
        </div>

        <!-- Partitions & Backup Storage Breakdown -->
        <div class=""grid-2"">
            <!-- All Physical Drives -->
            <div class=""table-card"">
                <div style=""display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;"">
                    <div style=""font-weight: 700; font-size: 14px;"">💾 ALL SYSTEM DRIVES &amp; PARTITIONS</div>
                    <span style=""font-size: 11px; color: var(--muted);"" id=""drive-count"">-- Ready</span>
                </div>
                <table>
                    <thead>
                        <tr>
                            <th>Drive</th>
                            <th>Label</th>
                            <th>Free / Total</th>
                            <th>Usage</th>
                        </tr>
                    </thead>
                    <tbody id=""drives-tbody"">
                        <tr><td colspan=""4"" style=""text-align: center; color: var(--muted);"">Scanning system drives...</td></tr>
                    </tbody>
                </table>
            </div>

            <!-- Backup Storage Allocation -->
            <div class=""table-card"">
                <div style=""display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;"">
                    <div style=""font-weight: 700; font-size: 14px;"">📁 PINAYPAL BACKUP STORAGE ALLOCATION</div>
                    <span class=""pill pill-gold"" id=""total-backup-size"">Total: 0 MB</span>
                </div>
                <table>
                    <thead>
                        <tr>
                            <th>Service</th>
                            <th>Path</th>
                            <th>Files</th>
                            <th>Disk Size</th>
                        </tr>
                    </thead>
                    <tbody id=""folders-tbody"">
                        <tr><td colspan=""4"" style=""text-align: center; color: var(--muted);"">Analyzing backup folders...</td></tr>
                    </tbody>
                </table>
            </div>
        </div>

        <!-- Upcoming Schedules & System Specs -->
        <div class=""grid-2"">
            <!-- Automated Schedules -->
            <div class=""table-card"">
                <div style=""font-weight: 700; font-size: 14px; margin-bottom: 12px;"">⏰ AUTOMATED BACKUP SCHEDULES (MANILA TIME)</div>
                <table>
                    <thead>
                        <tr>
                            <th>Service</th>
                            <th>Daily Sync</th>
                            <th>Auto-Scan Interval</th>
                            <th>Status</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr>
                            <td><strong>🌐 Website FTP</strong></td>
                            <td id=""sched-ftp-daily"">22:00 MNL</td>
                            <td id=""sched-ftp-interval"">Every 3h</td>
                            <td><span class=""tag tag-success"">ACTIVE</span></td>
                        </tr>
                        <tr>
                            <td><strong>🗄️ SQL Database</strong></td>
                            <td id=""sched-sql-daily"">17:00 MNL</td>
                            <td id=""sched-sql-interval"">Every 2h 15m</td>
                            <td><span class=""tag tag-success"">ACTIVE</span></td>
                        </tr>
                        <tr>
                            <td><strong>🐵 Mailchimp</strong></td>
                            <td id=""sched-mc-daily"">18:00 MNL</td>
                            <td id=""sched-mc-interval"">Every 2h</td>
                            <td><span class=""tag tag-success"">ACTIVE</span></td>
                        </tr>
                        <tr>
                            <td><strong>🩺 Health Diagnostics</strong></td>
                            <td id=""sched-health-daily"">08:00 AM</td>
                            <td>Daily</td>
                            <td><span class=""tag tag-success"">ACTIVE</span></td>
                        </tr>
                    </tbody>
                </table>
            </div>

            <!-- System Specs & Remote Access -->
            <div class=""table-card"">
                <div style=""font-weight: 700; font-size: 14px; margin-bottom: 12px;"">⚙️ SYSTEM SPECS &amp; REMOTE TUNNEL</div>
                <table>
                    <tbody>
                        <tr>
                            <td style=""color: var(--muted); width: 140px;"">Host / Machine:</td>
                            <td id=""spec-host"">--</td>
                        </tr>
                        <tr>
                            <td style=""color: var(--muted);"">Operating System:</td>
                            <td id=""spec-os"">--</td>
                        </tr>
                        <tr>
                            <td style=""color: var(--muted);"">System Uptime:</td>
                            <td id=""spec-sys-uptime"">--</td>
                        </tr>
                        <tr>
                            <td style=""color: var(--muted);"">App Uptime:</td>
                            <td id=""spec-app-uptime"">--</td>
                        </tr>
                        <tr>
                            <td style=""color: var(--muted);"">Local IP:</td>
                            <td id=""spec-ip"">--</td>
                        </tr>
                    </tbody>
                </table>
                <div class=""tip-box"">
                    <strong>💡 Cloudflare Remote Tunnel Tip:</strong><br>
                    To access remotely without <code>HTTP 400 Invalid Hostname</code> errors, start cloudflared with:<br>
                    <span class=""tip-code"">cloudflared tunnel --url http://localhost:8080 --http-host-header localhost</span>
                </div>
            </div>
        </div>

        <!-- Live Activity Logs Terminal -->
        <div class=""table-card"">
            <div style=""display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;"">
                <div style=""display: flex; align-items: center; gap: 8px; font-weight: 700; font-size: 14px;"">
                    <span>📜 LIVE ACTIVITY &amp; DIAGNOSTIC LOGS</span>
                    <span style=""width: 8px; height: 8px; background: var(--green); border-radius: 50%; display: inline-block; box-shadow: 0 0 6px var(--green);""></span>
                </div>
                <div style=""display: flex; gap: 8px;"">
                    <button class=""btn-secondary"" style=""padding: 4px 10px; font-size: 11px;"" onclick=""toggleLogsPause()"" id=""btn-pause-logs"">⏸ Pause</button>
                    <button class=""btn-secondary"" style=""padding: 4px 10px; font-size: 11px;"" onclick=""loadLogs()"">🔄 Refresh</button>
                </div>
            </div>
            <div class=""term-box"" id=""term-box"">
                <div style=""color: var(--muted);"">Connecting to live log stream...</div>
            </div>
        </div>

        <!-- Backup History -->
        <div class=""table-card"">
            <div style=""font-weight: 700; font-size: 14px; margin-bottom: 8px;"">📜 RECENT BACKUP EXECUTIONS</div>
            <table>
                <thead>
                    <tr>
                        <th>Service</th>
                        <th>Status</th>
                        <th>Date &amp; Time</th>
                        <th>Duration</th>
                        <th>File Size</th>
                        <th>Action</th>
                    </tr>
                </thead>
                <tbody id=""history-rows"">
                    <tr><td colspan=""6"" style=""text-align: center; color: var(--muted); padding: 24px;"">Loading backup history...</td></tr>
                </tbody>
            </table>
        </div>
    </div>
    <!-- Pair iOS App QR Modal -->
    <div class=""modal-overlay"" id=""qr-modal"" onclick=""if(event.target === this) closeQrModal()"">
        <div class=""modal-card"">
            <div style=""font-size: 20px; font-weight: 800; color: var(--gold); margin-bottom: 8px;"">📱 Pair iOS App</div>
            <p style=""font-size: 13px; color: var(--muted); margin-bottom: 12px;"">
                Open <strong>PinayPal Backup</strong> on your iPhone, select <em>Scan QR</em>, and point your camera at this code.
            </p>
            <img id=""qr-modal-img"" class=""modal-qr-img"" src="""" alt=""Pairing QR Code"" />
            <div style=""font-size: 11px; color: var(--muted); margin-top: 10px; word-break: break-all;"">
                Auto-configures your local IP and security PIN in one step.
            </div>
            <div style=""margin-top: 20px;"">
                <button class=""btn-secondary"" style=""width: 100%;"" onclick=""closeQrModal()"">Close</button>
            </div>
        </div>
    </div>

    <div class=""toast"" id=""toast""></div>

    <script>
        let logsPaused = false;
        let latestLogs = [];
        let expandedService = null;

        function toggleServiceCard(service) {
            expandedService = expandedService === service ? null : service;
            ['ftp', 'sql', 'mailchimp'].forEach(key => {
                const card = document.querySelector('.card-' + (key === 'mailchimp' ? 'mc' : key));
                if (card) card.classList.toggle('expanded', expandedService === key);
            });
            renderServiceConsoles();
        }

        function renderServiceConsoles() {
            ['ftp', 'sql', 'mailchimp'].forEach(service => {
                const consoleEl = document.getElementById(service + '-console');
                if (!consoleEl) return;
                const lines = latestLogs.filter(line => line.toLowerCase().includes(service));
                consoleEl.innerHTML = lines.length
                    ? lines.slice(-40).map(line => `<div>${escapeHtml(line)}</div>`).join('')
                    : `<div style=""color:var(--muted)"">No ${service} log entries recorded yet.</div>`;
            });
        }

        function openQrModal() {
            document.getElementById('qr-modal-img').src = '/api/connection-qr?t=' + Date.now();
            document.getElementById('qr-modal').style.display = 'flex';
        }

        function closeQrModal() {
            document.getElementById('qr-modal').style.display = 'none';
        }

        function showToast(msg) {
            const t = document.getElementById('toast');
            t.textContent = msg;
            t.style.display = 'block';
            setTimeout(() => { t.style.display = 'none'; }, 3500);
        }

        function toggleLogsPause() {
            logsPaused = !logsPaused;
            document.getElementById('btn-pause-logs').textContent = logsPaused ? '▶ Resume' : '⏸ Pause';
        }

        function toggleTheme() {
            var cur = document.documentElement.getAttribute('data-theme') || 'dark';
            var next = cur === 'dark' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-theme', next);
            localStorage.setItem('pinaypal_theme', next);
            updateThemeButton(next);
            playChime('subtle');
        }

        function updateThemeButton(theme) {
            var btn = document.getElementById('theme-btn');
            if (btn) btn.textContent = theme === 'dark' ? '☀️ Light' : '🌙 Dark';
        }

        function playChime(type) {
            try {
                var AudioCtx = window.AudioContext || window.webkitAudioContext;
                if (!AudioCtx) return;
                var ctx = new AudioCtx();
                var now = ctx.currentTime;
                var osc = ctx.createOscillator();
                var gain = ctx.createGain();

                osc.type = 'sine';
                if (type === 'error') {
                    osc.frequency.setValueAtTime(320, now);
                    osc.frequency.exponentialRampToValueAtTime(160, now + 0.35);
                    gain.gain.setValueAtTime(0.2, now);
                    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.35);
                } else if (type === 'success') {
                    osc.frequency.setValueAtTime(587.33, now);
                    osc.frequency.setValueAtTime(880, now + 0.1);
                    osc.frequency.setValueAtTime(1174.66, now + 0.2);
                    gain.gain.setValueAtTime(0.18, now);
                    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.5);
                } else {
                    osc.frequency.setValueAtTime(880, now);
                    gain.gain.setValueAtTime(0.1, now);
                    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.15);
                }
                osc.connect(gain);
                gain.connect(ctx.destination);
                osc.start(now);
                osc.stop(now + (type === 'success' ? 0.55 : 0.35));
            } catch(e) {}
        }

        function sendBrowserNotification(title, body) {
            if ('Notification' in window) {
                if (Notification.permission === 'granted') {
                    new Notification(title, { body: body });
                } else if (Notification.permission !== 'denied') {
                    Notification.requestPermission().then(function(p) {
                        if (p === 'granted') new Notification(title, { body: body });
                    });
                }
            }
        }

        async function triggerBackup(service) {
            showToast('Starting backup: ' + service.toUpperCase() + '...');
            playChime('subtle');
            try {
                const res = await fetch('/api/backup/' + service, { method: 'POST' });
                const d = await res.json();
                if (!res.ok) {
                    showToast(d.message || 'Backup could not be started');
                    playChime('error');
                    return false;
                }
                showToast(d.message || 'Backup triggered');
                playChime('success');
                sendBrowserNotification('Backup Triggered: ' + service.toUpperCase(), d.message || 'Backup operation initiated.');
                setTimeout(loadData, 1500);
                setTimeout(loadLogs, 1500);
                return true;
            } catch(e) {
                showToast('Error triggering backup');
                playChime('error');
                return false;
            }
        }

        async function backupAll() {
            showToast('Starting sequential backup for all services (FTP, SQL, Mailchimp)...');
            playChime('subtle');
            try {
                await triggerBackup('ftp');
                setTimeout(() => triggerBackup('sql'), 3500);
                setTimeout(() => triggerBackup('mailchimp'), 7000);
            } catch (e) {
                showToast('Failed to trigger backup all: ' + e.message);
                playChime('error');
            }
        }

        async function enableLanAccess() {
            showToast('Requesting elevated Windows Firewall & URL ACL configuration...');
            playChime('subtle');
            try {
                const res = await fetch('/api/network/enable-lan', { method: 'POST' });
                const d = await res.json();
                if (d.success) {
                    showToast(d.message || 'LAN Access configured! Restarting web server...');
                    playChime('success');
                } else {
                    showToast(d.message || 'Failed to configure LAN access');
                    playChime('error');
                }
                setTimeout(loadData, 3000);
            } catch (e) {
                showToast('Error enabling LAN access: ' + e.message);
                playChime('error');
            }
        }

        async function runHealthCheck() {
            showToast('Running system diagnostics...');
            try {
                const res = await fetch('/api/health/run', { method: 'POST' });
                const d = await res.json();
                showToast('Diagnostics completed: ' + d.status);
                loadData();
            } catch(e) {
                showToast('Error running health check');
            }
        }

        function formatBytes(bytes) {
            if (!bytes || bytes === 0) return '0 B';
            const k = 1024;
            const dm = 1;
            const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
            const i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + ' ' + sizes[i];
        }

        async function loadLogs() {
            if (logsPaused) return;
            try {
                const res = await fetch('/api/logs');
                const logs = await res.json();
                latestLogs.splice(0, latestLogs.length, ...(logs || []));
                const term = document.getElementById('term-box');
                if (logs && logs.length > 0) {
                    term.innerHTML = logs.map(l => {
                        let cls = '';
                        if (l.includes('[ERROR]') || l.includes('FAIL')) cls = 'log-err';
                        else if (l.includes('[WARN]') || l.includes('CANCEL')) cls = 'log-warn';
                        else if (l.includes('[SUCCESS]') || l.includes('COMPLETE')) cls = 'log-ok';
                        else if (l.includes('[INFO]') || l.includes('SYNC')) cls = 'log-info';
                        return `<div class=""term-line ${cls}"">${escapeHtml(l)}</div>`;
                    }).join('');
                    term.scrollTop = term.scrollHeight;
                } else {
                    term.innerHTML = '<div style=""color: var(--muted);"">No logs recorded yet.</div>';
                }
                renderServiceConsoles();
            } catch(e) { }
        }

        function escapeHtml(text) {
            return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        async function loadData() {
            try {
                updateThemeButton(document.documentElement.getAttribute('data-theme') || 'dark');
                const [sRes, hRes] = await Promise.all([
                    fetch('/api/status').then(r => r.json()),
                    fetch('/api/history').then(r => r.json())
                ]);

                // Header & System info
                if (sRes.version) document.getElementById('app-version').textContent = sRes.version;
                if (sRes.system) {
                    document.getElementById('header-sys-info').textContent = `${sRes.system.hostname} | ${sRes.system.cores} Cores | Up: ${sRes.system.systemUptime}`;
                    document.getElementById('spec-host').textContent = sRes.system.hostname;
                    document.getElementById('spec-os').textContent = `${sRes.system.os} (${sRes.system.architecture})`;
                    document.getElementById('spec-sys-uptime').textContent = sRes.system.systemUptime;
                    document.getElementById('spec-app-uptime').textContent = sRes.system.appUptime;
                    document.getElementById('spec-ip').textContent = sRes.system.localIp;
                    document.getElementById('cpu-cores').textContent = `${sRes.system.cores} Cores`;
                }

                // Services & Freshness
                if (sRes.services) {
                    let updatedCount = 0;
                    let outdatedList = [];

                    function applyFreshnessBadge(badgeId, cardClass, serviceObj, serviceLabel) {
                        const badge = document.getElementById(badgeId);
                        const card = document.querySelector('.' + cardClass);
                        const f = serviceObj?.freshness;
                        if (!badge) return;

                        if (!f || f.status === 'never') {
                            badge.className = 'tag tag-neutral';
                            badge.innerHTML = '• NOT BACKED UP';
                            badge.title = 'No completed backup recorded yet';
                            outdatedList.push(serviceLabel + ' (Never)');
                            if (card) card.classList.remove('card-outdated-alert');
                        } else if (f.isOutdated) {
                            badge.className = 'tag tag-warning pulse-badge';
                            badge.innerHTML = `⚠ OUTDATED <span style=""font-size:10px;opacity:0.85"">(${f.relativeTime})</span>`;
                            badge.title = `Overdue! Last backup was ${f.relativeTime} (threshold: ${f.thresholdHours}h)`;
                            outdatedList.push(serviceLabel + ` (${f.relativeTime})`);
                            if (card) card.classList.add('card-outdated-alert');
                        } else {
                            badge.className = 'tag tag-success';
                            badge.innerHTML = `✓ UPDATED <span style=""font-size:10px;opacity:0.85"">(${f.relativeTime})</span>`;
                            badge.title = `Fresh! Last backup completed ${f.relativeTime}`;
                            updatedCount++;
                            if (card) card.classList.remove('card-outdated-alert');
                        }
                    }

                    // FTP
                    const ftp = sRes.services.ftp;
                    document.getElementById('ftp-meta').textContent = `Host: ${ftp.host || 'Not configured'} (Port ${ftp.port}) | User: ${ftp.user || 'None'}\nPath: ${ftp.folder || 'Not configured'}`;
                    document.getElementById('ftp-storage').textContent = `Files: ${ftp.fileCount} | Size: ${formatBytes(ftp.sizeBytes)}`;
                    applyFreshnessBadge('ftp-badge', 'card-ftp', ftp, 'FTP Website');

                    // SQL
                    const sql = sRes.services.sql;
                    document.getElementById('sql-meta').textContent = `User: ${sql.user || 'Not configured'} | Remote: ${sql.remotePath || 'Default'}\nPath: ${sql.folder || 'Not configured'}`;
                    document.getElementById('sql-storage').textContent = `Files: ${sql.fileCount} | Size: ${formatBytes(sql.sizeBytes)}`;
                    applyFreshnessBadge('sql-badge', 'card-sql', sql, 'SQL Database');

                    // Mailchimp
                    const mc = sRes.services.mailchimp;
                    document.getElementById('mc-meta').textContent = `Audience ID: ${mc.audienceId || 'Default'}\nPath: ${mc.folder || 'Not configured'}`;
                    document.getElementById('mc-storage').textContent = `Files: ${mc.fileCount} | Size: ${formatBytes(mc.sizeBytes)}`;
                    applyFreshnessBadge('mc-badge', 'card-mc', mc, 'Mailchimp');

                    // Global Freshness Strip
                    const gIcon = document.getElementById('global-freshness-icon');
                    const gText = document.getElementById('global-freshness-text');
                    if (gIcon && gText) {
                        if (outdatedList.length === 0 && updatedCount > 0) {
                            gIcon.textContent = '✨';
                            gText.innerHTML = `<span style=""color:var(--green)"">All Backups Up to Date:</span> All ${updatedCount} backup services are freshly synchronized.`;
                        } else if (outdatedList.length > 0) {
                            gIcon.textContent = '⚠️';
                            gText.innerHTML = `<span style=""color:var(--gold)"">Attention Required:</span> Outdated backups: <strong>${outdatedList.join(', ')}</strong>.`;
                        } else {
                            gIcon.textContent = '🛡️';
                            gText.textContent = 'No active backup history recorded yet.';
                        }
                    }

                    // LAN Diagnostics Badge
                    const lanStatus = document.getElementById('lan-access-status');
                    const lanContainer = document.getElementById('lan-access-container');
                    if (lanStatus && sRes.system) {
                        const port = window.location.port || 8080;
                        if (sRes.system.isBoundToAll) {
                            lanStatus.className = 'tag tag-success';
                            lanStatus.innerHTML = `🟢 LAN Ready: http://${sRes.system.localIp}:${port}`;
                        } else {
                            lanStatus.className = 'tag tag-sys';
                            lanStatus.innerHTML = `🌐 Local IP: http://${sRes.system.localIp}:${port}`;
                            if (lanContainer && !document.getElementById('btn-fix-lan')) {
                                const btn = document.createElement('button');
                                btn.id = 'btn-fix-lan';
                                btn.className = 'btn-secondary';
                                btn.style.cssText = 'padding:3px 8px;font-size:10px;font-weight:700;border-color:var(--gold);color:var(--gold);margin-left:6px;';
                                btn.textContent = 'Configure Firewall';
                                btn.onclick = enableLanAccess;
                                lanContainer.appendChild(btn);
                            }
                        }
                    }
                }

                // Schedules
                if (sRes.schedules) {
                    document.getElementById('ftp-sched').textContent = `Daily: ${sRes.schedules.ftpDaily}`;
                    document.getElementById('sql-sched').textContent = `Daily: ${sRes.schedules.sqlDaily}`;
                    document.getElementById('mc-sched').textContent = `Daily: ${sRes.schedules.mailchimpDaily}`;

                    document.getElementById('sched-ftp-daily').textContent = sRes.schedules.ftpDaily;
                    document.getElementById('sched-ftp-interval').textContent = `Every ${sRes.schedules.ftpInterval}`;

                    document.getElementById('sched-sql-daily').textContent = sRes.schedules.sqlDaily;
                    document.getElementById('sched-sql-interval').textContent = `Every ${sRes.schedules.sqlInterval}`;

                    document.getElementById('sched-mc-daily').textContent = sRes.schedules.mailchimpDaily;
                    document.getElementById('sched-mc-interval').textContent = `Every ${sRes.schedules.mailchimpInterval}`;

                    document.getElementById('sched-health-daily').textContent = sRes.schedules.healthDaily;
                }

                // Active Backup Banner Realtime Update
                if (sRes.activeBackup && sRes.activeBackup.isBusy) {
                    document.getElementById('active-backup-banner').style.display = 'flex';
                    document.getElementById('active-service-name').textContent = (sRes.activeBackup.service || 'Backup').toUpperCase();
                    document.getElementById('active-service-status').textContent = sRes.activeBackup.statusText || 'In Progress...';
                } else {
                    document.getElementById('active-backup-banner').style.display = 'none';
                }

                // Health & Hardware Metrics
                if (sRes.health) {
                    const h = sRes.health;
                    document.getElementById('health-status').textContent = h.status;
                    document.getElementById('health-status').style.color = h.isHealthy ? 'var(--green)' : 'var(--red)';
                    document.getElementById('health-badge').textContent = h.isHealthy ? 'HEALTHY' : 'DEGRADED';
                    document.getElementById('health-badge').className = `tag ${h.isHealthy ? 'tag-success' : 'tag-failed'}`;
                    document.getElementById('last-check-text').textContent = 'Last check: ' + new Date(h.lastCheck).toLocaleTimeString();

                    // CPU
                    const cpu = Math.round(h.cpu || 0);
                    document.getElementById('cpu-val').textContent = cpu + '%';
                    document.getElementById('cpu-fill').style.width = Math.min(100, Math.max(2, cpu)) + '%';

                    // Memory (Physical RAM)
                    if (h.memory) {
                        const m = h.memory;
                        const memPct = Math.round(m.percent || 0);
                        document.getElementById('mem-val').textContent = memPct + '%';
                        document.getElementById('mem-fill').style.width = Math.min(100, memPct) + '%';
                        
                        const usedGb = (m.usedBytes / (1024 * 1024 * 1024)).toFixed(1);
                        const totalGb = (m.totalBytes / (1024 * 1024 * 1024)).toFixed(1);
                        const freeGb = (m.availableBytes / (1024 * 1024 * 1024)).toFixed(1);
                        const appMb = (m.appBytes / (1024 * 1024)).toFixed(0);

                        document.getElementById('mem-size').textContent = `${usedGb} GB / ${totalGb} GB`;
                        document.getElementById('mem-free').textContent = `🟢 ${freeGb} GB Free`;
                        document.getElementById('mem-app').textContent = `⚡ App: ${appMb} MB`;
                    }

                    // Disk Usage
                    if (h.disk) {
                        const d = h.disk;
                        const diskPct = Math.round(d.percent || 0);
                        document.getElementById('disk-val').textContent = diskPct + '%';
                        document.getElementById('disk-fill').style.width = Math.min(100, diskPct) + '%';
                        document.getElementById('disk-fill').style.background = diskPct >= 90 ? 'var(--red)' : (diskPct >= 75 ? 'var(--gold)' : 'var(--green)');

                        document.getElementById('disk-size').textContent = `${d.availableGB} GB Free of ${d.totalGB} GB`;
                        document.getElementById('disk-drive-name').textContent = d.primaryDriveLetter ? `Drive ${d.primaryDriveLetter}` : 'Drive';
                        document.getElementById('disk-label').textContent = d.primaryDriveLabel ? `${d.primaryDriveLabel}` : 'Backup Drive';
                    }

                    // All Physical Drives Table
                    if (h.drives && h.drives.length > 0) {
                        document.getElementById('drive-count').textContent = `${h.drives.length} Drives Ready`;
                        const tbody = document.getElementById('drives-tbody');
                        tbody.innerHTML = h.drives.map(drive => {
                            const freeGb = (drive.freeBytes / (1024 * 1024 * 1024)).toFixed(1);
                            const totalGb = (drive.totalBytes / (1024 * 1024 * 1024)).toFixed(1);
                            const pct = drive.usedPercent;
                            const barColor = pct >= 90 ? 'var(--red)' : (pct >= 75 ? 'var(--gold)' : 'var(--green)');
                            const badge = drive.isBackupDrive 
                                ? '<span class=""tag tag-backup"">BACKUP</span>' 
                                : (drive.isSystemDrive ? '<span class=""tag tag-sys"">SYSTEM</span>' : '');

                            return `
                                <tr>
                                    <td><strong>${drive.name}</strong> ${badge}</td>
                                    <td><span style=""color: var(--muted);"">${drive.volumeLabel || 'Local Disk'}</span></td>
                                    <td>${freeGb} GB / ${totalGb} GB</td>
                                    <td style=""min-width: 130px;"">
                                        <div style=""display: flex; justify-content: space-between; font-size: 11px; margin-bottom: 2px;"">
                                            <span>${pct}%</span>
                                        </div>
                                        <div class=""progress-bar"" style=""margin-top: 0; height: 5px;"">
                                            <div class=""progress-fill"" style=""width: ${pct}%; background: ${barColor};""></div>
                                        </div>
                                    </td>
                                </tr>
                            `;
                        }).join('');
                    }

                    // Backup Folders Table
                    if (h.backupFolders && h.backupFolders.length > 0) {
                        const totalBytes = h.backupFolders.reduce((acc, f) => acc + (f.totalSizeBytes || 0), 0);
                        document.getElementById('total-backup-size').textContent = `Total: ${formatBytes(totalBytes)}`;
                        const tbody = document.getElementById('folders-tbody');
                        tbody.innerHTML = h.backupFolders.map(folder => `
                            <tr>
                                <td><strong>${folder.service}</strong></td>
                                <td style=""max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted);"">${folder.path || 'Not set'}</td>
                                <td>${folder.fileCount}</td>
                                <td><strong>${formatBytes(folder.totalSizeBytes)}</strong></td>
                            </tr>
                        `).join('');
                    }
                }

                // History table
                const tbody = document.getElementById('history-rows');
                if (hRes && hRes.length > 0) {
                    tbody.innerHTML = hRes.map(item => `
                        <tr>
                            <td><strong>${item.service.toUpperCase()}</strong></td>
                            <td><span class=""tag ${item.status === 'Success' ? 'tag-success' : 'tag-failed'}"">${item.status}</span></td>
                            <td>${new Date(item.time).toLocaleString()}</td>
                            <td>${item.durationSeconds ? item.durationSeconds.toFixed(1) + 's' : '--'}</td>
                            <td>${formatBytes(item.sizeBytes)}</td>
                            <td>${item.hasFile ? `<a class=""dl"" href=""/download/${encodeURIComponent(item.filename)}"">⬇ Download</a>` : '<span style=""color:var(--muted)"">--</span>'}</td>
                        </tr>
                    `).join('');
                } else {
                    tbody.innerHTML = '<tr><td colspan=""6"" style=""text-align: center; color: var(--muted); padding: 24px;"">No backups recorded yet.</td></tr>';
                }
            } catch(e) {
                console.error(e);
            }
        }

        loadData();
        loadLogs();
        setInterval(loadData, 4000);
        setInterval(loadLogs, 3000);
    </script>
</body>
</html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }
    }
}
