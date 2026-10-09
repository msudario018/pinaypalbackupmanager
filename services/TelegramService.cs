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
        private static int _consecutivePollFailures;
        private static long _lastUpdateId = 0;
        private static readonly object _stateLock = new();
        private static bool _isPolling = false;
        public static bool IsPolling => _isPolling;

        public static bool IsEnabled => !string.IsNullOrWhiteSpace(NotificationService.Settings.TelegramBotToken);
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
            var res = await SendMessageDetailedAsync(text, targetChatId, parseMode);
            return (res.success, res.message);
        }

        /// <summary>
        /// Sends an HTML-formatted message to Telegram and returns the created message ID for in-place editing.
        /// </summary>
        public static async Task<(bool success, string message, int messageId)> SendMessageDetailedAsync(string text, string? targetChatId = null, string parseMode = "HTML")
        {
            var token = BotToken;
            var chat = !string.IsNullOrWhiteSpace(targetChatId) ? targetChatId : ChatId;

            if (string.IsNullOrWhiteSpace(token))
                return (false, "Telegram Bot Token is not configured.", 0);
            if (string.IsNullOrWhiteSpace(chat))
                return (false, "Telegram Chat ID is not configured.", 0);

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
                    int messageId = 0;
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("result", out var resElem) &&
                            resElem.TryGetProperty("message_id", out var midElem))
                        {
                            messageId = midElem.GetInt32();
                        }
                    }
                    catch { }

                    return (true, "Message sent successfully.", messageId);
                }

                // If HTML parse failed, retry once without parse_mode so the notification is never lost
                if (parseMode == "HTML" && responseBody.Contains("can't parse entities", StringComparison.OrdinalIgnoreCase))
                {
                    LogService.WriteSystemLog("[TELEGRAM] HTML formatting parse failed. Retrying in plain text mode.", "Warning", "SYSTEM");
                    return await SendMessageDetailedAsync(StripHtmlTags(text), targetChatId, "");
                }

                LogService.WriteSystemLog($"[TELEGRAM] SendMessage failed: {responseBody}", "Error", "SYSTEM");
                return (false, $"Telegram API Error: {responseBody}", 0);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[TELEGRAM] SendMessage exception: {ex.Message}", "Error", "SYSTEM");
                return (false, ex.Message, 0);
            }
        }

        /// <summary>
        /// Edits an existing Telegram message in-place (ideal for smooth progress bar updates without notification spam).
        /// </summary>
        public static async Task<bool> EditMessageTextAsync(int messageId, string text, string? targetChatId = null, string parseMode = "HTML")
        {
            var token = BotToken;
            var chat = !string.IsNullOrWhiteSpace(targetChatId) ? targetChatId : ChatId;

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chat) || messageId <= 0)
                return false;

            try
            {
                var url = $"https://api.telegram.org/bot{token}/editMessageText";
                var payload = new Dictionary<string, object>
                {
                    ["chat_id"] = chat,
                    ["message_id"] = messageId,
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
                    return true;

                if (responseBody.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            }
            catch
            {
                return false;
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
                form.Add(new StringContent(chat, Encoding.UTF8), "chat_id");
                form.Add(new StringContent(caption, Encoding.UTF8), "caption");
                form.Add(new StringContent("HTML", Encoding.UTF8), "parse_mode");

                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                form.Add(imageContent, "photo", fileName);

                using var response = await _httpClient.PostAsync(url, form);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return (true, "Photo sent successfully.");
                }

                // If HTML parse failed on caption, retry once without parse_mode
                if (responseBody.Contains("can't parse entities", StringComparison.OrdinalIgnoreCase))
                {
                    LogService.WriteSystemLog("[TELEGRAM] SendPhoto caption HTML parse failed. Retrying with plain text caption.", "Warning", "SYSTEM");
                    using var plainForm = new MultipartFormDataContent();
                    plainForm.Add(new StringContent(chat, Encoding.UTF8), "chat_id");
                    plainForm.Add(new StringContent(StripHtmlTags(caption), Encoding.UTF8), "caption");
                    var plainImageContent = new ByteArrayContent(imageBytes);
                    plainImageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    plainForm.Add(plainImageContent, "photo", fileName);

                    using var retryRes = await _httpClient.PostAsync(url, plainForm);
                    var retryBody = await retryRes.Content.ReadAsStringAsync();
                    if (retryRes.IsSuccessStatusCode)
                    {
                        return (true, "Photo sent successfully (plain text caption fallback).");
                    }
                    responseBody = retryBody;
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

        private class ProgressTrackingInfo
        {
            public int MessageId { get; set; }
            public int LastReportedPercent { get; set; }
            public DateTime LastReportedTime { get; set; }
            public DateTime StartTime { get; set; }
        }

        private static readonly Dictionary<string, ProgressTrackingInfo> _activeProgress = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _progressLock = new();

        /// <summary>
        /// Sends or updates a live progress update in Telegram with a visual progress bar and elapsed time.
        /// </summary>
        public static async Task SendBackupProgressAlertAsync(string serviceName, int percent, string status)
        {
            var s = NotificationService.Settings;
            if (!s.TelegramEnabled || !s.NotifyOnBackupProgress || string.IsNullOrWhiteSpace(s.TelegramBotToken) || string.IsNullOrWhiteSpace(s.TelegramChatId))
                return;

            ProgressTrackingInfo info;
            var now = DateTime.UtcNow;
            bool shouldSend = false;

            lock (_progressLock)
            {
                if (!_activeProgress.TryGetValue(serviceName, out var existing))
                {
                    existing = new ProgressTrackingInfo
                    {
                        MessageId = 0,
                        LastReportedPercent = -1,
                        LastReportedTime = DateTime.MinValue,
                        StartTime = now
                    };
                    _activeProgress[serviceName] = existing;
                }
                info = existing;

                // Throttling criteria:
                // 1. Initial meaningful progress (>= 5%)
                // 2. Milestone jump >= 15% (e.g. 15, 30, 50, 75, 90)
                // 3. Or at least 8 seconds elapsed and progress moved
                // 4. Or complete (100%)
                if (info.LastReportedPercent < 0 && percent >= 5)
                {
                    shouldSend = true;
                }
                else if (percent >= 100 && info.LastReportedPercent < 100)
                {
                    shouldSend = true;
                }
                else if (percent - info.LastReportedPercent >= 15)
                {
                    shouldSend = true;
                }
                else if ((now - info.LastReportedTime).TotalSeconds >= 8 && percent > info.LastReportedPercent)
                {
                    shouldSend = true;
                }

                if (shouldSend)
                {
                    info.LastReportedPercent = percent;
                    info.LastReportedTime = now;
                }
            }

            if (!shouldSend)
                return;

            var bar = GenerateProgressBar(percent, 16);
            var elapsed = now - info.StartTime;
            var elapsedStr = elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s" : $"{elapsed.Seconds}s";

            var sb = new StringBuilder();
            sb.AppendLine($"⏳ <b>[BACKUP PROGRESS] — {EscapeHtml(serviceName.ToUpperInvariant())}</b>");
            sb.AppendLine();
            sb.AppendLine($"📊 <b>Progress:</b> <code>{bar}</code> <b>{percent}%</b>");
            if (!string.IsNullOrWhiteSpace(status))
            {
                sb.AppendLine($"📝 <b>Activity:</b> {EscapeHtml(status)}");
            }
            sb.AppendLine($"⏱️ <b>Elapsed:</b> {elapsedStr}");
            sb.AppendLine($"🖥️ <b>Host:</b> <code>{EscapeHtml(Environment.MachineName)}</code>");

            if (info.MessageId > 0)
            {
                var edited = await EditMessageTextAsync(info.MessageId, sb.ToString());
                if (edited) return;
            }

            var (sent, _, newMsgId) = await SendMessageDetailedAsync(sb.ToString());
            if (sent && newMsgId > 0)
            {
                lock (_progressLock)
                {
                    info.MessageId = newMsgId;
                }
            }
        }

        private static string GenerateProgressBar(int percent, int totalChars = 16)
        {
            var p = Math.Clamp(percent, 0, 100);
            int filled = (int)Math.Round((p / 100.0) * totalChars);
            filled = Math.Clamp(filled, 0, totalChars);
            int empty = totalChars - filled;
            return $"[{new string('█', filled)}{new string('▒', empty)}]";
        }

        /// <summary>
        /// Sends an alert when a backup operation starts, completes, or fails with comprehensive storage, size, and system diagnostics.
        /// </summary>
        public static async Task SendBackupAlertAsync(string serviceName, string status, string details, BackupHistoryService.BackupHistoryEntry? entry = null)
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

            // Clear any active progress tracking for this service
            lock (_progressLock)
            {
                _activeProgress.Remove(serviceName);
            }

            string headerEmoji = isStart ? "🚀" : (isSuccess ? "✅" : "🚨");
            string statusHeader = isStart ? "BACKUP STARTED" : (isSuccess ? "BACKUP COMPLETED" : "BACKUP FAILED");

            var sb = new StringBuilder();
            sb.AppendLine($"{headerEmoji} <b>[{statusHeader}] — {EscapeHtml(serviceName.ToUpperInvariant())}</b>");
            sb.AppendLine();
            sb.AppendLine($"🖥️ <b>Host:</b> <code>{EscapeHtml(Environment.MachineName)}</code>");
            sb.AppendLine($"🕒 <b>Timestamp:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local)");

            if (isSuccess)
            {
                // Current backup size details
                long backupSize = entry?.SizeBytes ?? 0;
                if (backupSize > 0)
                {
                    sb.AppendLine($"💾 <b>Backup Size:</b> {FormatBytes(backupSize)} <code>({backupSize:n0} bytes)</code>");
                }

                // File count & archive path
                if (entry != null && entry.FilesCount > 0)
                {
                    sb.AppendLine($"📄 <b>Files Archived:</b> {entry.FilesCount:n0} files");
                }
                if (entry != null && !string.IsNullOrWhiteSpace(entry.FilePath))
                {
                    sb.AppendLine($"📁 <b>Target Archive:</b> <code>{EscapeHtml(Path.GetFileName(entry.FilePath))}</code>");
                }

                // Duration & speed
                if (entry != null && entry.Duration.TotalSeconds > 0)
                {
                    var speedMbSec = (backupSize > 0 && entry.Duration.TotalSeconds > 0)
                        ? (backupSize / (1024.0 * 1024.0)) / entry.Duration.TotalSeconds
                        : 0;

                    if (speedMbSec > 0.01)
                    {
                        sb.AppendLine($"⏱️ <b>Duration:</b> {entry.Duration.TotalSeconds:F1}s (⚡ <b>Speed:</b> {speedMbSec:F2} MB/s)");
                    }
                    else
                    {
                        sb.AppendLine($"⏱️ <b>Duration:</b> {entry.Duration.TotalSeconds:F1}s");
                    }
                }

                // SHA-256 Checksum
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Checksum))
                {
                    var shortChecksum = entry.Checksum.Length > 16 ? entry.Checksum.Substring(0, 16) + "..." : entry.Checksum;
                    sb.AppendLine($"🔒 <b>Checksum:</b> <code>{EscapeHtml(shortChecksum)}</code> (Verified)");
                }

                // Total storage across all backups on disk
                var (totalBytes, totalCount) = GetTotalStorageStats();
                if (totalBytes > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"📦 <b>Total Backup Storage:</b> {FormatBytes(totalBytes)} ({totalCount:n0} archives on disk)");
                }

                // Storage drive free space
                var (freeBytes, totalDriveBytes, usedPct, driveName) = GetDriveStorageStats(entry?.FilePath);
                if (totalDriveBytes > 0)
                {
                    var freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);
                    var totalGb = totalDriveBytes / (1024.0 * 1024.0 * 1024.0);
                    sb.AppendLine($"💽 <b>Disk Space ({driveName}):</b> {freeGb:F1} GB free / {totalGb:F1} GB ({usedPct}% used)");
                }

                // Next scheduled run
                var nextSched = GetNextScheduledText(serviceName);
                if (!string.IsNullOrWhiteSpace(nextSched))
                {
                    sb.AppendLine($"⏰ <b>Next Scheduled:</b> {EscapeHtml(nextSched)}");
                }

                // Overall service status overview
                sb.AppendLine($"📊 <b>Services Status:</b> {GetOverallServicesStatus()}");
            }
            else if (!string.IsNullOrWhiteSpace(details))
            {
                sb.AppendLine();
                sb.AppendLine($"📋 <b>Details:</b>\n{EscapeHtml(details)}");
            }

            if (isFail)
            {
                sb.AppendLine();
                sb.AppendLine("<i>⚠️ Check desktop app logs or type <code>/status</code> to inspect service state.</i>");
            }
            else if (isSuccess)
            {
                sb.AppendLine();
                sb.AppendLine("<i>💡 Type <code>/status</code> for dashboard, <code>/stats</code> for metrics, or <code>/qr</code> to pair iOS app.</i>");
            }

            await SendMessageAsync(sb.ToString());
        }

        public static (long totalSizeBytes, int archiveCount) GetTotalStorageStats()
        {
            long totalBytes = 0;
            int count = 0;

            try
            {
                var summary = BackupHistoryService.GetSummary();
                if (summary.TotalSizeBytes > 0)
                {
                    totalBytes = summary.TotalSizeBytes;
                    count = summary.SuccessfulBackups;
                }
            }
            catch { }

            try
            {
                var folders = new[]
                {
                    BackupConfig.FtpLocalFolder,
                    BackupConfig.SqlLocalFolder,
                    BackupConfig.MailchimpFolder,
                    BackupConfig.NetworkDriveFolder
                };

                long physicalBytes = 0;
                int physicalCount = 0;

                foreach (var folder in folders.Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f)).Distinct())
                {
                    var dir = new DirectoryInfo(folder);
                    foreach (var file in dir.EnumerateFiles("*.*", SearchOption.AllDirectories))
                    {
                        if (file.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
                            file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                            file.Extension.Equals(".filepart", StringComparison.OrdinalIgnoreCase))
                            continue;

                        physicalBytes += file.Length;
                        physicalCount++;
                    }
                }

                if (physicalBytes > 0)
                {
                    totalBytes = physicalBytes;
                    count = physicalCount;
                }
            }
            catch { }

            return (totalBytes, count);
        }

        public static (long freeBytes, long totalBytes, int usedPercent, string driveName) GetDriveStorageStats(string? targetPath = null)
        {
            try
            {
                var path = targetPath;
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    path = BackupConfig.FtpLocalFolder;
                }
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    path = AppDomain.CurrentDomain.BaseDirectory;
                }

                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var d = new DriveInfo(root);
                    if (d.IsReady)
                    {
                        var used = d.TotalSize - d.AvailableFreeSpace;
                        var pct = d.TotalSize > 0 ? (int)Math.Round((used * 100.0) / d.TotalSize) : 0;
                        return (d.AvailableFreeSpace, d.TotalSize, pct, d.Name);
                    }
                }
            }
            catch { }

            return (0, 0, 0, "C:\\");
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int i = 0;
            while (val >= 1024 && i < units.Length - 1)
            {
                val /= 1024;
                i++;
            }
            return $"{val:0.##} {units[i]}";
        }

        public static string GetNextScheduledText(string service)
        {
            try
            {
                var svc = service.ToLowerInvariant();
                DateTime next;
                if (svc.Contains("ftp"))
                    next = BackupManager.NextFtpDailySyncMnl;
                else if (svc.Contains("sql"))
                    next = BackupManager.NextSqlDailySyncMnl;
                else if (svc.Contains("mailchimp"))
                    next = BackupManager.NextMailchimpDailySyncMnl;
                else
                    return "Scheduled daily";

                return $"{next:yyyy-MM-dd hh:mm tt} (MNL)";
            }
            catch
            {
                return "Configured schedule";
            }
        }

        public static string GetOverallServicesStatus()
        {
            try
            {
                var history = BackupHistoryService.GetHistory();
                string StatusFor(string svc)
                {
                    var last = history.FirstOrDefault(e => e.Service.Equals(svc, StringComparison.OrdinalIgnoreCase));
                    if (last == null) return "⚪ Idle";
                    if (last.Status.Equals("Success", StringComparison.OrdinalIgnoreCase) || last.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)) return "🟢 OK";
                    if (last.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase)) return "🔴 Error";
                    return "🟡 Running";
                }

                return $"FTP: {StatusFor("FTP")} | SQL: {StatusFor("SQL")} | MC: {StatusFor("Mailchimp")}";
            }
            catch
            {
                return "All services active";
            }
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
                version = BackupConfig.AppVersion
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
            string localIp = "127.0.0.1";
            int port = 8080;
            string pin = "";
            string cloudflare = "";

            try
            {
                var bytes = GenerateConnectionQrBytes(out _, out localIp, out port, out pin, out cloudflare);

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

                var photoResult = await SendPhotoAsync(bytes, "pinaypal_ios_pair_qr.png", caption.ToString(), targetChatId);
                if (photoResult.success)
                {
                    return photoResult;
                }

                LogService.WriteSystemLog($"[TELEGRAM] QR photo dispatch failed: {photoResult.message}. Sending text credentials fallback.", "Warning", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[TELEGRAM] QR dispatch exception: {ex.Message}. Sending text credentials fallback.", "Warning", "SYSTEM");
            }

            // Fallback: send text message with pairing details so user is never left hanging
            try
            {
                if (localIp == "127.0.0.1")
                {
                    localIp = FileDownloadService.GetLocalIpAddress() ?? "127.0.0.1";
                    port = ConfigService.Current.HttpServer?.Port ?? 8080;
                    pin = ConfigService.Current.HttpServer?.WebPin ?? "";
                    cloudflare = CloudflareTunnelService.ActiveUrl ?? ConfigService.Current.HttpServer?.CloudflareUrl ?? "";
                }

                var fallbackText = new StringBuilder();
                fallbackText.AppendLine("📱 <b>PinayPal iOS App Reconnection Credentials</b>");
                fallbackText.AppendLine();
                fallbackText.AppendLine($"🖥️ <b>Host:</b> <code>{EscapeHtml(Environment.MachineName)}</code>");
                fallbackText.AppendLine($"🔑 <b>Web Access PIN:</b> <code>{EscapeHtml(string.IsNullOrEmpty(pin) ? "None (Open)" : pin)}</code>");
                fallbackText.AppendLine($"🌐 <b>Local URL:</b> <code>http://{localIp}:{port}</code>");
                if (!string.IsNullOrWhiteSpace(cloudflare))
                {
                    fallbackText.AppendLine($"☁️ <b>Cloudflare Tunnel:</b> <code>{EscapeHtml(cloudflare)}</code>");
                }
                fallbackText.AppendLine();
                fallbackText.AppendLine("<b>Manual Reconnect in iOS App:</b>");
                fallbackText.AppendLine("Open <i>Settings → Server URL</i> in the iOS app, paste the URL and PIN above to restore connection!");

                await SendMessageAsync(fallbackText.ToString(), targetChatId);
                return (true, "Sent connection credentials via text fallback.");
            }
            catch (Exception fallbackEx)
            {
                LogService.WriteSystemLog($"[TELEGRAM] QR text fallback failed: {fallbackEx.Message}", "Error", "SYSTEM");
                return (false, fallbackEx.Message);
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

            // Clear any webhook before polling starts so getUpdates does not fail with HTTP 409 Conflict
            try
            {
                var delWebhookUrl = $"https://api.telegram.org/bot{token}/deleteWebhook?drop_pending_updates=false";
                await _httpClient.PostAsync(delWebhookUrl, null, ct);
                LogService.WriteSystemLog("[TELEGRAM] Webhook cleared to ensure long-polling operates cleanly.", "Information", "SYSTEM");
            }
            catch { }

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
                        var status = (int)response.StatusCode;
                        string apiError = "";
                        try
                        {
                            var errBody = await response.Content.ReadAsStringAsync(ct);
                            using var errDoc = JsonDocument.Parse(errBody);
                            if (errDoc.RootElement.TryGetProperty("description", out var desc))
                                apiError = desc.GetString() ?? "";
                        }
                        catch { /* body wasn't JSON - status alone is still useful */ }

                        // If Telegram returns 409 conflict, delete the conflicting webhook immediately
                        if (status == 409)
                        {
                            try
                            {
                                var delUrl = $"https://api.telegram.org/bot{token}/deleteWebhook?drop_pending_updates=false";
                                await _httpClient.PostAsync(delUrl, null, ct);
                                LogService.WriteSystemLog("[TELEGRAM] Auto-cleared conflicting webhook during polling.", "Information", "SYSTEM");
                            }
                            catch { }
                        }

                        _consecutivePollFailures++;
                        LogService.WriteSystemLog(
                            $"[TELEGRAM] getUpdates failed (HTTP {status}) {apiError}. " +
                            $"Consecutive failures: {_consecutivePollFailures}. " +
                            "Check the Bot Token in Settings → Telegram.",
                            _consecutivePollFailures >= 3 ? "Error" : "Warning",
                            "SYSTEM");

                        await Task.Delay(4000, ct);
                        continue;
                    }

                    _consecutivePollFailures = 0;

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

                            JsonElement messageElem = default;
                            bool hasMessage = false;

                            if (update.TryGetProperty("message", out messageElem)) hasMessage = true;
                            else if (update.TryGetProperty("channel_post", out messageElem)) hasMessage = true;
                            else if (update.TryGetProperty("edited_message", out messageElem)) hasMessage = true;
                            else if (update.TryGetProperty("edited_channel_post", out messageElem)) hasMessage = true;

                            if (hasMessage)
                            {
                                _ = Task.Run(() => ProcessIncomingMessageAsync(messageElem), ct);
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

                string senderUserId = "";
                string senderUsername = "";
                if (message.TryGetProperty("from", out var fromElem))
                {
                    if (fromElem.TryGetProperty("id", out var fId)) senderUserId = fId.ToString();
                    if (fromElem.TryGetProperty("username", out var uName)) senderUsername = uName.GetString() ?? "";
                }

                LogService.WriteSystemLog($"[TELEGRAM] Incoming message from {senderChatId} (User: {senderUserId} @{senderUsername}): '{text}'", "Information", "SYSTEM");

                var configuredChatId = ChatId;

                // If Chat ID is not configured yet, auto-bind to the first active user messaging the bot
                if (string.IsNullOrWhiteSpace(configuredChatId))
                {
                    configuredChatId = senderChatId;
                    NotificationService.Settings.TelegramChatId = senderChatId;
                    NotificationService.Settings.TelegramEnabled = true;
                    NotificationService.SaveSettings();
                    LogService.WriteSystemLog($"[TELEGRAM] Auto-linked authorized Chat ID to {senderChatId} ({senderUsername})", "Information", "SYSTEM");
                    await SendMessageAsync($"✅ <b>PinayPal Bot Linked Successfully!</b>\n\nConnected to host <code>{EscapeHtml(Environment.MachineName)}</code>.\nYour Chat ID: <code>{senderChatId}</code> has been automatically saved.", senderChatId);
                }
                else
                {
                    // Permissive security authorization: check sender chat ID, user ID (for groups), and username
                    bool isAuthorized = string.Equals(senderChatId, configuredChatId, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(senderUserId) && string.Equals(senderUserId, configuredChatId, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrEmpty(senderUsername) && string.Equals(configuredChatId.TrimStart('@'), senderUsername.TrimStart('@'), StringComparison.OrdinalIgnoreCase));

                    if (!isAuthorized)
                    {
                        LogService.WriteSystemLog($"[TELEGRAM] Unauthorized command attempt from Chat ID {senderChatId} (User: {senderUserId} @{senderUsername}): '{text}'", "Warning", "SECURITY");
                        var deniedMsg = "⛔ <b>Access Denied</b>\n\nThis PinayPal Backup Manager bot is linked to another user.\n" +
                                       $"Your Chat ID: <code>{senderChatId}</code>\n" +
                                       "If this is your desktop, update the Chat ID in desktop app Settings → Telegram.";
                        await SendMessageAsync(deniedMsg, senderChatId);
                        return;
                    }
                }

                // Process authorized command with protective error reply so user is NEVER left in silence
                try
                {
                    await HandleCommandAsync(text.Trim(), senderChatId);
                }
                catch (Exception cmdEx)
                {
                    LogService.WriteSystemLog($"[TELEGRAM] Command '{text}' execution failed: {cmdEx.Message}", "Error", "SYSTEM");
                    await SendMessageAsync($"⚠️ <b>Command Execution Issue:</b> {EscapeHtml(cmdEx.Message)}\n\nType <code>/help</code> for available commands.", senderChatId);
                }
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
                    var (qrOk, qrMsg) = await SendConnectionQrAsync(chatId);
                    if (!qrOk)
                    {
                        await SendMessageAsync($"⚠️ Could not dispatch QR code: {EscapeHtml(qrMsg)}", chatId);
                    }
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

                case "stats":
                case "statistics":
                    await SendStatisticsMessageAsync(chatId);
                    break;

                case "errors":
                case "error":
                case "logs":
                    await SendRecentErrorsMessageAsync(chatId);
                    break;

                case "ai":
                case "ask":
                    await HandleAiCommandAsync(tokens, rawText, chatId);
                    break;

                default:
                    // Support natural typing without leading slash (e.g. "backup full", "qr", "status", "health", "stats")
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
                    else if (tokens.Length >= 1 && (string.Equals(tokens[0], "stats", StringComparison.OrdinalIgnoreCase) || string.Equals(tokens[0], "statistics", StringComparison.OrdinalIgnoreCase)))
                    {
                        await SendStatisticsMessageAsync(chatId);
                    }
                    else if (tokens.Length >= 1 && (string.Equals(tokens[0], "errors", StringComparison.OrdinalIgnoreCase) || string.Equals(tokens[0], "logs", StringComparison.OrdinalIgnoreCase)))
                    {
                        await SendRecentErrorsMessageAsync(chatId);
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
            sb.AppendLine("• <code>/stats</code> — Overall backup statistics, total storage & success rate");
            sb.AppendLine("• <code>/errors</code> — Recent application error logs & failed backups");
            sb.AppendLine("• <code>/health</code> — Run on-demand health check across all services");
            sb.AppendLine("• <code>/pause</code> — Pause automatic backup scheduler");
            sb.AppendLine("• <code>/resume</code> — Resume automatic backup scheduler");
            sb.AppendLine("• <code>/help</code> — Show this guide");
            sb.AppendLine();
            sb.AppendLine("<i>Tip: You can also type commands naturally without a slash (e.g. <code>backup full</code>, <code>qr</code>, <code>status</code>, <code>stats</code>).</i>");

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
            sb.AppendLine("<i>Commands: <code>/backup full</code> | <code>/stats</code> | <code>/health</code> | <code>/qr</code></i>");

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

        private static async Task SendStatisticsMessageAsync(string chatId)
        {
            var summary = BackupHistoryService.GetSummary();
            var (totalStorage, archiveCount) = GetTotalStorageStats();
            var (freeBytes, totalDriveBytes, usedPct, driveName) = GetDriveStorageStats();
            var freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);
            var totalGb = totalDriveBytes / (1024.0 * 1024.0 * 1024.0);

            var sb = new StringBuilder();
            sb.AppendLine("📊 <b>PinayPal Backup Manager — Enterprise Statistics</b>");
            sb.AppendLine();
            sb.AppendLine($"📈 <b>Total Backups:</b> {summary.TotalBackups:n0}");
            sb.AppendLine($"✅ <b>Successful:</b> {summary.SuccessfulBackups:n0} ({summary.SuccessRate:F1}%)");
            sb.AppendLine($"🚨 <b>Failed:</b> {summary.FailedBackups:n0}");
            sb.AppendLine($"⏱️ <b>Avg Duration:</b> {summary.AverageDuration.TotalSeconds:F1}s");
            sb.AppendLine($"📦 <b>Total Backup Storage:</b> {FormatBytes(totalStorage)} ({archiveCount:n0} archives)");
            sb.AppendLine($"💽 <b>Disk Space ({driveName}):</b> {freeGb:F1} GB free / {totalGb:F1} GB ({usedPct}% used)");
            sb.AppendLine();

            if (summary.BackupsByService.Count > 0)
            {
                sb.AppendLine("📋 <b>Service Breakdown:</b>");
                foreach (var (svc, count) in summary.BackupsByService)
                {
                    sb.AppendLine($"• <b>{EscapeHtml(svc)}:</b> {count:n0} backups");
                }
            }

            if (summary.LastSuccessfulBackupTime > DateTime.MinValue)
            {
                sb.AppendLine();
                sb.AppendLine($"🕒 <b>Last Successful Backup:</b> {summary.LastSuccessfulBackupTime:yyyy-MM-dd HH:mm:ss} UTC");
            }

            sb.AppendLine();
            sb.AppendLine("<i>💡 Type <code>/backup full</code> to run backup or <code>/health</code> for diagnostics.</i>");

            await SendMessageAsync(sb.ToString(), chatId);
        }

        private static async Task SendRecentErrorsMessageAsync(string chatId)
        {
            var errors = ErrorReportingService.GetErrorReports(5);
            var failedBackups = BackupHistoryService.GetFailedBackups(5);

            var sb = new StringBuilder();
            sb.AppendLine("🚨 <b>PinayPal Backup Manager — Recent Error Reports</b>");
            sb.AppendLine();

            if (errors.Count == 0 && failedBackups.Count == 0)
            {
                sb.AppendLine("✅ <b>No errors reported! System is running cleanly.</b>");
            }
            else
            {
                if (failedBackups.Count > 0)
                {
                    sb.AppendLine("<b>Recent Failed Backups:</b>");
                    foreach (var fail in failedBackups.Take(3))
                    {
                        sb.AppendLine($"• <b>[{EscapeHtml(fail.Service)}]</b> {fail.Timestamp:yyyy-MM-dd HH:mm} UTC");
                        sb.AppendLine($"  <i>{EscapeHtml(fail.ErrorMessage)}</i>");
                    }
                    sb.AppendLine();
                }

                if (errors.Count > 0)
                {
                    sb.AppendLine("<b>Recent Application Errors:</b>");
                    foreach (var err in errors.Take(3))
                    {
                        sb.AppendLine($"• <b>[{EscapeHtml(err.Source)}]</b> {err.Timestamp:yyyy-MM-dd HH:mm} UTC");
                        sb.AppendLine($"  <i>{EscapeHtml(err.Message)}</i>");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("<i>💡 Type <code>/status</code> for system overview or <code>/health</code> for diagnostic check.</i>");

            await SendMessageAsync(sb.ToString(), chatId);
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
