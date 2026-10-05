using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QRCoder;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Enterprise Telegram integration service for PinayPal Backup Manager.
    /// Provides real-time bidirectional messaging:
    /// 1. Outgoing alerts on backup start, success, and failure, disconnects, and outdated states.
    /// 2. QR code generation and dispatch directly to Telegram for one-tap iOS app reconnection.
    /// 3. Interactive Telegram bot long-polling with commands: /help, /status, /qr, /backup (full, ftp, sql, mailchimp), /health, /pause, /resume.
    /// </summary>
    public static class TelegramService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        private static CancellationTokenSource? _pollCts;
        private static Task? _pollTask;
        private static long _lastUpdateId = 0;
        private static readonly object _stateLock = new();
        private static bool _isPolling = false;
        public static bool IsPolling => _isPolling;

        public static bool IsEnabled => NotificationService.Settings.TelegramEnabled && !string.IsNullOrWhiteSpace(NotificationService.Settings.TelegramBotToken);
        public static string BotToken => NotificationService.Settings.TelegramBotToken?.Trim() ?? string.Empty;
        public static string ChatId => NotificationService.Settings.TelegramChatId?.Trim() ?? string.Empty;

        /// <summary>
        /// Initializes the Telegram service on application startup.
        /// </summary>
        public static void Initialize()
        {
            Restart();
        }

        /// <summary>
        /// Restarts the Telegram background polling service according to current settings.
        /// </summary>
        public static void Restart()
        {
            Stop();

            if (!IsEnabled)
            {
                LogService.WriteSystemLog("[TELEGRAM] Telegram Bot is disabled or missing bot token.", "Information", "SYSTEM");
                return;
            }

            lock (_stateLock)
            {
                _pollCts = new CancellationTokenSource();
                var token = _pollCts.Token;
                _isPolling = true;
                _pollTask = Task.Run(() => PollingLoopAsync(token), token);
            }

            LogService.WriteSystemLog("[TELEGRAM] Telegram Bot polling engine started.", "Information", "SYSTEM");
        }

        /// <summary>
        /// Stops the background polling loop.
        /// </summary>
        public static void Stop()
        {
            lock (_stateLock)
            {
                if (_pollCts != null)
                {
                    try
                    {
                        _pollCts.Cancel();
                        _pollCts.Dispose();
                    }
                    catch { }
                    _pollCts = null;
                }
                _isPolling = false;
            }
        }

        #region Telegram API Outgoing Dispatches

        /// <summary>
        /// Sends an HTML-formatted message to the configured (or target) Telegram chat.
        /// </summary>
        public static async Task<(bool success, string message)> SendMessageAsync(string text, string? targetChatId = null, string parseMode = "HTML")
        {
            var token = BotToken;
            var chat = !string.IsNullOrWhiteSpace(targetChatId) ? targetChatId : ChatId;

            if (string.IsNullOrWhiteSpace(token))
                return (false, "Telegram Bot Token is not configured.");
            if (string.IsNullOrWhiteSpace(chat))
                return (false, "Telegram Chat ID is not configured.");

            try
            {
                var url = $"https://api.telegram.org/bot{token}/sendMessage";
                var payload = new Dictionary<string, object>
                {
                    ["chat_id"] = chat,
                    ["text"] = text,
                    ["disable_web_page_preview"] = true
                };

                if (!string.IsNullOrEmpty(parseMode))
                {
                    payload["parse_mode"] = parseMode;
                }

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return (true, "Message sent successfully.");
                }

                // If HTML parse failed, retry once without parse_mode so the notification is never lost
                if (parseMode == "HTML" && responseBody.Contains("can't parse entities", StringComparison.OrdinalIgnoreCase))
                {
                    LogService.WriteSystemLog("[TELEGRAM] HTML formatting parse failed. Retrying in plain text mode.", "Warning", "SYSTEM");
                    return await SendMessageAsync(StripHtmlTags(text), targetChatId, "");
                }

                LogService.WriteSystemLog($"[TELEGRAM] SendMessage failed: {responseBody}", "Error", "SYSTEM");
                return (false, $"Telegram API Error: {responseBody}");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[TELEGRAM] SendMessage exception: {ex.Message}", "Error", "SYSTEM");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Sends a photo (e.g. iOS pairing QR code) with caption to Telegram.
        /// </summary>
        public static async Task<(bool success, string message)> SendPhotoAsync(byte[] imageBytes, string fileName, string caption, string? targetChatId = null)
        {
            var token = BotToken;
            var chat = !string.IsNullOrWhiteSpace(targetChatId) ? targetChatId : ChatId;

            if (string.IsNullOrWhiteSpace(token))
                return (false, "Telegram Bot Token is not configured.");
            if (string.IsNullOrWhiteSpace(chat))
                return (false, "Telegram Chat ID is not configured.");

            try
            {
                var url = $"https://api.telegram.org/bot{token}/sendPhoto";
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(chat), "chat_id");
                form.Add(new StringContent(caption), "caption");
                form.Add(new StringContent("HTML"), "parse_mode");

                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                form.Add(imageContent, "photo", fileName);

                using var response = await _httpClient.PostAsync(url, form);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return (true, "Photo sent successfully.");
                }

                LogService.WriteSystemLog($"[TELEGRAM] SendPhoto failed: {responseBody}", "Error", "SYSTEM");
                return (false, $"Telegram API Error: {responseBody}");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[TELEGRAM] SendPhoto exception: {ex.Message}", "Error", "SYSTEM");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Sends a test message using provided or saved credentials.
        /// </summary>
        public static async Task<(bool success, string message)> SendTestMessageAsync(string? customToken = null, string? customChatId = null)
        {
            var token = !string.IsNullOrWhiteSpace(customToken) ? customToken.Trim() : BotToken;
            var chat = !string.IsNullOrWhiteSpace(customChatId) ? customChatId.Trim() : ChatId;

            if (string.IsNullOrWhiteSpace(token))
                return (false, "Please enter your Telegram Bot API Token.");
            if (string.IsNullOrWhiteSpace(chat))
                return (false, "Please enter your Telegram Chat ID.");

            // First verify bot token with getMe
            try
            {
                var meUrl = $"https://api.telegram.org/bot{token}/getMe";
                using var meRes = await _httpClient.GetAsync(meUrl);
                if (!meRes.IsSuccessStatusCode)
                {
                    var err = await meRes.Content.ReadAsStringAsync();
                    return (false, $"Invalid Bot Token. Telegram replied: {err}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Could not contact Telegram API: {ex.Message}");
            }

            var text = new StringBuilder();
            text.AppendLine("🤖 <b>PinayPal Backup Manager — Alert Verification</b>");
            text.AppendLine();
            text.AppendLine("✅ Telegram integration is active and operating normally!");
            text.AppendLine($"💻 <b>Host:</b> <code>{EscapeHtml(Environment.MachineName)}</code>");
            text.AppendLine($"🕒 <b>Local Time:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            text.AppendLine();
            text.AppendLine("You will receive real-time notifications for:");
            text.AppendLine("• 🚀 <b>Backup Started</b> (FTP, SQL, Mailchimp)");
            text.AppendLine("• ✅ <b>Backup Complete</b> (with duration, size & file info)");
            text.AppendLine("• 🚨 <b>Backup Failed</b> (with immediate diagnostic details)");
            text.AppendLine("• ⚠️ <b>Disconnects & Outdated Backups</b>");
            text.AppendLine();
            text.AppendLine("<i>Tip: Type <code>/help</code> in this chat to see available commands or type <code>/qr</code> to get your iOS reconnection code!</i>");

            return await SendMessageAsync(text.ToString(), chat, "HTML");
        }

        /// <summary>
        /// Inspects the Telegram bot's recent updates to automatically discover the sender's Chat ID.
        /// </summary>
        public static async Task<(bool success, string chatId, string message)> DetectChatIdAsync(string? customToken = null)
        {
            var token = !string.IsNullOrWhiteSpace(customToken) ? customToken.Trim() : BotToken;
            if (string.IsNullOrWhiteSpace(token))
                return (false, "", "Bot token is required to detect Chat ID.");

            try
            {
                var url = $"https://api.telegram.org/bot{token}/getUpdates?limit=10";
                using var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return (false, "", $"Telegram API Error: {content}");

                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("result", out var resultArr) && resultArr.GetArrayLength() > 0)
                {
                    // Find the most recent message with a chat id
                    for (int i = resultArr.GetArrayLength() - 1; i >= 0; i--)
                    {
                        var update = resultArr[i];
                        if (update.TryGetProperty("message", out var msg) &&
                            msg.TryGetProperty("chat", out var chat) &&
                            chat.TryGetProperty("id", out var idElem))
                        {
                            var detectedId = idElem.ToString();
                            string senderName = "User";
                            if (msg.TryGetProperty("from", out var from))
                            {
                                var firstName = from.TryGetProperty("first_name", out var fn) ? fn.GetString() : "";
                                var username = from.TryGetProperty("username", out var un) ? $"@{un.GetString()}" : "";
                                senderName = $"{firstName} {username}".Trim();
                            }

                            return (true, detectedId, $"Found Chat ID for {senderName}: {detectedId}");
                        }
                    }
                }

                return (false, "", "No messages found for this bot yet. Please open your bot in Telegram, click 'Start' or send any message, then try 'Detect Chat ID' again.");
            }
            catch (Exception ex)
            {
                return (false, "", $"Error querying updates: {ex.Message}");
            }
        }

        #endregion

        #region Automated Backup Notification Alerts

        /// <summary>
        /// Sends an alert when a backup operation starts, completes, or fails.
        /// </summary>
        public static async Task SendBackupAlertAsync(string serviceName, string status, string details)
        {
            var s = NotificationService.Settings;
            if (!s.TelegramEnabled || string.IsNullOrWhiteSpace(s.TelegramBotToken) || string.IsNullOrWhiteSpace(s.TelegramChatId))
                return;

            var normStatus = status?.ToLowerInvariant() ?? "";
            bool isStart = normStatus.Contains("start");
            bool isSuccess = normStatus.Contains("complete") || normStatus.Contains("success");
            bool isFail = normStatus.Contains("fail") || normStatus.Contains("error");

            if (isStart && !s.NotifyOnBackupStart) return;
            if (isSuccess && !s.NotifyOnBackupSuccess) return;
            if (isFail && !s.NotifyOnBackupFailure) return;

            string headerEmoji = isStart ? "🚀" : (isSuccess ? "✅" : "🚨");
            string statusHeader = isStart ? "BACKUP STARTED" : (isSuccess ? "BACKUP COMPLETED" : "BACKUP FAILED");

            var sb = new StringBuilder();
            sb.AppendLine($"{headerEmoji} <b>[{statusHeader}] — {EscapeHtml(serviceName.ToUpperInvariant())}</b>");
            sb.AppendLine();
            sb.AppendLine($"🖥️ <b>Host:</b> <code>{EscapeHtml(Environment.MachineName)}</code>");
            sb.AppendLine($"🕒 <b>Timestamp:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local)");

            if (!string.IsNullOrWhiteSpace(details))
            {
                sb.AppendLine();
                sb.AppendLine($"📋 <b>Details:</b>\n{EscapeHtml(details)}");
            }

            if (isFail)
            {
                sb.AppendLine();
                sb.AppendLine("<i>⚠️ Check desktop app logs or type <code>/status</code> to inspect service state.</i>");
            }

            await SendMessageAsync(sb.ToString());
        }

        /// <summary>
        /// Sends an alert when network or Cloudflare tunnel connection is lost.
        /// </summary>
        public static async Task SendDisconnectAlertAsync(string reason)
        {
            var s = NotificationService.Settings;
            if (!s.TelegramEnabled || !s.NotifyOnDisconnect || string.IsNullOrWhiteSpace(s.TelegramBotToken) || string.IsNullOrWhiteSpace(s.TelegramChatId))
                return;

            var sb = new StringBuilder();
            sb.AppendLine("⚠️ <b>[CONNECTION ALERT] — Disconnect Detected</b>");
            sb.AppendLine();
            sb.AppendLine($"🖥️ <b>Host:</b> <code>{EscapeHtml(Environment.MachineName)}</code>");
            sb.AppendLine($"🕒 <b>Timestamp:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local)");
            sb.AppendLine($"📡 <b>Notice:</b> {EscapeHtml(reason)}");
            sb.AppendLine();
            sb.AppendLine("<i>Remote access may be temporarily interrupted. Type <code>/status</code> to check reconnect status or <code>/qr</code> to retrieve a new pairing code.</i>");

            await SendMessageAsync(sb.ToString());
        }

        /// <summary>
        /// Sends an alert when a service backup has become outdated (>24h).
        /// </summary>
        public static async Task SendOutdatedAlertAsync(string serviceName, string detail)
        {
            var s = NotificationService.Settings;
            if (!s.TelegramEnabled || !s.NotifyOnOutdated || string.IsNullOrWhiteSpace(s.TelegramBotToken) || string.IsNullOrWhiteSpace(s.TelegramChatId))
                return;

            var sb = new StringBuilder();
            sb.AppendLine($"⚠️ <b>[BACKUP OUTDATED] — {EscapeHtml(serviceName.ToUpperInvariant())}</b>");
            sb.AppendLine();
            sb.AppendLine($"🖥️ <b>Host:</b> <code>{EscapeHtml(Environment.MachineName)}</code>");
            sb.AppendLine($"🕒 <b>Timestamp:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local)");
            sb.AppendLine($"⏳ <b>Status:</b> {EscapeHtml(detail)}");
            sb.AppendLine();
            sb.AppendLine($"<i>Tip: You can trigger an immediate backup by replying: <code>/backup {serviceName.ToLowerInvariant()}</code></i>");

            await SendMessageAsync(sb.ToString());
        }

        #endregion

        #region iOS Pairing QR Code Generation & Dispatch

        /// <summary>
        /// Generates the exact iOS pairing QR code payload PNG byte array using QRCoder.
        /// </summary>
        public static byte[] GenerateConnectionQrBytes(out string payloadJson, out string localIp, out int port, out string pin, out string cloudflare)
        {
            localIp = FileDownloadService.GetLocalIpAddress() ?? "127.0.0.1";
            port = ConfigService.Current.HttpServer?.Port ?? 8080;
            pin = ConfigService.Current.HttpServer?.WebPin ?? "";
            cloudflare = CloudflareTunnelService.ActiveUrl ?? ConfigService.Current.HttpServer?.CloudflareUrl ?? "";
            var hostname = Environment.MachineName;

            int localPort = port;
            var allIps = FileDownloadService.GetAllLocalIPv4Addresses();
            var allUrls = allIps.Select(ip => $"http://{ip}:{localPort}").ToList();

            var payloadObj = new
            {
                localUrl = $"http://{localIp}:{localPort}",
                allLocalUrls = allUrls,
                fallbackUrl = cloudflare,
                cloudflareUrl = cloudflare,
                tailscaleUrl = TailscaleNetworkService.GetTailscaleUrl(localPort),
                pin = pin,
                hostname = hostname,
                version = "3.7.2"
            };

            payloadJson = JsonSerializer.Serialize(payloadObj);

            var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payloadJson, QRCodeGenerator.ECCLevel.Q);
            var pngQr = new PngByteQRCode(data);
            return pngQr.GetGraphic(8, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, true);
        }

        /// <summary>
        /// Generates and sends the iOS pairing QR code directly to Telegram.
        /// </summary>
        public static async Task<(bool success, string message)> SendConnectionQrAsync(string? targetChatId = null)
        {
            try
            {
                var bytes = GenerateConnectionQrBytes(out _, out string localIp, out int port, out string pin, out string cloudflare);

                var caption = new StringBuilder();
                caption.AppendLine("📱 <b>PinayPal iOS App Reconnection QR Code</b>");
                caption.AppendLine();
                caption.AppendLine($"🖥️ <b>Host:</b> <code>{EscapeHtml(Environment.MachineName)}</code>");
                caption.AppendLine($"🔑 <b>Web Access PIN:</b> <code>{EscapeHtml(string.IsNullOrEmpty(pin) ? "None (Open)" : pin)}</code>");
                caption.AppendLine($"🌐 <b>Local URL:</b> <code>http://{localIp}:{port}</code>");
                if (!string.IsNullOrWhiteSpace(cloudflare))
                {
                    caption.AppendLine($"☁️ <b>Cloudflare Tunnel:</b> <code>{EscapeHtml(cloudflare)}</code>");
                }
                caption.AppendLine();
                caption.AppendLine("<b>How to Reconnect:</b>");
                caption.AppendLine("1. Open the <b>PinayPal Backup</b> app on your iPhone.");
                caption.AppendLine("2. Tap <b>'Scan QR Code'</b> on the pairing screen.");
                caption.AppendLine("3. Point your camera at this QR code to restore live connection!");

                return await SendPhotoAsync(bytes, "pinaypal_ios_pair_qr.png", caption.ToString(), targetChatId);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[TELEGRAM] QR dispatch failed: {ex.Message}", "Error", "SYSTEM");
                return (false, ex.Message);
            }
        }

        #endregion

        #region Background Long-Polling & Command Execution

        private static async Task RegisterBotCommandsAsync(string token)
        {
            try
            {
                var url = $"https://api.telegram.org/bot{token}/setMyCommands";
                var body = new
                {
                    commands = new[]
                    {
                        new { command = "help", description = "Show command guide and instructions" },
                        new { command = "ai", description = "Chat with PinayPal AI, learn preferences, or query host" },
                        new { command = "status", description = "Current backup status, health, and tunnel URL" },
                        new { command = "qr", description = "Get iOS app pairing QR code to reconnect" },
                        new { command = "backup", description = "Run backup (full, ftp, sql, mailchimp)" },
                        new { command = "health", description = "Run on-demand system health check" },
                        new { command = "pause", description = "Pause automatic scheduled backups" },
                        new { command = "resume", description = "Resume automatic scheduled backups" }
                    }
                };

                var json = JsonSerializer.Serialize(body);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(url, content);
            }
            catch { }
        }

        private static async Task PollingLoopAsync(CancellationToken ct)
        {
            var token = BotToken;
            if (string.IsNullOrWhiteSpace(token)) return;

            // Register autocomplete command menu
            await RegisterBotCommandsAsync(token);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var url = $"https://api.telegram.org/bot{token}/getUpdates?offset={_lastUpdateId + 1}&timeout=20";
                    using var response = await _httpClient.GetAsync(url, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        await Task.Delay(4000, ct);
                        continue;
                    }

                    var content = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(content);

                    if (doc.RootElement.TryGetProperty("result", out var resultArr))
                    {
                        foreach (var update in resultArr.EnumerateArray())
                        {
                            if (update.TryGetProperty("update_id", out var uid))
                            {
                                var idVal = uid.GetInt64();
                                if (idVal > _lastUpdateId)
                                {
                                    _lastUpdateId = idVal;
                                }
                            }

                            if (update.TryGetProperty("message", out var message))
                            {
                                _ = Task.Run(() => ProcessIncomingMessageAsync(message), ct);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[TELEGRAM] Polling loop error: {ex.Message}", "Warning", "SYSTEM");
                    try { await Task.Delay(5000, ct); } catch { break; }
                }
            }
        }

        private static async Task ProcessIncomingMessageAsync(JsonElement message)
        {
            try
            {
                if (!message.TryGetProperty("chat", out var chatElem) || !chatElem.TryGetProperty("id", out var idElem))
                    return;

                var senderChatId = idElem.ToString();
                var text = message.TryGetProperty("text", out var tElem) ? tElem.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(text))
                    return;

                var configuredChatId = ChatId;

                // Security Authorization:
                // If Chat ID is configured, restrict all commands to the authorized chat ID.
                if (!string.IsNullOrWhiteSpace(configuredChatId) && !string.Equals(senderChatId, configuredChatId, StringComparison.OrdinalIgnoreCase))
                {
                    LogService.WriteSystemLog($"[TELEGRAM] Unauthorized command attempt from Chat ID {senderChatId}: '{text}'", "Warning", "SECURITY");
                    var deniedMsg = "⛔ <b>Access Denied</b>\n\nThis PinayPal Backup Manager bot is linked to another user.\n" +
                                   $"Your Chat ID: <code>{senderChatId}</code>\n" +
                                   "If this is your desktop, update the Chat ID in desktop app Settings.";
                    await SendMessageAsync(deniedMsg, senderChatId);
                    return;
                }

                // If Chat ID is not configured yet, welcome the user and provide their Chat ID
                if (string.IsNullOrWhiteSpace(configuredChatId))
                {
                    var welcomeMsg = "👋 <b>Welcome to PinayPal Backup Manager Bot!</b>\n\n" +
                                     $"Your Telegram Chat ID is: <code>{senderChatId}</code>\n\n" +
                                     "<b>Setup Instructions:</b>\n" +
                                     "1. Copy your Chat ID above.\n" +
                                     "2. Open the <b>PinayPal Backup Manager</b> desktop app on your PC.\n" +
                                     "3. Go to <b>Settings → Telegram Bot &amp; Remote Alerts</b>.\n" +
                                     "4. Paste your Chat ID and click <b>Save Telegram Config</b>.\n\n" +
                                     "Once saved, you will be authorized to trigger backups and receive alerts!";
                    await SendMessageAsync(welcomeMsg, senderChatId);
                    return;
                }

                // Process authorized command
                await HandleCommandAsync(text.Trim(), senderChatId);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[TELEGRAM] Error handling incoming message: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static async Task HandleCommandAsync(string rawText, string chatId)
        {
            // Normalize command string
            var cleanText = rawText.Trim();
            var tokens = cleanText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return;

            var root = tokens[0].ToLowerInvariant();
            if (root.Contains('@'))
                root = root.Substring(0, root.IndexOf('@'));
            if (root.StartsWith('/'))
                root = root.Substring(1);

            switch (root)
            {
                case "start":
                case "help":
                    await SendHelpMessageAsync(chatId);
                    break;

                case "qr":
                case "connect":
                case "reconnect":
                case "pair":
                    await SendMessageAsync("📱 <i>Generating iOS pairing QR code with active network & tunnel credentials...</i>", chatId);
                    await SendConnectionQrAsync(chatId);
                    break;

                case "status":
                case "info":
                    await SendStatusMessageAsync(chatId);
                    break;

                case "health":
                    await RunHealthCheckAndReportAsync(chatId);
                    break;

                case "pause":
                    if (BackupManager.Current != null)
                    {
                        BackupManager.Current.IsPaused = true;
                        await SendMessageAsync("⏸️ <b>Automatic Backup Scheduler Paused</b>\nScheduled backups will not run until resumed.", chatId);
                    }
                    else
                    {
                        await SendMessageAsync("⚠️ Backup Manager is initializing. Try again in a few seconds.", chatId);
                    }
                    break;

                case "resume":
                    if (BackupManager.Current != null)
                    {
                        BackupManager.Current.IsPaused = false;
                        await SendMessageAsync("▶️ <b>Automatic Backup Scheduler Resumed</b>\nAll daily and periodic schedules are active.", chatId);
                    }
                    else
                    {
                        await SendMessageAsync("⚠️ Backup Manager is initializing. Try again in a few seconds.", chatId);
                    }
                    break;

                case "backup":
                    await HandleBackupCommandAsync(tokens, chatId);
                    break;

                case "ai":
                case "ask":
                    await HandleAiCommandAsync(tokens, rawText, chatId);
                    break;

                default:
                    // Support natural typing without leading slash (e.g. "backup full", "qr", "status", "health")
                    if (tokens.Length >= 2 && string.Equals(tokens[0], "backup", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleBackupCommandAsync(tokens, chatId);
                    }
                    else if (tokens.Length >= 1 && (string.Equals(tokens[0], "qr", StringComparison.OrdinalIgnoreCase) || string.Equals(tokens[0], "connect", StringComparison.OrdinalIgnoreCase)))
                    {
                        await SendConnectionQrAsync(chatId);
                    }
                    else if (tokens.Length >= 1 && string.Equals(tokens[0], "status", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendStatusMessageAsync(chatId);
                    }
                    else if (tokens.Length >= 1 && string.Equals(tokens[0], "health", StringComparison.OrdinalIgnoreCase))
                    {
                        await RunHealthCheckAndReportAsync(chatId);
                    }
                    else
                    {
                        // Route conversational input naturally to the AI Assistant
                        await HandleAiCommandAsync(tokens, rawText, chatId);
                    }
                    break;
            }
        }

        private static async Task HandleAiCommandAsync(string[] tokens, string rawText, string chatId)
        {
            var prompt = rawText.Trim();
            if (prompt.StartsWith("/ai", StringComparison.OrdinalIgnoreCase) || prompt.StartsWith("/ask", StringComparison.OrdinalIgnoreCase))
            {
                var spaceIdx = prompt.IndexOf(' ');
                prompt = spaceIdx > 0 ? prompt.Substring(spaceIdx + 1).Trim() : "";
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                var guide = new StringBuilder();
                guide.AppendLine("🤖 <b>PinayPal AI Assistant</b>");
                guide.AppendLine();
                guide.AppendLine("Ask questions, teach preferences, or manage your backups directly:");
                guide.AppendLine("• <code>/ai How is disk space?</code>");
                guide.AppendLine("• <code>/ai Why did the last backup fail?</code>");
                guide.AppendLine("• <code>/ai Remember that my preferred backup time is 11 PM</code>");
                guide.AppendLine("• <code>/ai What do you remember?</code>");
                guide.AppendLine("• <code>/ai Switch to Backup Specialist agent</code>");
                guide.AppendLine("• <code>/ai Run a backup for SQL database</code>");
                guide.AppendLine();
                guide.AppendLine("<i>Tip: You can also chat naturally without typing /ai!</i>");
                await SendMessageAsync(guide.ToString(), chatId);
                return;
            }

            await SendMessageAsync("🧠 <i>PinayPal AI is analyzing...</i>", chatId);

            try
            {
                var response = await AIAssistantService.ProcessUserMessageAsync(prompt);
                var sb = new StringBuilder();
                sb.AppendLine("🤖 <b>PinayPal AI:</b>");
                sb.AppendLine();
                sb.AppendLine(EscapeHtml(response.Content));

                if (response.ProposedAction != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"⚡ <b>Action Proposed:</b> <code>{EscapeHtml(response.ProposedAction.Title)}</code>");
                    sb.AppendLine($"<i>{EscapeHtml(response.ProposedAction.Description)}</i>");
                    sb.AppendLine();
                    sb.AppendLine("🔒 <i>Action requires approval: reply with <code>/backup full</code> (or specific service) or tap Approve on desktop/iOS.</i>");
                }

                await SendMessageAsync(sb.ToString(), chatId);
            }
            catch (Exception ex)
            {
                await SendMessageAsync($"⚠️ AI Assistant encountered an issue: {EscapeHtml(ex.Message)}", chatId);
            }
        }

        private static async Task HandleBackupCommandAsync(string[] tokens, string chatId)
        {
            if (tokens.Length < 2)
            {
                var sb = new StringBuilder();
                sb.AppendLine("⚡ <b>Backup Command Options</b>");
                sb.AppendLine();
                sb.AppendLine("Type any of the following commands:");
                sb.AppendLine("• <code>/backup full</code> — Run Full Backup for all services");
                sb.AppendLine("• <code>/backup ftp</code> — Run Website / FTP file backup");
                sb.AppendLine("• <code>/backup sql</code> — Run SQL Database backup");
                sb.AppendLine("• <code>/backup mailchimp</code> — Run Full Mailchimp backup");
                sb.AppendLine("• <code>/backup mailchimp members</code> — Export Mailchimp Members");
                sb.AppendLine("• <code>/backup mailchimp campaigns</code> — Export Mailchimp Campaigns");
                sb.AppendLine("• <code>/backup mailchimp reports</code> — Export Campaign Reports");
                sb.AppendLine("• <code>/backup mailchimp merge_fields</code> — Export Merge Fields");
                sb.AppendLine("• <code>/backup mailchimp tags</code> — Export Tags");
                sb.AppendLine();
                sb.AppendLine("<i>Tip: You can also just type <code>backup full</code> or <code>backup sql</code> without the slash!</i>");
                await SendMessageAsync(sb.ToString(), chatId);
                return;
            }

            var target = tokens[1].ToLowerInvariant();

            // Full / All services backup
            if (target is "full" or "all" or "both")
            {
                await SendMessageAsync("⏳ <b>Starting Full Backup</b> for Website (FTP), Database (SQL), and Mailchimp in parallel...", chatId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (BackupSchedulingService.BackupExecutor != null)
                        {
                            await BackupSchedulingService.BackupExecutor("all", "TELEGRAM");
                        }
                    }
                    catch (Exception ex)
                    {
                        await SendMessageAsync($"🚨 Failed to launch full backup: {EscapeHtml(ex.Message)}", chatId);
                    }
                });
                return;
            }

            // Website / FTP backup
            if (target is "ftp" or "website" or "files" or "web")
            {
                await SendMessageAsync("⏳ <b>Starting Website / FTP Backup</b>...", chatId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (BackupSchedulingService.BackupExecutor != null)
                        {
                            await BackupSchedulingService.BackupExecutor("ftp", "TELEGRAM");
                        }
                    }
                    catch (Exception ex)
                    {
                        await SendMessageAsync($"🚨 Failed to launch FTP backup: {EscapeHtml(ex.Message)}", chatId);
                    }
                });
                return;
            }

            // SQL / Database backup
            if (target is "sql" or "database" or "db")
            {
                await SendMessageAsync("⏳ <b>Starting Database / SQL Backup</b>...", chatId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (BackupSchedulingService.BackupExecutor != null)
                        {
                            await BackupSchedulingService.BackupExecutor("sql", "TELEGRAM");
                        }
                    }
                    catch (Exception ex)
                    {
                        await SendMessageAsync($"🚨 Failed to launch SQL backup: {EscapeHtml(ex.Message)}", chatId);
                    }
                });
                return;
            }

            // Mailchimp backup (full or specific subtask)
            if (target is "mailchimp" or "mc")
            {
                // Check if a 3rd argument is provided (e.g. /backup mailchimp members)
                if (tokens.Length >= 3)
                {
                    var taskRaw = tokens[2].ToLowerInvariant();
                    var taskName = MapMailchimpTask(taskRaw);

                    if (taskName != null)
                    {
                        await SendMessageAsync($"⏳ <b>Starting Mailchimp export for:</b> <code>{taskName}</code>...", chatId);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (BackupSchedulingService.MailchimpTaskExecutor != null)
                                {
                                    var ok = await BackupSchedulingService.MailchimpTaskExecutor(taskName);
                                    if (!ok)
                                    {
                                        await SendMessageAsync($"⚠️ Mailchimp is currently busy with another backup task.", chatId);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                await SendMessageAsync($"🚨 Mailchimp export failed: {EscapeHtml(ex.Message)}", chatId);
                            }
                        });
                        return;
                    }
                    else
                    {
                        await SendMessageAsync($"❓ Unknown Mailchimp task: <code>{EscapeHtml(tokens[2])}</code>\nAvailable: <code>members</code>, <code>campaigns</code>, <code>reports</code>, <code>merge_fields</code>, <code>tags</code>", chatId);
                        return;
                    }
                }

                // Full Mailchimp backup
                await SendMessageAsync("⏳ <b>Starting Full Mailchimp Backup</b> (all endpoints)...", chatId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (BackupSchedulingService.BackupExecutor != null)
                        {
                            await BackupSchedulingService.BackupExecutor("mailchimp", "TELEGRAM");
                        }
                    }
                    catch (Exception ex)
                    {
                        await SendMessageAsync($"🚨 Failed to launch Mailchimp backup: {EscapeHtml(ex.Message)}", chatId);
                    }
                });
                return;
            }

            // Direct Mailchimp shortcut (e.g. /backup members)
            var directMcTask = MapMailchimpTask(target);
            if (directMcTask != null)
            {
                await SendMessageAsync($"⏳ <b>Starting Mailchimp export for:</b> <code>{directMcTask}</code>...", chatId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (BackupSchedulingService.MailchimpTaskExecutor != null)
                        {
                            await BackupSchedulingService.MailchimpTaskExecutor(directMcTask);
                        }
                    }
                    catch (Exception ex)
                    {
                        await SendMessageAsync($"🚨 Mailchimp export failed: {EscapeHtml(ex.Message)}", chatId);
                    }
                });
                return;
            }

            await SendMessageAsync($"❓ Unknown backup target: <code>{EscapeHtml(target)}</code>\n\nOptions: <code>full</code>, <code>ftp</code>, <code>sql</code>, <code>mailchimp</code>, <code>members</code>, <code>campaigns</code>, <code>reports</code>, <code>merge_fields</code>, <code>tags</code>", chatId);
        }

        private static string? MapMailchimpTask(string input)
        {
            return input switch
            {
                "members" or "member" or "contacts" => "Members",
                "campaigns" or "campaign" => "Campaigns",
                "reports" or "report" => "Reports",
                "merge_fields" or "mergefields" or "merge" or "fields" => "Merge_Fields",
                "tags" or "tag" => "Tags",
                _ => null
            };
        }

        private static async Task SendHelpMessageAsync(string chatId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("🤖 <b>PinayPal Backup Manager — Bot Commands</b>");
            sb.AppendLine();
            sb.AppendLine("⚡ <b>Backup Control:</b>");
            sb.AppendLine("• <code>/backup full</code> — Run parallel backup for all services");
            sb.AppendLine("• <code>/backup ftp</code> — Run Website / FTP backup");
            sb.AppendLine("• <code>/backup sql</code> — Run Database / SQL backup");
            sb.AppendLine("• <code>/backup mailchimp</code> — Run full Mailchimp export");
            sb.AppendLine("• <code>/backup mailchimp members</code> — Export audience members");
            sb.AppendLine("• <code>/backup mailchimp campaigns</code> — Export campaigns");
            sb.AppendLine("• <code>/backup mailchimp reports</code> — Export campaign reports");
            sb.AppendLine("• <code>/backup mailchimp merge_fields</code> — Export merge fields");
            sb.AppendLine("• <code>/backup mailchimp tags</code> — Export tags");
            sb.AppendLine();
            sb.AppendLine("🧠 <b>PinayPal AI Assistant:</b>");
            sb.AppendLine("• <code>/ai [question]</code> — Ask AI anything (e.g. <i>\"How is disk space?\"</i>)");
            sb.AppendLine("• <code>/ai Remember that...</code> — Teach custom preferences or notes");
            sb.AppendLine("• <code>/ai What do you remember?</code> — List all learned memories");
            sb.AppendLine("• <i>Or simply send any question directly to chat with the AI!</i>");
            sb.AppendLine();
            sb.AppendLine("📱 <b>iOS App Connectivity:</b>");
            sb.AppendLine("• <code>/qr</code> or <code>/connect</code> — Send iOS pairing QR code image to reconnect when disconnected");
            sb.AppendLine();
            sb.AppendLine("📊 <b>Status & System:</b>");
            sb.AppendLine("• <code>/status</code> — Current health, last syncs & tunnel URLs");
            sb.AppendLine("• <code>/health</code> — Run on-demand health check across all services");
            sb.AppendLine("• <code>/pause</code> — Pause automatic backup scheduler");
            sb.AppendLine("• <code>/resume</code> — Resume automatic backup scheduler");
            sb.AppendLine("• <code>/help</code> — Show this guide");
            sb.AppendLine();
            sb.AppendLine("<i>Tip: You can also type commands naturally without a slash (e.g. <code>backup full</code>, <code>qr</code>, <code>status</code>).</i>");

            await SendMessageAsync(sb.ToString(), chatId);
        }

        private static async Task SendStatusMessageAsync(string chatId)
        {
            var localIp = FileDownloadService.GetLocalIpAddress() ?? "127.0.0.1";
            var port = ConfigService.Current.HttpServer?.Port ?? 8080;
            var cloudflare = CloudflareTunnelService.ActiveUrl ?? ConfigService.Current.HttpServer?.CloudflareUrl ?? "Not active";
            var isPaused = BackupManager.Current?.IsPaused == true;

            var ftpStatus = SyncStatusService.GetStatus("FTP");
            var sqlStatus = SyncStatusService.GetStatus("SQL");
            var mcStatus = SyncStatusService.GetStatus("Mailchimp");

            var state = BackupStateTracker.CurrentState;

            var sb = new StringBuilder();
            sb.AppendLine("📊 <b>PinayPal Backup Manager — Current Status</b>");
            sb.AppendLine();
            sb.AppendLine($"🖥️ <b>Host:</b> <code>{EscapeHtml(Environment.MachineName)}</code>");
            sb.AppendLine($"🌐 <b>Local Server:</b> <code>http://{localIp}:{port}</code>");
            sb.AppendLine($"☁️ <b>Cloudflare Tunnel:</b> <code>{EscapeHtml(cloudflare)}</code>");
            sb.AppendLine($"⏱️ <b>Scheduler:</b> {(isPaused ? "⏸️ <b>PAUSED</b>" : "▶️ <b>ACTIVE</b>")}");
            sb.AppendLine();

            sb.AppendLine("💾 <b>Services Status:</b>");
            sb.AppendLine($"• <b>Website (FTP):</b> {FormatServiceStatus(ftpStatus)}");
            sb.AppendLine($"• <b>Database (SQL):</b> {FormatServiceStatus(sqlStatus)}");
            sb.AppendLine($"• <b>Mailchimp:</b> {FormatServiceStatus(mcStatus)}");
            sb.AppendLine();

            if (state.IsBusy)
            {
                sb.AppendLine($"⚙️ <b>Active Operation:</b> <code>{EscapeHtml(state.Service.ToUpperInvariant())} ({state.Progress}%) — {EscapeHtml(state.StatusText)}</code>");
            }
            else
            {
                sb.AppendLine("⚙️ <b>Active Operation:</b> <code>Idle (No running backups)</code>");
            }

            sb.AppendLine();
            sb.AppendLine("<i>Commands: <code>/backup full</code> | <code>/qr</code> | <code>/health</code></i>");

            await SendMessageAsync(sb.ToString(), chatId);
        }

        private static string FormatServiceStatus(SyncStatusInfo info)
        {
            var icon = info.IsOutdated ? "⚠️" : (info.Status == "LATEST" || info.Status == "OK" ? "✅" : "ℹ️");
            var timeStr = info.LocalLatestTimeUtc.HasValue
                ? info.LocalLatestTimeUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : (info.LastCheckedUtc.HasValue ? info.LastCheckedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "Never");
            return $"{icon} <b>{EscapeHtml(info.Status)}</b> (Last: {timeStr})";
        }

        private static async Task RunHealthCheckAndReportAsync(string chatId)
        {
            await SendMessageAsync("🏥 <i>Running global health check across Website, SQL, and Mailchimp...</i>", chatId);

            if (BackupManager.Current != null)
            {
                await BackupManager.Current.RunHealthCheckAsync();
                await Task.Delay(1000);
                await SendStatusMessageAsync(chatId);
            }
            else
            {
                await SendMessageAsync("⚠️ Backup manager instance is not available.", chatId);
            }
        }

        #endregion

        #region Helpers

        public static string EscapeHtml(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static string StripHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var sb = new StringBuilder();
            bool inside = false;
            foreach (var c in html)
            {
                if (c == '<') inside = true;
                else if (c == '>') inside = false;
                else if (!inside) sb.Append(c);
            }
            return sb.ToString();
        }

        #endregion
    }
}
