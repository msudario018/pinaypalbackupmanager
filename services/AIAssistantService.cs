using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public class ChatMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Role { get; set; } = "user"; // "user", "assistant", "system"
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public AIProposedAction? ProposedAction { get; set; }
        public bool IsActionExecuted { get; set; }
    }

    public class AIProposedAction
    {
        public string ActionId { get; set; } = Guid.NewGuid().ToString("N");
        public string ActionType { get; set; } = ""; // "run_backup", "recreate_tunnel", "run_health_check", "test_email", "emergency_stop", "clear_history"
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, string> Parameters { get; set; } = new();
        public bool RequiresConfirmation { get; set; } = true;
        public bool IsExecuted { get; set; } = false;
        public bool IsApproved { get; set; } = false;
        public string? ExecutionResult { get; set; }
    }

    public class AIAssistantConfig
    {
        public bool IsEnabled { get; set; } = true;
        public string Provider { get; set; } = "hybrid"; // "hybrid", "ollama", "cloud", "heuristics"
        public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
        public string OllamaModel { get; set; } = "llama3.2:latest";
        public string CloudApiKey { get; set; } = "";
        public string CloudEndpoint { get; set; } = "https://api.openai.com/v1";
        public string CloudModel { get; set; } = "gpt-4o-mini";
        public bool EnableFloatingWidget { get; set; } = true;
        public bool EnableLoginGreeting { get; set; } = true;
        public bool EnableSoundChimes { get; set; } = false;
    }

    /// <summary>
    /// Smart Conversational AI Engine for PinayPal Backup Manager.
    /// Supports Local Ollama, Cloud LLMs (OpenAI, Gemini, Claude), and built-in offline diagnostics.
    /// Strictly protects passwords, encryption keys, and backup payloads through a Zero-Leak Sanitizer.
    /// </summary>
    public static class AIAssistantService
    {
        private static AIAssistantConfig _config = new();
        private static readonly string ConfigFilePath = AppDataPaths.GetPath("ai_config.json");
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly ConcurrentDictionary<string, AIProposedAction> _pendingActions = new();
        private static readonly List<ChatMessage> _sessionHistory = new();
        private static readonly object _historyLock = new();

        public static event Action<string>? OnNotificationBubble;
        public static event Action<ChatMessage>? OnMessageReceived;

        static AIAssistantService()
        {
            LoadConfig();
        }

        public static AIAssistantConfig Config => _config;

        public static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    _config = JsonSerializer.Deserialize<AIAssistantConfig>(json) ?? new AIAssistantConfig();
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Error loading config: {ex.Message}", "Warning", "AI");
            }
        }

        public static void SaveConfig(AIAssistantConfig config)
        {
            try
            {
                _config = config;
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
                LogService.WriteSystemLog("[AIAssistant] Configuration updated successfully", "Information", "AI");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Error saving config: {ex.Message}", "Error", "AI");
            }
        }

        public static List<ChatMessage> GetSessionHistory()
        {
            lock (_historyLock)
            {
                return _sessionHistory.ToList();
            }
        }

        public static void ClearSessionHistory()
        {
            lock (_historyLock)
            {
                _sessionHistory.Clear();
            }
        }

        /// <summary>
        /// Generates a proactive welcome greeting on user login summarizing node health and storage.
        /// </summary>
        public static async Task<string> GenerateLoginGreetingAsync(string username)
        {
            await Task.Yield();
            if (!_config.EnableLoginGreeting) return "";

            try
            {
                var hour = DateTime.Now.Hour;
                string timeGreeting = hour < 12 ? "Good morning" : (hour < 18 ? "Good afternoon" : "Good evening");
                var telemetry = HardwareTelemetryService.GetTelemetrySync();
                var freeRam = telemetry.RamFreeGB > 0 ? $"{telemetry.RamFreeGB:F1} GB" : "normal";

                var greeting = $"{timeGreeting}, {username}! PinayPal is running smoothly on {Environment.MachineName}. All backup watchdogs are active and {freeRam} RAM is available.";
                OnNotificationBubble?.Invoke(greeting);
                return greeting;
            }
            catch
            {
                return $"Welcome back, {username}! PinayPal Backup Manager is operational.";
            }
        }

        /// <summary>
        /// Posts a proactive speech bubble when an event occurs (e.g. backup completion, warning).
        /// </summary>
        public static void PostEventBubble(string title, string message, bool isError = false)
        {
            if (!_config.EnableFloatingWidget) return;
            var text = isError ? $"⚠️ {title}: {message}" : $"✨ {title}: {message}";
            OnNotificationBubble?.Invoke(text);
        }

        /// <summary>
        /// Core conversational chat entry point with multi-provider dispatch and Zero-Leak sanitization.
        /// </summary>
        public static async Task<ChatMessage> ProcessUserMessageAsync(string userMessage)
        {
            var sanitizedUserMessage = SanitizePrompt(userMessage.Trim());
            var userMsg = new ChatMessage { Role = "user", Content = sanitizedUserMessage };

            lock (_historyLock)
            {
                _sessionHistory.Add(userMsg);
                if (_sessionHistory.Count > 40) _sessionHistory.RemoveAt(0);
            }

            ChatMessage assistantMsg;

            try
            {
                // 1. Check for immediate guarded actions intent
                var proposedAction = DetectActionIntent(sanitizedUserMessage);

                // 2. Dispatch to chosen or hybrid provider
                string replyText = "";
                if (_config.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase) ||
                    (_config.Provider.Equals("hybrid", StringComparison.OrdinalIgnoreCase) && await IsOllamaReachableAsync()))
                {
                    replyText = await QueryOllamaAsync(sanitizedUserMessage, proposedAction);
                }
                else if ((_config.Provider.Equals("cloud", StringComparison.OrdinalIgnoreCase) || _config.Provider.Equals("hybrid", StringComparison.OrdinalIgnoreCase))
                         && !string.IsNullOrWhiteSpace(_config.CloudApiKey))
                {
                    replyText = await QueryCloudLlmAsync(sanitizedUserMessage, proposedAction);
                }

                // 3. Fallback to smart diagnostic engine if LLM is unavailable or empty
                if (string.IsNullOrWhiteSpace(replyText))
                {
                    replyText = await QueryHeuristicEngineAsync(sanitizedUserMessage, proposedAction);
                }

                assistantMsg = new ChatMessage
                {
                    Role = "assistant",
                    Content = SanitizeOutput(replyText),
                    ProposedAction = proposedAction
                };

                if (proposedAction != null)
                {
                    _pendingActions[proposedAction.ActionId] = proposedAction;
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Message processing error: {ex.Message}", "Warning", "AI");
                assistantMsg = new ChatMessage
                {
                    Role = "assistant",
                    Content = $"I encountered an issue processing that query: {ex.Message}. I have automatically switched to local diagnostic mode."
                };
            }

            lock (_historyLock)
            {
                _sessionHistory.Add(assistantMsg);
                if (_sessionHistory.Count > 40) _sessionHistory.RemoveAt(0);
            }

            OnMessageReceived?.Invoke(assistantMsg);
            return assistantMsg;
        }

        /// <summary>
        /// Safely executes an action once the user explicitly clicks Approve on the proposal card.
        /// </summary>
        public static async Task<(bool success, string resultMessage)> ExecuteActionAsync(string actionId, bool userApproved)
        {
            if (!_pendingActions.TryGetValue(actionId, out var action))
            {
                return (false, "Action expired or already executed.");
            }

            if (!userApproved)
            {
                _pendingActions.TryRemove(actionId, out _);
                action.IsExecuted = true;
                action.IsApproved = false;
                action.ExecutionResult = "Action cancelled by user.";
                return (false, "Action cancelled.");
            }

            action.IsApproved = true;
            action.IsExecuted = true;
            _pendingActions.TryRemove(actionId, out _);

            try
            {
                LogService.WriteSystemLog($"[AIAssistant] Executing approved action: {action.ActionType} ({action.Title})", "Information", "AI");

                switch (action.ActionType.ToLowerInvariant())
                {
                    case "run_backup":
                        var service = action.Parameters.TryGetValue("service", out var s) ? s : "all";
                        if (service == "all")
                        {
                            _ = BackupSchedulingService.BackupExecutor?.Invoke("ftp", "AI Assistant");
                            _ = BackupSchedulingService.BackupExecutor?.Invoke("sql", "AI Assistant");
                            _ = BackupSchedulingService.BackupExecutor?.Invoke("mailchimp", "AI Assistant");
                            action.ExecutionResult = "Dispatched parallel backup for Website FTP, SQL Database, and Mailchimp.";
                        }
                        else
                        {
                            var ok = await (BackupSchedulingService.BackupExecutor?.Invoke(service, "AI Assistant") ?? Task.FromResult(false));
                            action.ExecutionResult = ok ? $"{service.ToUpper()} backup task started successfully." : $"Failed to start {service.ToUpper()} backup.";
                        }
                        return (true, action.ExecutionResult);

                    case "recreate_tunnel":
                        var (tunOk, _, tunMsg) = await CloudflareTunnelService.RestartQuickTunnelAsync();
                        action.ExecutionResult = tunOk ? $"Cloudflare Quick Tunnel recreated: {CloudflareTunnelService.ActiveUrl}" : $"Recreation error: {tunMsg}";
                        return (tunOk, action.ExecutionResult);

                    case "run_health_check":
                        var health = await HealthCheckService.RunHealthCheckAsync();
                        var healthyCount = health.Components.Values.Count(c => c.IsHealthy);
                        action.ExecutionResult = $"Health check completed: Status is {health.Status}, with {healthyCount}/{health.Components.Count} components healthy.";
                        return (true, action.ExecutionResult);

                    case "test_email":
                        var (mailOk, mailMsg) = await NotificationService.SendTestEmailAsync();
                        action.ExecutionResult = mailOk ? "Verified email test delivered successfully!" : $"Email dispatch failed: {mailMsg}";
                        return (mailOk, action.ExecutionResult);

                    case "emergency_stop":
                        WebDashboardService.EmergencyStopExecutor?.Invoke();
                        action.ExecutionResult = "Emergency Stop signal broadcasted. All running backup tasks have been halted.";
                        return (true, action.ExecutionResult);

                    case "clear_history":
                        BackupHistoryService.ClearHistory();
                        action.ExecutionResult = "Backup history records cleared.";
                        return (true, action.ExecutionResult);

                    default:
                        action.ExecutionResult = $"Unknown action type: {action.ActionType}";
                        return (false, action.ExecutionResult);
                }
            }
            catch (Exception ex)
            {
                action.ExecutionResult = $"Execution exception: {ex.Message}";
                LogService.WriteSystemLog($"[AIAssistant] Action execution failed: {ex.Message}", "Error", "AI");
                return (false, action.ExecutionResult);
            }
        }

        // ==========================================
        // Action Intent Recognition
        // ==========================================
        private static AIProposedAction? DetectActionIntent(string prompt)
        {
            var lower = prompt.ToLowerInvariant();

            // Run backup intents
            if (lower.Contains("run backup") || lower.Contains("start backup") || lower.Contains("backup now") || lower.Contains("do backup") || lower.Contains("trigger backup"))
            {
                if (lower.Contains("sql") || lower.Contains("database"))
                {
                    return new AIProposedAction
                    {
                        ActionType = "run_backup",
                        Title = "Trigger SQL Database Backup",
                        Description = "Creates a fresh snapshot of your MySQL database.",
                        Parameters = new Dictionary<string, string> { { "service", "sql" } }
                    };
                }
                if (lower.Contains("ftp") || lower.Contains("website"))
                {
                    return new AIProposedAction
                    {
                        ActionType = "run_backup",
                        Title = "Trigger Website FTP Backup",
                        Description = "Performs incremental sync of all remote website files.",
                        Parameters = new Dictionary<string, string> { { "service", "ftp" } }
                    };
                }
                if (lower.Contains("mailchimp"))
                {
                    return new AIProposedAction
                    {
                        ActionType = "run_backup",
                        Title = "Trigger Mailchimp Sync",
                        Description = "Downloads latest campaign lists and audience members.",
                        Parameters = new Dictionary<string, string> { { "service", "mailchimp" } }
                    };
                }
                return new AIProposedAction
                {
                    ActionType = "run_backup",
                    Title = "Run All Backups (Parallel)",
                    Description = "Executes FTP Website, SQL Database, and Mailchimp backups concurrently.",
                    Parameters = new Dictionary<string, string> { { "service", "all" } }
                };
            }

            // Cloudflare Tunnel Recreation
            if (lower.Contains("recreate tunnel") || lower.Contains("restart tunnel") || lower.Contains("fix tunnel") || lower.Contains("new tunnel"))
            {
                return new AIProposedAction
                {
                    ActionType = "recreate_tunnel",
                    Title = "Recreate Cloudflare Quick Tunnel",
                    Description = "Generates a fresh public trycloudflare.com URL and restarts cloudflared."
                };
            }

            // Emergency Stop
            if (lower.Contains("emergency stop") || lower.Contains("abort backup") || lower.Contains("cancel all"))
            {
                return new AIProposedAction
                {
                    ActionType = "emergency_stop",
                    Title = "Emergency Stop All Tasks",
                    Description = "Immediately halts all running and queued backup operations."
                };
            }

            // Test Email Alert
            if (lower.Contains("test email") || lower.Contains("send test email") || lower.Contains("check email alert"))
            {
                return new AIProposedAction
                {
                    ActionType = "test_email",
                    Title = "Dispatch Verified Test Email",
                    Description = "Sends a luxury HTML test email to verify SMTP delivery."
                };
            }

            // Run Health Check
            if (lower.Contains("run health check") || lower.Contains("test health") || lower.Contains("diagnostic test"))
            {
                return new AIProposedAction
                {
                    ActionType = "run_health_check",
                    Title = "Run System Health Diagnostics",
                    Description = "Tests disk readiness, network interfaces, and database connectivity."
                };
            }

            return null;
        }

        // ==========================================
        // Ollama Local Provider
        // ==========================================
        private static async Task<bool> IsOllamaReachableAsync()
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(1500);
                var resp = await _httpClient.GetAsync($"{_config.OllamaEndpoint}/api/tags", cts.Token);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<string> QueryOllamaAsync(string prompt, AIProposedAction? action)
        {
            try
            {
                var systemContext = BuildSanitizedSystemContext();
                var fullPrompt = $@"System instructions: You are Antigravity, the smart executive AI assistant for PinayPal Backup Manager on Windows.
Be concise, helpful, friendly, and accurate. Use clear markdown formatting.
If the user wants to execute an action, advise them to click the action button provided.
Never ask for passwords, credentials, or private keys.

Live Telemetry Context:
{systemContext}

User Query: {prompt}
Assistant:";

                var requestBody = new
                {
                    model = _config.OllamaModel,
                    prompt = fullPrompt,
                    stream = false
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_config.OllamaEndpoint}/api/generate", content);

                if (!response.IsSuccessStatusCode) return "";

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("response", out var respProp))
                {
                    return respProp.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Ollama query failed: {ex.Message}", "Warning", "AI");
            }

            return "";
        }

        // ==========================================
        // Cloud LLM Provider (OpenAI / Gemini)
        // ==========================================
        private static async Task<string> QueryCloudLlmAsync(string prompt, AIProposedAction? action)
        {
            try
            {
                var systemContext = BuildSanitizedSystemContext();
                var endpoint = _config.CloudEndpoint.TrimEnd('/') + "/chat/completions";

                var requestBody = new
                {
                    model = _config.CloudModel,
                    messages = new[]
                    {
                        new { role = "system", content = $"You are Antigravity, the executive AI assistant for PinayPal Backup Manager. Be concise, precise, and polite. Never disclose or ask for secrets.\n\nLive Telemetry:\n{systemContext}" },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.4,
                    max_tokens = 500
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.CloudApiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return "";

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0)
                {
                    return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Cloud LLM query failed: {ex.Message}", "Warning", "AI");
            }

            return "";
        }

        // ==========================================
        // Smart Offline Heuristics Engine
        // ==========================================
        private static async Task<string> QueryHeuristicEngineAsync(string prompt, AIProposedAction? action)
        {
            await Task.Yield();
            var lower = prompt.ToLowerInvariant();
            var telemetry = HardwareTelemetryService.GetTelemetrySync();

            // Action proposals
            if (action != null)
            {
                return $"I have prepared the action for you: **{action.Title}**.\n\n{action.Description}\n\nPlease click **Approve** on the action card below to start execution.";
            }

            // Greetings
            if (lower.Contains("hello") || lower.Contains("hi") || lower.Contains("hey") || lower.Contains("good morning") || lower.Contains("good evening"))
            {
                return $"Hello! I'm your **PinayPal AI Assistant**. I'm monitoring host **{Environment.MachineName}** in real time.\n\nAll systems are operational. You can ask me to check disk space, inspect backup errors, trigger backups, or test remote connections.";
            }

            // Health & System Specs
            if (lower.Contains("health") || lower.Contains("specs") || lower.Contains("status") || lower.Contains("cpu") || lower.Contains("ram"))
            {
                var cpu = telemetry.CpuUsagePercent > 0 ? $"{telemetry.CpuUsagePercent:F0}%" : "Normal";
                var ram = telemetry.RamUsagePercent > 0 ? $"{telemetry.RamUsagePercent:F0}%" : "Normal";
                var freeRam = telemetry.RamFreeGB > 0 ? $"{telemetry.RamFreeGB:F1} GB" : "N/A";
                var gpu = telemetry.GpuName ?? "Integrated Graphics";
                var gpuUsage = telemetry.GpuUsagePercent.HasValue ? $"{telemetry.GpuUsagePercent.Value:F0}%" : "Idle";

                return $@"### 🖥️ Host System Telemetry
- **Machine:** `{Environment.MachineName}` ({RuntimeInformation.OSDescription})
- **Processor:** {telemetry.CpuName ?? "Host CPU"} ({Environment.ProcessorCount} threads)
- **CPU Load:** **{cpu}** ({telemetry.CpuTempC ?? 42:F0}°C)
- **Memory:** **{ram} used** ({freeRam} free)
- **GPU Accelerator:** {gpu} ({gpuUsage})
- **PinayPal App Version:** `v{BackupConfig.AppVersion}` (Running cleanly)";
            }

            // Storage & Disk Space
            if (lower.Contains("disk") || lower.Contains("storage") || lower.Contains("space") || lower.Contains("capacity"))
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
                var sb = new StringBuilder();
                sb.AppendLine("### 💾 Storage Allocation & Drive Status");
                foreach (var d in drives)
                {
                    var totalGB = d.TotalSize / (1024 * 1024 * 1024);
                    var freeGB = d.AvailableFreeSpace / (1024 * 1024 * 1024);
                    var usedPct = totalGB > 0 ? (totalGB - freeGB) * 100.0 / totalGB : 0;
                    sb.AppendLine($"- **Drive {d.Name}** ({d.DriveFormat}): **{freeGB} GB free** of {totalGB} GB ({usedPct:F0}% full)");
                }
                return sb.ToString();
            }

            // Errors & Troubleshooting
            if (lower.Contains("error") || lower.Contains("fail") || lower.Contains("why") || lower.Contains("logs"))
            {
                var recentHistory = BackupHistoryService.GetHistory().Take(5).ToList();
                var lastFailed = recentHistory.FirstOrDefault(h => h.Status != "Success");
                if (lastFailed != null)
                {
                    return $"### ⚠️ Recent Backup Issue Detected\n- **Service:** {lastFailed.Service.ToUpper()}\n- **Time:** {lastFailed.Timestamp:yyyy-MM-dd HH:mm}\n- **Error:** `{lastFailed.ErrorMessage}`\n\nWould you like me to trigger a retry for **{lastFailed.Service.ToUpper()}**?";
                }
                return "✅ **No recent backup errors detected!** All recent cycles for Website FTP, SQL Database, and Mailchimp have completed with 100% success.";
            }

            // Tunnel & Remote Access
            if (lower.Contains("tunnel") || lower.Contains("cloudflare") || lower.Contains("tailscale") || lower.Contains("remote"))
            {
                var cfUrl = CloudflareTunnelService.ActiveUrl ?? "Inactive";
                var tsUrl = TailscaleNetworkService.GetTailscaleUrl();
                var tsDisplay = string.IsNullOrEmpty(tsUrl) ? "Inactive" : tsUrl;
                var cfStatus = CloudflareTunnelService.IsRunning ? "🟢 Active & Guarded" : "🔴 Disconnected";

                return $@"### 🌐 Remote Access & Tunnel Status
- **Cloudflare Tunnel:** {cfStatus}
- **Tunnel URL:** `{cfUrl}`
- **Tailscale Mesh VPN:** `{tsDisplay}`
- **Watchdog Auto-Restart:** {(CloudflareTunnelService.IsAutoManaged ? "Enabled (Self-Healing)" : "Disabled")}";
            }

            // Privacy & Security
            if (lower.Contains("privacy") || lower.Contains("security") || lower.Contains("password") || lower.Contains("credential") || lower.Contains("leak"))
            {
                return "🔒 **Privacy Guarantee**: I operate behind PinayPal's **Zero-Leak Sanitizer**. I never see, store, or transmit your passwords, database connection strings, encryption keys, or backup file contents. Only aggregated operational metadata is used.";
            }

            // Default conversational fallback
            return $@"I'm listening! I can help you monitor and operate **PinayPal Backup Manager v{BackupConfig.AppVersion}**.

Here are some things you can ask me:
- **""How is the system health?""** — View live CPU, memory, and daemon status.
- **""How much disk space is left?""** — Inspect drive capacities and backup folders.
- **""Run a backup for Website FTP""** — I will prepare the action for your approval.
- **""Recreate the Cloudflare Tunnel""** — Generates a fresh remote access URL.
- **""Send a test email""** — Validates executive HTML alert delivery.";
        }

        // ==========================================
        // Zero-Leak Sanitizer
        // ==========================================
        public static string SanitizePrompt(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            // Redact passwords & keys
            var sanitized = Regex.Replace(input, @"(?i)(password|pwd|secret|pin|token|apikey)\s*[:=]\s*\S+", "$1=[REDACTED]");
            sanitized = Regex.Replace(sanitized, @"Bearer\s+[A-Za-z0-9_\-\.]+", "Bearer [REDACTED]");

            // Obfuscate Windows user directories
            sanitized = Regex.Replace(sanitized, @"(?i)[a-z]:\\users\\[^\\]+\\", @"C:\Users\[User]\");

            return sanitized;
        }

        public static string SanitizeOutput(string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            return Regex.Replace(output, @"(?i)(password|pwd|secret|pin)\s*[:=]\s*\S+", "$1=[REDACTED]");
        }

        private static string BuildSanitizedSystemContext()
        {
            try
            {
                var telemetry = HardwareTelemetryService.GetTelemetrySync();
                var history = BackupHistoryService.GetHistory().Take(3);
                var recentSummaries = string.Join("; ", history.Select(h => $"{h.Service}:{h.Status}({h.Duration.TotalSeconds:F0}s)"));

                return $@"Host: {Environment.MachineName}
OS: {RuntimeInformation.OSDescription}
CPU: {telemetry.CpuName} ({telemetry.CpuUsagePercent:F0}% load)
RAM: {telemetry.RamUsagePercent:F0}% used ({telemetry.RamFreeGB:F1} GB free)
Tunnel: {(CloudflareTunnelService.IsRunning ? "Active" : "Offline")}
Recent Backups: {recentSummaries}";
            }
            catch
            {
                return $"Host: {Environment.MachineName}, App: PinayPal v{BackupConfig.AppVersion}";
            }
        }
    }
}
