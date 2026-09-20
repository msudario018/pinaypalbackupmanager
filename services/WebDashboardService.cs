using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public static class WebDashboardService
    {
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

            // PIN Authentication check if enabled
            if (ConfigService.Current.HttpServer.RequireAuth && !string.IsNullOrWhiteSpace(ConfigService.Current.HttpServer.WebPin))
            {
                if (!IsAuthorized(request))
                {
                    if (path == "/login" && request.HttpMethod == "POST")
                    {
                        await HandleLoginPostAsync(context);
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

        private static bool IsAuthorized(HttpListenerRequest request)
        {
            var expectedPin = ConfigService.Current.HttpServer.WebPin.Trim();
            if (string.IsNullOrEmpty(expectedPin)) return true;

            // Check query parameter ?pin=...
            var pinQuery = request.QueryString["pin"];
            if (!string.IsNullOrEmpty(pinQuery) && pinQuery == expectedPin) return true;

            // Check Authorization header
            var authHeader = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(authHeader))
            {
                var token = authHeader.Replace("Bearer ", "").Trim();
                if (token == expectedPin) return true;
            }

            // Check Cookie pp_pin
            var cookie = request.Cookies["pp_pin"];
            if (cookie != null && cookie.Value == expectedPin) return true;

            return false;
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
            
            if (BackupSchedulingService.BackupExecutor != null)
            {
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

                await SendJsonAsync(response, 200, new { success = true, message = $"Backup started for {service}" });
            }
            else
            {
                await SendJsonAsync(response, 503, new { success = false, message = "Backup engine is currently offline" });
            }
        }

        private static async Task ServeStatusApiAsync(HttpListenerResponse response)
        {
            var health = HealthCheckService.GetLastResult();
            var history = BackupHistoryService.GetHistory();
            var lastSuccess = history?.Where(h => h.Status == "Success").OrderByDescending(h => h.Timestamp).FirstOrDefault();

            var status = new
            {
                appName = "PinayPal Backup Manager",
                version = BackupConfig.AppVersion,
                timestamp = DateTime.UtcNow,
                isOnline = true,
                services = new
                {
                    ftp = new
                    {
                        name = "FTP / Website",
                        host = BackupConfig.FtpHost,
                        port = BackupConfig.FtpPort,
                        configured = !string.IsNullOrEmpty(BackupConfig.FtpHost)
                    },
                    sql = new
                    {
                        name = "MySQL / Database",
                        host = BackupConfig.FtpHost,
                        user = BackupConfig.SqlUser,
                        configured = !string.IsNullOrEmpty(BackupConfig.SqlUser)
                    },
                    mailchimp = new
                    {
                        name = "Mailchimp",
                        configured = !string.IsNullOrEmpty(BackupConfig.McApiKey)
                    }
                },
                health = health != null ? new
                {
                    status = health.Status,
                    isHealthy = health.IsHealthy,
                    lastCheck = health.Timestamp,
                    cpu = health.Resources.CpuUsagePercent,
                    memory = health.Resources.MemoryUsagePercent,
                    disk = health.Resources.DiskUsagePercent
                } : null,
                lastBackup = lastSuccess != null ? new
                {
                    service = lastSuccess.Service,
                    time = lastSuccess.Timestamp,
                    duration = lastSuccess.Duration.TotalSeconds,
                    sizeBytes = lastSuccess.SizeBytes
                } : null
            };

            await SendJsonAsync(response, 200, status);
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
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }
        body { background: #0B0E14; color: #E6EDF3; display: flex; align-items: center; justify-content: center; min-height: 100vh; }
        .card { background: #161B22; border: 1px solid #30363D; border-radius: 16px; padding: 36px; width: 100%; max-width: 380px; text-align: center; box-shadow: 0 20px 40px rgba(0,0,0,0.6); }
        .logo { font-size: 32px; font-weight: 800; color: #FCA311; margin-bottom: 8px; }
        .sub { color: #8B949E; font-size: 13px; margin-bottom: 28px; }
        input { width: 100%; padding: 14px; background: #0D1117; border: 1px solid #30363D; border-radius: 8px; color: #FFF; font-size: 16px; text-align: center; letter-spacing: 4px; margin-bottom: 20px; outline: none; transition: border 0.2s; }
        input:focus { border-color: #FCA311; }
        button { width: 100%; padding: 14px; background: #FCA311; color: #000; font-weight: 700; border: none; border-radius: 8px; font-size: 14px; cursor: pointer; transition: opacity 0.2s; }
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
    <style>
        :root {
            --bg: #0B0E14; --surface: #161B22; --card: #1B212C; --border: #30363D;
            --text: #F0F6FC; --muted: #8B949E; --gold: #FCA311; --green: #3FB950;
            --blue: #58A6FF; --cyan: #48CAE4; --purple: #A371F7; --red: #F85149;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }
        body { background: var(--bg); color: var(--text); padding: 24px; min-height: 100vh; }
        .container { max-width: 1200px; margin: 0 auto; }
        header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; padding-bottom: 16px; border-bottom: 1px solid var(--border); }
        .title { display: flex; align-items: center; gap: 12px; }
        .logo { font-size: 28px; font-weight: 900; color: var(--gold); letter-spacing: -0.5px; }
        .badge { background: rgba(63, 185, 80, 0.15); color: var(--green); border: 1px solid rgba(63,185,80,0.3); border-radius: 20px; padding: 4px 12px; font-size: 11px; font-weight: 700; display: inline-flex; align-items: center; gap: 6px; }
        .badge::before { content: ''; width: 8px; height: 8px; background: var(--green); border-radius: 50%; display: inline-block; }
        .btn-primary { background: var(--gold); color: #000; border: none; font-weight: 700; border-radius: 8px; padding: 10px 18px; cursor: pointer; transition: all 0.2s; font-size: 13px; }
        .btn-primary:hover { opacity: 0.9; transform: translateY(-1px); }
        .btn-secondary { background: var(--surface); color: var(--text); border: 1px solid var(--border); font-weight: 600; border-radius: 8px; padding: 8px 14px; cursor: pointer; transition: all 0.2s; font-size: 12px; }
        .btn-secondary:hover { border-color: var(--gold); color: var(--gold); }
        
        .grid-3 { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 20px; margin-bottom: 24px; }
        .card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 20px; position: relative; overflow: hidden; }
        .card::before { content: ''; position: absolute; top: 0; left: 0; right: 0; height: 3px; }
        .card-ftp::before { background: var(--green); }
        .card-sql::before { background: var(--gold); }
        .card-mc::before { background: var(--cyan); }
        .card-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
        .card-title { font-size: 15px; font-weight: 700; }
        .card-meta { font-size: 12px; color: var(--muted); margin-bottom: 16px; min-height: 36px; }
        .card-actions { display: flex; gap: 8px; }

        .health-card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 20px; margin-bottom: 24px; }
        .resources { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 16px; margin-top: 16px; }
        .res-item { background: var(--card); padding: 14px; border-radius: 8px; border: 1px solid var(--border); }
        .res-name { font-size: 11px; color: var(--muted); font-weight: 600; text-transform: uppercase; margin-bottom: 6px; }
        .res-val { font-size: 22px; font-weight: 800; color: var(--text); }
        .progress-bar { width: 100%; height: 6px; background: #0D1117; border-radius: 3px; margin-top: 8px; overflow: hidden; }
        .progress-fill { height: 100%; border-radius: 3px; transition: width 0.4s; }

        .table-card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 20px; }
        table { width: 100%; border-collapse: collapse; margin-top: 12px; }
        th { text-align: left; padding: 10px 12px; font-size: 11px; color: var(--muted); border-bottom: 1px solid var(--border); text-transform: uppercase; font-weight: 700; }
        td { padding: 12px; font-size: 13px; border-bottom: 1px solid rgba(48, 54, 61, 0.5); }
        .tag { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 10px; font-weight: 700; }
        .tag-success { background: rgba(63,185,80,0.15); color: var(--green); }
        .tag-failed { background: rgba(248,81,73,0.15); color: var(--red); }
        a.dl { color: var(--blue); text-decoration: none; font-weight: 600; }
        a.dl:hover { text-decoration: underline; }
        .toast { position: fixed; bottom: 24px; right: 24px; background: var(--card); border: 1px solid var(--gold); color: #FFF; padding: 14px 20px; border-radius: 10px; box-shadow: 0 10px 30px rgba(0,0,0,0.8); display: none; font-weight: 600; }
    </style>
</head>
<body>
    <div class=""container"">
        <header>
            <div class=""title"">
                <div class=""logo"">🛡️ PinayPal</div>
                <div class=""badge"">ONLINE</div>
            </div>
            <div>
                <button class=""btn-primary"" onclick=""triggerBackup('all')"">🚀 Run All Backups</button>
            </div>
        </header>

        <!-- Services -->
        <div class=""grid-3"">
            <div class=""card card-ftp"">
                <div class=""card-header"">
                    <div class=""card-title"" style=""color: var(--green)"">🌐 FTP Website Sync</div>
                    <span id=""ftp-badge"" class=""tag tag-success"">READY</span>
                </div>
                <div class=""card-meta"" id=""ftp-meta"">Loading server configuration...</div>
                <div class=""card-actions"">
                    <button class=""btn-secondary"" onclick=""triggerBackup('ftp')"">Backup Website</button>
                </div>
            </div>

            <div class=""card card-sql"">
                <div class=""card-header"">
                    <div class=""card-title"" style=""color: var(--gold)"">🗄️ SQL Database</div>
                    <span id=""sql-badge"" class=""tag tag-success"">READY</span>
                </div>
                <div class=""card-meta"" id=""sql-meta"">Loading database configuration...</div>
                <div class=""card-actions"">
                    <button class=""btn-secondary"" onclick=""triggerBackup('sql')"">Backup Database</button>
                </div>
            </div>

            <div class=""card card-mc"">
                <div class=""card-header"">
                    <div class=""card-title"" style=""color: var(--cyan)"">🐵 Mailchimp Sync</div>
                    <span id=""mc-badge"" class=""tag tag-success"">READY</span>
                </div>
                <div class=""card-meta"" id=""mc-meta"">Audience, templates and campaigns</div>
                <div class=""card-actions"">
                    <button class=""btn-secondary"" onclick=""triggerBackup('mailchimp')"">Backup Mailchimp</button>
                </div>
            </div>
        </div>

        <!-- Health & Resources -->
        <div class=""health-card"">
            <div style=""display: flex; justify-content: space-between; align-items: center;"">
                <div style=""font-weight: 700; font-size: 15px;"">⚡ SYSTEM HEALTH &amp; RESOURCES</div>
                <button class=""btn-secondary"" onclick=""runHealthCheck()"">Run Diagnostics</button>
            </div>
            <div class=""resources"">
                <div class=""res-item"">
                    <div class=""res-name"">Status</div>
                    <div class=""res-val"" id=""health-status"" style=""color: var(--green); font-size: 18px;"">Healthy</div>
                    <div style=""font-size: 10px; color: var(--muted); margin-top: 4px;"" id=""health-time"">Last check: --</div>
                </div>
                <div class=""res-item"">
                    <div class=""res-name"">CPU Usage</div>
                    <div class=""res-val"" id=""cpu-val"">0%</div>
                    <div class=""progress-bar""><div class=""progress-fill"" id=""cpu-fill"" style=""background: var(--blue); width: 0%;""></div></div>
                </div>
                <div class=""res-item"">
                    <div class=""res-name"">Memory Usage</div>
                    <div class=""res-val"" id=""mem-val"">0%</div>
                    <div class=""progress-bar""><div class=""progress-fill"" id=""mem-fill"" style=""background: var(--purple); width: 0%;""></div></div>
                </div>
                <div class=""res-item"">
                    <div class=""res-name"">Disk Usage</div>
                    <div class=""res-val"" id=""disk-val"">0%</div>
                    <div class=""progress-bar""><div class=""progress-fill"" id=""disk-fill"" style=""background: var(--green); width: 0%;""></div></div>
                </div>
            </div>
        </div>

        <!-- History -->
        <div class=""table-card"">
            <div style=""font-weight: 700; font-size: 15px; margin-bottom: 8px;"">📜 RECENT BACKUPS</div>
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

    <div class=""toast"" id=""toast""></div>

    <script>
        function showToast(msg) {
            const t = document.getElementById('toast');
            t.textContent = msg;
            t.style.display = 'block';
            setTimeout(() => { t.style.display = 'none'; }, 3500);
        }

        async function triggerBackup(service) {
            showToast('Starting backup: ' + service.toUpperCase() + '...');
            try {
                const res = await fetch('/api/backup/' + service, { method: 'POST' });
                const d = await res.json();
                showToast(d.message || 'Backup triggered');
                setTimeout(loadData, 2000);
            } catch(e) {
                showToast('Error triggering backup');
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

        async function loadData() {
            try {
                const [sRes, hRes] = await Promise.all([
                    fetch('/api/status').then(r => r.json()),
                    fetch('/api/history').then(r => r.json())
                ]);

                // FTP
                document.getElementById('ftp-meta').textContent = 'Host: ' + (sRes.services.ftp.host || 'Not set') + ' (Port ' + sRes.services.ftp.port + ')';
                // SQL
                document.getElementById('sql-meta').textContent = 'User: ' + (sRes.services.sql.user || 'Not set') + ' | Remote sync';

                // Health & Resources
                if (sRes.health) {
                    document.getElementById('health-status').textContent = sRes.health.status;
                    document.getElementById('health-status').style.color = sRes.health.isHealthy ? 'var(--green)' : 'var(--red)';
                    document.getElementById('health-time').textContent = 'Last check: ' + new Date(sRes.health.lastCheck).toLocaleTimeString();
                    
                    const cpu = Math.round(sRes.health.cpu || 0);
                    document.getElementById('cpu-val').textContent = cpu + '%';
                    document.getElementById('cpu-fill').style.width = cpu + '%';

                    const mem = Math.round(sRes.health.memory || 0);
                    document.getElementById('mem-val').textContent = mem + '%';
                    document.getElementById('mem-fill').style.width = mem + '%';

                    const disk = Math.round(sRes.health.disk || 0);
                    document.getElementById('disk-val').textContent = disk + '%';
                    document.getElementById('disk-fill').style.width = disk + '%';
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
        setInterval(loadData, 5000);
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
