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

        /// <summary>Tappable next-step prompts shown as chips under the reply.</summary>
        public List<string> FollowUpSuggestions { get; set; } = new();

        /// <summary>Which engine actually produced this reply (ollama / cloud / heuristics).</summary>
        public string? Engine { get; set; }
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
        // ---- Core engine ----
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// hybrid | ollama | cloud | heuristics. "hybrid" is the recommended default:
        /// it prefers the local Ollama model and only escalates to the cloud when the
        /// local engine is unreachable or the question needs stronger reasoning.
        /// </summary>
        public string Provider { get; set; } = "hybrid";

        /// <summary>
        /// When true, only aggregated operational metadata (telemetry rollups, backup
        /// status, disk figures) is ever attached to a cloud request. Identifiers and
        /// error strings are stripped. This is the "online but still secure" mode.
        /// </summary>
        public bool CloudRedactsContext { get; set; } = true;

        /// <summary>Hard ceiling on how many past turns are sent to a cloud provider.</summary>
        public int CloudMaxHistoryTurns { get; set; } = 6;

        public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";

        /// <summary>
        /// Defaults to a small, fast model. On a 6-core Ryzen 5 5600 with 16 GB of RAM a
        /// 3B-class model keeps replies snappy without starving the backup engine.
        /// </summary>
        public string OllamaModel { get; set; } = "qwen2.5:3b-instruct-q4_K_M";

        /// <summary>
        /// Number of CPU threads Ollama may use. Defaults to 0 (auto). Set it to 4 on a
        /// 6-core/12-thread part so backup transfers and telemetry keep a headroom.
        /// </summary>
        public int OllamaThreads { get; set; } = 0;

        /// <summary>
        /// Keep the local Ollama model resident during backups. Leave this off on a
        /// 16 GB host so the model is evicted automatically before each backup runs.
        /// </summary>
        public bool EnableLocalModelDuringBackups { get; set; } = false;

        public string CloudApiKey { get; set; } = "";
        public string CloudEndpoint { get; set; } = "https://api.openai.com/v1";
        public string CloudModel { get; set; } = "gpt-4o-mini";

        // ---- Experience / personality ----
        /// <summary>How conversational and warm the assistant sounds. 0 = terse, 100 = chatty.</summary>
        public int Talkativeness { get; set; } = 60;
        /// <summary>0.0 (deterministic) to 1.5 (creative). Higher means more personality in phrasing.</summary>
        public double Creativity { get; set; } = 0.6;
        /// <summary>Name the assistant replies to. "Antigravity" is the default persona name.</summary>
        public string AssistantName { get; set; } = "Antigravity";
        /// <summary>How many prior turns are replayed to the LLM so it can hold a real conversation.</summary>
        public int ConversationMemoryDepth { get; set; } = 12;

        // ---- Conversation behaviour ----
        /// <summary>Show tappable follow-up suggestion chips after each reply.</summary>
        public bool EnableFollowUpSuggestions { get; set; } = true;
        /// <summary>Allow the assistant to proactively post status bubbles and scheduled briefings.</summary>
        public bool EnableProactiveUpdates { get; set; } = true;
        /// <summary>Minutes between proactive health briefings. 0 disables the scheduler.</summary>
        public int ProactiveIntervalMinutes { get; set; } = 60;

        // ---- Agent Profiles & Learning Memory ----
        /// <summary>
        /// guardian (SRE reliability & recovery) | specialist (backup integrity & data) | speedy (crisp & immediate)
        /// </summary>
        public string AgentProfile { get; set; } = "guardian";

        /// <summary>Enables persistent memory store for learned facts and user preferences.</summary>
        public bool EnableLearningMemory { get; set; } = true;

        public string GetProfileTitle() => (AgentProfile ?? "guardian").ToLowerInvariant() switch
        {
            "specialist" => "💾 Backup & Data Specialist",
            "speedy" => "⚡ Speedy Minimalist Assistant",
            _ => "🛡️ SRE Guardian & Auto-Healer"
        };

        public string GetProfileInstructions() => (AgentProfile ?? "guardian").ToLowerInvariant() switch
        {
            "specialist" => "You are the Backup & Data Integrity Specialist. Focus on database schemas, incremental file synchronization, snapshot verification, storage runway forecasting, and Mailchimp audience exports. Prioritize data consistency above all.",
            "speedy" => "You are the Speedy Minimalist Assistant. Keep responses ultra-crisp (1-2 sentences), directly answer the prompt, and prepare requested actions without unnecessary explanation.",
            _ => "You are the SRE Guardian & System Auto-Healer. Focus on system stability, CPU/RAM/Disk health, Cloudflare tunnel resilience, proactive disaster prevention, and prompt recovery recommendations."
        };

        // ---- Safety ----
        /// <summary>Require an explicit tap on the action card before any state-changing command runs.</summary>
        public bool RequireActionApproval { get; set; } = true;
        /// <summary>Master kill switch for the Zero-Leak sanitizer. Should stay on.</summary>
        public bool EnableZeroLeakSanitizer { get; set; } = true;

        // ---- UI ----
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
        public static event Action<AIAssistantConfig>? OnConfigChanged;
        public static Func<bool>? IsAnyBackupRunning { get; set; }
        public static Func<string?>? GetActiveBackupDetails { get; set; }

        /// <summary>Last backup service the user discussed, so "run that one" resolves correctly.</summary>
        private static string? _lastDiscussedService;

        private static System.Threading.Timer? _proactiveTimer;

        static AIAssistantService()
        {
            LoadConfig();
            StartProactiveScheduler();
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
                // Apply the new proactive cadence immediately without a restart.
                StartProactiveScheduler();
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
                OnConfigChanged?.Invoke(_config);
                LogService.WriteSystemLog("[AIAssistant] Configuration updated successfully", "Information", "AI");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Error saving config: {ex.Message}", "Error", "AI");
            }
        }

        public static async Task<(bool ok, string message)> TestOllamaConnectionAsync(string? endpoint = null, string? model = null)
        {
            var ep = string.IsNullOrWhiteSpace(endpoint) ? _config.OllamaEndpoint : endpoint.Trim().TrimEnd('/');
            var targetModel = string.IsNullOrWhiteSpace(model) ? _config.OllamaModel : model.Trim();

            try
            {
                using var cts = new System.Threading.CancellationTokenSource(3000);
                var resp = await _httpClient.GetAsync($"{ep}/api/tags", cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    return (false, $"Ollama HTTP Error {(int)resp.StatusCode}: {resp.ReasonPhrase}");
                }

                var jsonStr = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("models", out var modelsArr))
                {
                    var modelNames = new List<string>();
                    foreach (var m in modelsArr.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var n)) modelNames.Add(n.GetString() ?? "");
                    }

                    if (string.IsNullOrEmpty(targetModel) || modelNames.Any(m => m.Equals(targetModel, StringComparison.OrdinalIgnoreCase) || m.StartsWith(targetModel, StringComparison.OrdinalIgnoreCase)))
                    {
                        return (true, $"🟢 Online! Model '{targetModel}' is ready ({modelNames.Count} models available).");
                    }
                    return (true, $"🟡 Reachable, but model '{targetModel}' is not yet pulled. Found: {string.Join(", ", modelNames.Take(2))}");
                }

                return (true, "🟢 Ollama server is online and operational!");
            }
            catch (Exception ex)
            {
                return (false, $"🔴 Could not connect to {ep}: {ex.Message}");
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
        /// Automatically inspects host CPU and RAM to set optimal thread count, memory retention, and provider mode.
        /// </summary>
        public static async Task<(bool ok, string message)> AutoOptimizeForHardwareAsync()
        {
            await Task.Yield();
            try
            {
                var telemetry = HardwareTelemetryService.GetTelemetrySync();
                var logicalCores = Environment.ProcessorCount;
                var ramGB = telemetry.RamFreeGB + (telemetry.RamUsagePercent > 0 ? (telemetry.RamFreeGB / (1.0 - (telemetry.RamUsagePercent / 100.0))) * (telemetry.RamUsagePercent / 100.0) : 16);

                // Optimal threads: cores / 2 capped between 2 and 8
                var optimalThreads = Math.Clamp(logicalCores / 2, 2, 8);
                _config.OllamaThreads = optimalThreads;

                // Memory policy: if host has <= 24 GB, evict local model during backups to prevent starvation
                _config.EnableLocalModelDuringBackups = (ramGB > 24);

                // Test if local Ollama is active
                var (ollamaOk, _) = await TestOllamaConnectionAsync();
                if (ollamaOk)
                {
                    _config.Provider = "hybrid";
                }
                else if (!string.IsNullOrWhiteSpace(_config.CloudApiKey))
                {
                    _config.Provider = "cloud";
                }
                else
                {
                    _config.Provider = "heuristics";
                }

                SaveConfig(_config);
                return (true, $"Optimized for {logicalCores} CPU threads ({optimalThreads} AI threads) & ~{ramGB:F0}GB RAM. Engine: {_config.Provider.ToUpper()}. Model retention during backup: {(_config.EnableLocalModelDuringBackups ? "ON" : "OFF")}.");
            }
            catch (Exception ex)
            {
                return (false, $"Auto-tune error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles explicit memory learning, retrieval, or deletion commands.
        /// </summary>
        private static string? TryHandleMemoryCommand(string prompt)
        {
            var lower = prompt.Trim().ToLowerInvariant();

            // 1. Inquire about memory
            if (lower == "what do you remember?" || lower == "what do you remember" ||
                lower == "show memories" || lower == "show memory" ||
                lower == "list memories" || lower == "what have you learned?")
            {
                var all = AIMemoryStore.GetAll();
                if (all.Count == 0)
                {
                    return "🧠 **My memory is currently clear.**\n\nI haven't recorded any custom preferences or facts yet. You can teach me things by saying:\n- *\"Remember that my preferred backup time is 11 PM\"*\n- *\"Remember that I prefer alerts via Telegram\"*\n- *\"Note: Server FTP has a 10s connection timeout\"*";
                }

                var sb = new StringBuilder();
                sb.AppendLine("### 🧠 Learned Memories & User Preferences");
                foreach (var m in all)
                {
                    var icon = m.Category == "preference" ? "⭐" : m.Category == "instruction" ? "📌" : "💡";
                    sb.AppendLine($"- {icon} **{char.ToUpper(m.Category[0]) + m.Category.Substring(1)}** (`{m.Key}`): {m.Content} *(updated {m.UpdatedAt:MMM dd})*");
                }
                sb.AppendLine("\n*Tip: Say \"forget [topic]\" to remove an entry, or \"clear memories\" to reset all.*");
                return sb.ToString();
            }

            // 2. Clear all memories
            if (lower == "clear memories" || lower == "clear memory" || lower == "forget everything" || lower == "reset memories")
            {
                AIMemoryStore.ClearAll();
                return "🧹 **Memory Cleared**: I have reset all learned user preferences and notes.";
            }

            // 3. Forget specific memory
            if (lower.StartsWith("forget memory") || lower.StartsWith("forget that") || lower.StartsWith("forget "))
            {
                var target = prompt.Substring(prompt.IndexOf(' ') + 1).Trim();
                if (target.StartsWith("that ", StringComparison.OrdinalIgnoreCase)) target = target.Substring(5).Trim();
                if (target.StartsWith("memory ", StringComparison.OrdinalIgnoreCase)) target = target.Substring(7).Trim();

                var forgot = AIMemoryStore.Forget(target);
                return forgot
                    ? $"🗑️ **Forgot Memory**: I've removed knowledge related to \"{target}\"."
                    : $"❓ I couldn't find any memory matching \"{target}\". Say *\"what do you remember\"* to view active memories.";
            }

            // 4. Remember / Learn statements
            string? factToRemember = null;
            string category = "preference";
            string key = "general";

            if (Regex.IsMatch(prompt, @"^(?i)(?:please\s+)?remember\s+(?:that\s+)?(.+)"))
            {
                var m = Regex.Match(prompt, @"^(?i)(?:please\s+)?remember\s+(?:that\s+)?(.+)");
                factToRemember = m.Groups[1].Value.Trim();
            }
            else if (Regex.IsMatch(prompt, @"^(?i)(?:please\s+)?(?:note\s+down|note|keep\s+in\s+mind)\s+(?:that\s+)?(.+)"))
            {
                var m = Regex.Match(prompt, @"^(?i)(?:please\s+)?(?:note\s+down|note|keep\s+in\s+mind)\s+(?:that\s+)?(.+)");
                factToRemember = m.Groups[1].Value.Trim();
                category = "instruction";
            }
            else if (Regex.IsMatch(prompt, @"^(?i)my\s+preferred\s+([a-zA-Z0-9_\s]+)\s+is\s+(.+)"))
            {
                var m = Regex.Match(prompt, @"^(?i)my\s+preferred\s+([a-zA-Z0-9_\s]+)\s+is\s+(.+)");
                key = m.Groups[1].Value.Trim().Replace(" ", "_");
                factToRemember = m.Groups[2].Value.Trim();
                category = "preference";
            }

            if (!string.IsNullOrWhiteSpace(factToRemember))
            {
                if (key == "general")
                {
                    var fl = factToRemember.ToLowerInvariant();
                    if (fl.Contains("telegram")) key = "telegram_alert";
                    else if (fl.Contains("time") || fl.Contains("pm") || fl.Contains("am") || fl.Contains("schedule")) key = "schedule_time";
                    else if (fl.Contains("sql") || fl.Contains("database")) key = "sql_preference";
                    else if (fl.Contains("ftp") || fl.Contains("website")) key = "ftp_preference";
                    else if (fl.Contains("mailchimp")) key = "mailchimp_preference";
                    else if (fl.Contains("name is") || fl.Contains("call me")) key = "user_name";
                    else key = "note_" + DateTime.UtcNow.ToString("MMdd");
                }

                var (ok, msg) = AIMemoryStore.Remember(category, key, factToRemember);
                if (ok)
                {
                    return $"🧠 **Learned & Remembered!**\n\nI have saved this to my persistent memory:\n- **Category:** {char.ToUpper(category[0]) + category.Substring(1)}\n- **Topic:** `{key}`\n- **Fact:** {factToRemember}\n\nI will remember this across sessions and keep it in mind during our operations.";
                }
                else
                {
                    return $"⚠️ **Could Not Store Memory**: {msg}";
                }
            }

            return null;
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
                if (_sessionHistory.Count > 60) _sessionHistory.RemoveAt(0);
            }

            ChatMessage assistantMsg;

            try
            {
                // Check for explicit memory store commands first if learning memory is enabled
                if (_config.EnableLearningMemory)
                {
                    var memoryResponse = TryHandleMemoryCommand(sanitizedUserMessage);
                    if (!string.IsNullOrWhiteSpace(memoryResponse))
                    {
                        assistantMsg = new ChatMessage
                        {
                            Role = "assistant",
                            Content = memoryResponse,
                            Engine = "memory_store",
                            FollowUpSuggestions = new List<string> { "What do you remember?", "How is the system health?", "Run all backups" }
                        };

                        lock (_historyLock)
                        {
                            _sessionHistory.Add(assistantMsg);
                            if (_sessionHistory.Count > 60) _sessionHistory.RemoveAt(0);
                        }

                        OnMessageReceived?.Invoke(assistantMsg);
                        return assistantMsg;
                    }
                }

                // 1. Detect a guarded action intent.
                var proposedAction = DetectActionIntent(sanitizedUserMessage);

                // Remember which service we last discussed so follow-ups like "run that one" work.
                TrackDiscussedService(sanitizedUserMessage);

                // 2. Dispatch to the chosen or hybrid provider.
                string replyText = "";
                var engineUsed = "heuristics";

                if (_config.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase) ||
                    (_config.Provider.Equals("hybrid", StringComparison.OrdinalIgnoreCase) && await IsOllamaReachableAsync()))
                {
                    replyText = await QueryOllamaAsync(sanitizedUserMessage, proposedAction);
                    if (!string.IsNullOrWhiteSpace(replyText)) engineUsed = "ollama";
                }

                if (string.IsNullOrWhiteSpace(replyText) &&
                    (_config.Provider.Equals("cloud", StringComparison.OrdinalIgnoreCase) ||
                     _config.Provider.Equals("hybrid", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(_config.CloudApiKey))
                {
                    replyText = await QueryCloudLlmAsync(sanitizedUserMessage, proposedAction);
                    if (!string.IsNullOrWhiteSpace(replyText)) engineUsed = "cloud";
                }

                // If LLM returned action tags [ACTION: ...], extract it
                if (!string.IsNullOrWhiteSpace(replyText))
                {
                    var (cleanReply, extractedAction) = ExtractActionFromReply(replyText);
                    if (proposedAction == null && extractedAction != null)
                    {
                        proposedAction = extractedAction;
                        replyText = cleanReply;
                    }
                }

                // 3. Fall back to the offline diagnostic engine when no LLM could answer.
                if (string.IsNullOrWhiteSpace(replyText))
                {
                    replyText = await QueryHeuristicEngineAsync(sanitizedUserMessage, proposedAction);
                }

                assistantMsg = new ChatMessage
                {
                    Role = "assistant",
                    Content = ApplyVerbosity(SanitizeOutput(replyText)),
                    ProposedAction = proposedAction,
                    Engine = engineUsed,
                    FollowUpSuggestions = proposedAction != null
                        ? new List<string>()
                        : BuildFollowUpSuggestions(sanitizedUserMessage)
                };

                if (proposedAction != null)
                {
                    // Guard against unbounded growth: proposals the user never approves would
                    // otherwise sit here for the whole session. Clearing is safe because an
                    // expired proposal can no longer be executed anyway.
                    if (_pendingActions.Count > 64)
                    {
                        _pendingActions.Clear();
                    }

                    _pendingActions[proposedAction.ActionId] = proposedAction;
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Message processing error: {ex.Message}", "Warning", "AI");
                assistantMsg = new ChatMessage
                {
                    Role = "assistant",
                    Content = $"I hit a snag processing that one: {ex.Message}. I have switched to local diagnostic mode, so I can still help.",
                    Engine = "heuristics"
                };
            }

            lock (_historyLock)
            {
                _sessionHistory.Add(assistantMsg);
                if (_sessionHistory.Count > 60) _sessionHistory.RemoveAt(0);
            }

            OnMessageReceived?.Invoke(assistantMsg);
            return assistantMsg;
        }

        /// <summary>Remembers the most recently discussed service so pronoun follow-ups resolve.</summary>
        private static void TrackDiscussedService(string prompt)
        {
            var lower = prompt.ToLowerInvariant();
            if (lower.Contains("sql") || lower.Contains("database") || lower.Contains("mysql"))
                _lastDiscussedService = "sql";
            else if (lower.Contains("ftp") || lower.Contains("website") || lower.Contains("files"))
                _lastDiscussedService = "ftp";
            else if (lower.Contains("mailchimp") || lower.Contains("audience") || lower.Contains("campaign"))
                _lastDiscussedService = "mailchimp";
        }

        /// <summary>Resolves "that one" / "it again" against the last discussed service.</summary>
        private static string ResolveServiceFromPronoun(string prompt)
        {
            var lower = prompt.ToLowerInvariant();
            if (lower.Contains("that one") || lower.Contains("that service")
                || lower.Contains("it again") || lower.Contains("same one") || lower.Contains("again"))
            {
                return _lastDiscussedService ?? "all";
            }
            return "all";
        }

        /// <summary>
        /// Warms up terse prose answers according to the configured Talkativeness.
        /// Structured markdown reports and action prompts are left untouched.
        /// </summary>
        private static string ApplyVerbosity(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return reply;
            var talk = Math.Clamp(_config.Talkativeness, 0, 100);
            if (talk < 30) return reply;

            // Never decorate bullet/markdown reports or approval prompts.
            if (reply.Contains("###") || reply.Contains("- **") || reply.Contains("Approve"))
                return reply;

            var opener = talk > 70
                ? "Good question — here's the full picture. "
                : "Here's what I found. ";

            return opener + reply;
        }

        /// <summary>Produces contextual next-step chips based on what was just asked.</summary>
        private static List<string> BuildFollowUpSuggestions(string prompt)
        {
            if (!_config.EnableFollowUpSuggestions) return new List<string>();

            var lower = prompt.ToLowerInvariant();
            var chips = new List<string>();

            if (lower.Contains("health") || lower.Contains("cpu") || lower.Contains("ram") || lower.Contains("temp"))
            {
                chips.Add("Check disk space");
                chips.Add("Show my computers");
                chips.Add("Run a health check");
            }
            else if (lower.Contains("disk") || lower.Contains("storage") || lower.Contains("space"))
            {
                chips.Add("Show recent backups");
                chips.Add("Check system health");
            }
            else if (lower.Contains("computer") || lower.Contains("devpc") || lower.Contains("dev pc")
                     || lower.Contains("mainpc") || lower.Contains("main pc") || lower.Contains("pc "))
            {
                chips.Add("How is the system health?");
                chips.Add("Run all backups");
            }
            else if (lower.Contains("backup") || lower.Contains("history") || lower.Contains("run"))
            {
                chips.Add("How is the system health?");
                chips.Add("Check disk space");
                chips.Add("Inspect recent errors");
            }
            else if (lower.Contains("tunnel") || lower.Contains("remote") || lower.Contains("network"))
            {
                chips.Add("Recreate the Cloudflare tunnel");
                chips.Add("Test email alert");
            }
            else if (lower.Contains("error") || lower.Contains("fail") || lower.Contains("why"))
            {
                chips.Add("Retry the failed backup");
                chips.Add("Show recent backups");
            }
            else
            {
                chips.Add("How is the system health?");
                chips.Add("Show my computers");
            }

            return chips.Distinct().Take(3).ToList();
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

            // Safety rail: even an "approved" call is refused when the guard is switched on
            // and the action still demands explicit confirmation.
            if (_config.RequireActionApproval && action.RequiresConfirmation && !action.IsApproved)
            {
                action.ExecutionResult = "Blocked: this action requires explicit confirmation.";
                return (false, action.ExecutionResult);
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

                    case "computer_power":
                        var computerId = action.Parameters.TryGetValue("computerId", out var cid) ? cid : "";
                        var pcAction = action.Parameters.TryGetValue("action", out var pa) ? pa : "wake";
                        var powerResult = await ComputerManagementService.ExecutePowerAsync(computerId, pcAction);
                        action.ExecutionResult = powerResult.Message;
                        return (powerResult.Success, powerResult.Message);

                    case "sync_preview":
                        var previewService = action.Parameters.TryGetValue("service", out var ps) ? ps : "ftp";
                        var plan = await SyncPreviewService.BuildPlanAsync(previewService);
                        action.ExecutionResult = SyncPreviewService.DescribePlan(plan);
                        return (true, action.ExecutionResult);

                    case "sync_rollback":
                        var rollbackService = action.Parameters.TryGetValue("service", out var rs) ? rs : "ftp";
                        var snapshots = SyncPreviewService.ListSnapshots(rollbackService);
                        if (snapshots.Count == 0)
                        {
                            action.ExecutionResult = $"There is no saved snapshot for {rollbackService.ToUpperInvariant()} yet. A snapshot is taken automatically before each sync that overwrites files.";
                            return (false, action.ExecutionResult);
                        }

                        var restored = await SyncPreviewService.RestoreSnapshotAsync(rollbackService, snapshots[0].Id);
                        action.ExecutionResult = restored.message;
                        return (restored.ok, restored.message);

                    case "unload_model":
                        var (unloadOk, unloadMsg) = await UnloadOllamaModelAsync();
                        action.ExecutionResult = unloadMsg;
                        return (unloadOk, unloadMsg);

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

                    case "test_telegram":
                        var (tgOk, tgMsg) = await TelegramService.SendTestMessageAsync();
                        action.ExecutionResult = tgOk ? "Test alert successfully delivered to Telegram!" : $"Telegram delivery failed: {tgMsg}";
                        return (tgOk, action.ExecutionResult);

                    case "send_telegram_qr":
                        var (qrOk, qrMsg) = await TelegramService.SendConnectionQrAsync();
                        action.ExecutionResult = qrOk ? "iOS pairing QR code uploaded to Telegram!" : $"QR upload failed: {qrMsg}";
                        return (qrOk, action.ExecutionResult);

                    case "switch_agent_profile":
                        var targetProfile = action.Parameters.TryGetValue("profile", out var prof) ? prof : "guardian";
                        _config.AgentProfile = targetProfile;
                        SaveConfig(_config);
                        action.ExecutionResult = $"Agent profile switched to: {_config.GetProfileTitle()}";
                        return (true, action.ExecutionResult);

                    case "forget_memory":
                        var targetKey = action.Parameters.TryGetValue("key", out var tk) ? tk : "";
                        var forgot = AIMemoryStore.Forget(targetKey);
                        action.ExecutionResult = forgot ? $"Successfully removed memory for '{targetKey}'." : $"Could not find memory '{targetKey}'.";
                        return (forgot, action.ExecutionResult);

                    case "clear_memories":
                        AIMemoryStore.ClearAll();
                        action.ExecutionResult = "All learned AI memories have been cleared.";
                        return (true, action.ExecutionResult);

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

        private static string? FirstMatch(string haystack, params string[] needles)
        {
            foreach (var n in needles)
            {
                if (haystack.Contains(n)) return n;
            }
            return null;
        }

        /// <summary>True when the prompt clearly refers to a machine rather than a backup.</summary>
        private static bool MentionsComputer(string lower)
        {
            if (lower.Contains("computer") || lower.Contains("pc") || lower.Contains("desktop")
                || lower.Contains("laptop") || lower.Contains("machine") || lower.Contains("devpc")
                || lower.Contains("mainpc") || lower.Contains("dev pc") || lower.Contains("main pc"))
                return true;

            // A bare registered computer name (e.g. "restart devpc") also counts.
            return ComputerManagementService.GetNodes()
                .Any(n => lower.Contains(n.DisplayName.ToLowerInvariant()));
        }

        /// <summary>Pulls a quoted computer name out of the prompt, if present.</summary>
        private static string? ExtractComputerName(string lower)
        {
            var quoted = System.Text.RegularExpressions.Regex.Match(lower, @"[""']([^""']+)[""']");
            if (quoted.Success) return quoted.Groups[1].Value;

            // Otherwise, look for a registered name mentioned anywhere in the sentence.
            var node = ComputerManagementService.GetNodes()
                .FirstOrDefault(n => lower.Contains(n.DisplayName.ToLowerInvariant()));
            return node?.DisplayName;
        }

        private static string HumanizeAction(string action) => action switch
        {
            "wake" => "Wake",
            "restart" => "Restart",
            "shutdown" => "Shut down",
            "lock" => "Lock",
            "sleep" => "Sleep",
            "signout" => "Sign out of",
            _ => action
        };

        // ==========================================
        // Action Intent Recognition
        // ==========================================
        private static AIProposedAction? DetectActionIntent(string prompt)
        {
            var lower = prompt.ToLowerInvariant();

            // ---- Computer fleet power intents (wake / restart / shutdown / lock / sleep) ----
            var powerVerb = FirstMatch(lower, "wake", "boot", "power on")
                          ?? FirstMatch(lower, "restart", "reboot")
                          ?? FirstMatch(lower, "shut down", "shutdown", "power off", "turn off")
                          ?? FirstMatch(lower, "lock", "lock screen")
                          ?? FirstMatch(lower, "sleep", "standby", "hibernate");

            // "wake" must not be confused with "wake up the backup".
            if (powerVerb != null && MentionsComputer(lower))
            {
                var verb = powerVerb;
                var target = ExtractComputerName(lower);
                var node = ComputerManagementService.FindNodeByName(target);

                if (node != null)
                {
                    var actionName = verb switch
                    {
                        "wake" or "boot" or "power on" => "wake",
                        "restart" or "reboot" => "restart",
                        "shut down" or "shutdown" or "power off" or "turn off" => "shutdown",
                        "lock" or "lock screen" => "lock",
                        _ => "sleep"
                    };

                    var description = actionName switch
                    {
                        "wake" => $"Sends a Wake-on-LAN magic packet to {node.DisplayName} using MAC {ComputerManagementService.FormatMac(node.MacAddress)}.",
                        "restart" => $"{node.DisplayName} will restart after a 10 second grace delay.",
                        "shutdown" => $"{node.DisplayName} will shut down after a 10 second grace delay.",
                        "lock" => $"Locks the {node.DisplayName} workstation.",
                        _ => $"Puts {node.DisplayName} to sleep."
                    };

                    return new AIProposedAction
                    {
                        ActionType = "computer_power",
                        Title = $"{HumanizeAction(actionName)} {node.DisplayName}",
                        Description = description,
                        Parameters = new Dictionary<string, string>
                        {
                            { "computerId", node.Id },
                            { "computerName", node.DisplayName },
                            { "action", actionName }
                        }
                    };
                }

                // A computer was named but isn't registered yet.
                return new AIProposedAction
                {
                    ActionType = "none",
                    Title = $"I don't know a computer called \"{target ?? "that"}\"",
                    Description = $"Add it under **Settings → My Computers** with its name, MAC address and dashboard URL, then ask me again.",
                    RequiresConfirmation = false
                };
            }

            // Show what a sync would change before running it (read-only, no transfer)
            if (lower.Contains("preview") || lower.Contains("what would change") || lower.Contains("dry run")
                || lower.Contains("diff") || lower.Contains("pending changes") || lower.Contains("what will sync"))
            {
                var target = lower.Contains("sql") || lower.Contains("database") ? "sql"
                    : lower.Contains("mailchimp") ? "mailchimp"
                    : "ftp";

                return new AIProposedAction
                {
                    ActionType = "sync_preview",
                    Title = $"Preview {target.ToUpperInvariant()} Sync Changes",
                    Description = "Lists the files a sync would upload, without transferring anything.",
                    RequiresConfirmation = false,
                    Parameters = new Dictionary<string, string> { { "service", target } }
                };
            }

            // Restore the server to a previous state
            if (lower.Contains("rollback") || lower.Contains("undo the sync") || lower.Contains("revert the sync")
                || lower.Contains("restore previous") || lower.Contains("undo last sync"))
            {
                var target = lower.Contains("sql") || lower.Contains("database") ? "sql"
                    : lower.Contains("mailchimp") ? "mailchimp"
                    : "ftp";

                return new AIProposedAction
                {
                    ActionType = "sync_rollback",
                    Title = $"Roll Back {target.ToUpperInvariant()} to Previous Snapshot",
                    Description = "Restores the most recent saved copy of every file the last sync overwrote.",
                    Parameters = new Dictionary<string, string> { { "service", target } }
                };
            }

            // Free up RAM by evicting the local LLM (useful before a big backup on a 16 GB box)
            if (lower.Contains("free memory") || lower.Contains("free up ram") || lower.Contains("unload model")
                || lower.Contains("free up memory") || lower.Contains("release memory")
                || lower.Contains("stop using ram") || lower.Contains("ollama memory"))
            {
                return new AIProposedAction
                {
                    ActionType = "unload_model",
                    Title = "Free Memory (Unload Local AI Model)",
                    Description = "Asks Ollama to evict the loaded model from RAM so backups get the full 16 GB."
                };
            }

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

                // Resolve "run that one" against the last service we discussed.
                var resolved = ResolveServiceFromPronoun(lower);

                return new AIProposedAction
                {
                    ActionType = "run_backup",
                    Title = resolved == "all"
                        ? "Run All Backups (Parallel)"
                        : $"Run {resolved.ToUpperInvariant()} Backup",
                    Description = resolved == "all"
                        ? "Executes FTP Website, SQL Database, and Mailchimp backups concurrently."
                        : $"Executes the {resolved} backup on its own.",
                    Parameters = new Dictionary<string, string> { { "service", resolved } }
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

            // Test Telegram Alert
            if (lower.Contains("test telegram") || lower.Contains("send telegram test") || lower.Contains("check telegram alert") || lower.Contains("telegram test"))
            {
                return new AIProposedAction
                {
                    ActionType = "test_telegram",
                    Title = "Dispatch Telegram Test Alert",
                    Description = "Sends an alert verification message to your configured Telegram Chat ID."
                };
            }

            // Send iOS QR Code to Telegram
            if (lower.Contains("send qr") || lower.Contains("qr to telegram") || lower.Contains("telegram qr") || lower.Contains("send ios qr"))
            {
                return new AIProposedAction
                {
                    ActionType = "send_telegram_qr",
                    Title = "Send iOS QR Code to Telegram",
                    Description = "Generates and sends the iOS pairing QR code photo to your Telegram chat."
                };
            }

            // Switch Agent Profile
            if (lower.Contains("switch to guardian") || lower.Contains("set guardian agent"))
            {
                return new AIProposedAction
                {
                    ActionType = "switch_agent_profile",
                    Title = "Switch to SRE Guardian Agent",
                    Description = "Switches AI persona to system health, reliability, and proactive watchdogs.",
                    Parameters = new Dictionary<string, string> { { "profile", "guardian" } }
                };
            }
            if (lower.Contains("switch to specialist") || lower.Contains("switch to backup specialist") || lower.Contains("set specialist agent"))
            {
                return new AIProposedAction
                {
                    ActionType = "switch_agent_profile",
                    Title = "Switch to Backup Specialist Agent",
                    Description = "Switches AI persona to database integrity, backup snapshots, and storage runway.",
                    Parameters = new Dictionary<string, string> { { "profile", "specialist" } }
                };
            }
            if (lower.Contains("switch to speedy") || lower.Contains("set speedy agent") || lower.Contains("fast mode"))
            {
                return new AIProposedAction
                {
                    ActionType = "switch_agent_profile",
                    Title = "Switch to Speedy Minimalist Agent",
                    Description = "Switches AI persona to ultra-crisp, immediate answers with minimal token overhead.",
                    Parameters = new Dictionary<string, string> { { "profile", "speedy" } }
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

            // Clear History
            if (lower.Contains("clear history") || lower.Contains("delete history") || lower.Contains("reset history") || lower.Contains("wipe history"))
            {
                return new AIProposedAction
                {
                    ActionType = "clear_history",
                    Title = "Clear Local Backup History",
                    Description = "Purges all recorded historical log entries from the database while preserving raw backup files."
                };
            }

            // Retry failed backups
            if (lower.Contains("retry") || lower.Contains("try again") || lower.Contains("retry failed"))
            {
                var recentHistory = BackupHistoryService.GetHistory().Take(5).ToList();
                var lastFailed = recentHistory.FirstOrDefault(h => h.Status != "Success");
                var serviceToRetry = lastFailed?.Service.ToLowerInvariant() ?? "all";
                var serviceName = serviceToRetry == "all" ? "All Services" : serviceToRetry.ToUpper();

                return new AIProposedAction
                {
                    ActionType = "run_backup",
                    Title = $"Retry {serviceName} Backup",
                    Description = $"Dispatches a retry cycle for {serviceName} to recover from the previous disruption.",
                    Parameters = new Dictionary<string, string> { { "service", serviceToRetry } }
                };
            }

            return null;
        }

        /// <summary>
        /// Creates an AIProposedAction from a structured action type and parameter dictionary.
        /// </summary>
        public static AIProposedAction? CreateActionFromType(string actionType, Dictionary<string, string> parameters)
        {
            var cleanType = actionType.Trim().ToLowerInvariant();
            switch (cleanType)
            {
                case "run_backup":
                    var service = parameters.TryGetValue("service", out var s) ? s.ToLowerInvariant() : "all";
                    return new AIProposedAction
                    {
                        ActionType = "run_backup",
                        Title = service == "all" ? "Run All Backups (Parallel)" : $"Run {service.ToUpperInvariant()} Backup",
                        Description = service == "all" ? "Executes FTP Website, SQL Database, and Mailchimp backups concurrently." : $"Executes {service.ToUpperInvariant()} backup task.",
                        Parameters = new Dictionary<string, string> { { "service", service } }
                    };

                case "test_telegram":
                    return new AIProposedAction
                    {
                        ActionType = "test_telegram",
                        Title = "Send Telegram Test Alert",
                        Description = "Sends an alert verification message to your configured Telegram Chat ID."
                    };

                case "send_telegram_qr":
                    return new AIProposedAction
                    {
                        ActionType = "send_telegram_qr",
                        Title = "Send iOS QR Code to Telegram",
                        Description = "Generates and sends the iOS pairing QR code photo to your Telegram chat."
                    };

                case "recreate_tunnel":
                    return new AIProposedAction
                    {
                        ActionType = "recreate_tunnel",
                        Title = "Recreate Cloudflare Quick Tunnel",
                        Description = "Generates a fresh public trycloudflare.com URL and restarts cloudflared."
                    };

                case "run_health_check":
                    return new AIProposedAction
                    {
                        ActionType = "run_health_check",
                        Title = "Run System Health Diagnostics",
                        Description = "Tests disk readiness, network interfaces, and database connectivity."
                    };

                case "unload_model":
                    return new AIProposedAction
                    {
                        ActionType = "unload_model",
                        Title = "Free Memory (Unload Local AI Model)",
                        Description = "Asks Ollama to evict the loaded model from RAM so backups get full host memory."
                    };

                case "emergency_stop":
                    return new AIProposedAction
                    {
                        ActionType = "emergency_stop",
                        Title = "Emergency Stop All Tasks",
                        Description = "Immediately halts all running and queued backup operations."
                    };

                case "clear_history":
                    return new AIProposedAction
                    {
                        ActionType = "clear_history",
                        Title = "Clear Local Backup History",
                        Description = "Purges all recorded historical log entries while preserving raw backup archives."
                    };

                case "switch_agent_profile":
                    var prof = parameters.TryGetValue("profile", out var pr) ? pr : "guardian";
                    return new AIProposedAction
                    {
                        ActionType = "switch_agent_profile",
                        Title = $"Switch Agent Profile ({prof})",
                        Description = $"Changes active AI personality to {prof}.",
                        Parameters = new Dictionary<string, string> { { "profile", prof } }
                    };

                default:
                    return null;
            }
        }

        /// <summary>
        /// Scans LLM reply text for [ACTION: action_type(param="val")] and extracts the typed action.
        /// </summary>
        public static (string cleanText, AIProposedAction? action) ExtractActionFromReply(string rawReply)
        {
            if (string.IsNullOrWhiteSpace(rawReply)) return (rawReply, null);

            var match = Regex.Match(rawReply, @"\[ACTION:\s*([a-zA-Z0-9_]+)(?:\((.*?)\))?\]");
            if (!match.Success) return (rawReply, null);

            var actionType = match.Groups[1].Value.ToLowerInvariant();
            var paramsStr = match.Groups[2].Success ? match.Groups[2].Value : "";
            var parameters = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(paramsStr))
            {
                var paramMatches = Regex.Matches(paramsStr, @"([a-zA-Z0-9_]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|(\S+))");
                foreach (Match pm in paramMatches)
                {
                    var pKey = pm.Groups[1].Value.ToLowerInvariant();
                    var pVal = pm.Groups[2].Success && !string.IsNullOrEmpty(pm.Groups[2].Value) ? pm.Groups[2].Value
                             : pm.Groups[3].Success && !string.IsNullOrEmpty(pm.Groups[3].Value) ? pm.Groups[3].Value
                             : pm.Groups[4].Value;
                    parameters[pKey] = pVal;
                }
            }

            var clean = rawReply.Replace(match.Value, "").Trim();
            var action = CreateActionFromType(actionType, parameters);
            return (clean, action);
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

        /// <summary>
        /// Asks Ollama to evict the model from RAM. Useful before a heavy backup starts so
        /// the 16 GB of system memory is not held hostage by an idle chat model.
        /// </summary>
        public static async Task<(bool ok, string message)> UnloadOllamaModelAsync()
        {
            var ep = _config.OllamaEndpoint.TrimEnd('/');
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(4000);
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{ep}/api/generate")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            model = _config.OllamaModel,
                            keep_alive = 0
                        }),
                        Encoding.UTF8, "application/json")
                };

                using var resp = await _httpClient.SendAsync(req, cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    return (true, $"Released the local model. Memory is now free for backups.");
                }

                return (false, $"Ollama responded HTTP {(int)resp.StatusCode}.");
            }
            catch (Exception ex)
            {
                return (false, $"Could not reach Ollama: {ex.Message}");
            }
        }

        /// <summary>
        /// Ollama runtime options. The thread cap matters on a 6-core part: leaving it on
        /// "auto" lets the model saturate every core and starve the backup engine and
        /// telemetry timers, which is what makes a local agent feel like it "eats the PC".
        /// </summary>
        private static Dictionary<string, object> BuildOllamaOptions()
        {
            var options = new Dictionary<string, object>
            {
                ["temperature"] = Math.Clamp(_config.Creativity, 0.0, 1.5),
                ["num_ctx"] = 2048
            };

            var threads = Math.Clamp(_config.OllamaThreads, 0, 64);
            if (threads > 0) options["num_thread"] = threads;

            return options;
        }

        private static async Task<string> QueryOllamaAsync(string prompt, AIProposedAction? action)
        {
            try
            {
                var requestBody = new
                {
                    model = _config.OllamaModel,
                    messages = BuildChatMessages(prompt, action),
                    stream = false,
                    keep_alive = "5m",
                    options = BuildOllamaOptions()
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_config.OllamaEndpoint.TrimEnd('/')}/api/chat", content);

                if (!response.IsSuccessStatusCode) return "";
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return "";

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("message", out var msgProp) &&
                    msgProp.TryGetProperty("content", out var contentProp))
                {
                    return contentProp.GetString() ?? "";
                }

                // Tolerate older Ollama builds that only expose /api/generate semantics.
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
                var endpoint = _config.CloudEndpoint.TrimEnd('/') + "/chat/completions";

                var requestBody = new
                {
                    model = _config.CloudModel,
                    messages = BuildChatMessages(prompt, action, isCloud: true),
                    temperature = Math.Clamp(_config.Creativity, 0.0, 1.5),
                    max_tokens = 800
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

            // Memory queries
            if (_config.EnableLearningMemory && (lower.Contains("remember") || lower.Contains("memory") || lower.Contains("learned")))
            {
                var mem = TryHandleMemoryCommand(prompt);
                if (!string.IsNullOrWhiteSpace(mem)) return mem;
            }

            // Telegram status and commands
            if (lower.Contains("telegram") || lower.Contains("tg bot"))
            {
                var tokenConfigured = !string.IsNullOrWhiteSpace(TelegramService.BotToken);
                var chatConfigured = !string.IsNullOrWhiteSpace(TelegramService.ChatId);
                var maskedChat = chatConfigured ? TelegramService.ChatId : "(Not configured yet)";
                return $@"### 🤖 Telegram Bot & Remote Alert Pipeline
- **Status:** {(tokenConfigured && chatConfigured ? "🟢 Active & Operational" : "⚠️ Needs Setup")}
- **Bot Token:** {(tokenConfigured ? "Guarded behind Zero-Leak Sanitizer" : "Missing (Configure in Settings)")}
- **Authorized Chat ID:** `{maskedChat}`
- **Alert Triggers:** Backup Start (🚀), Complete (✅), Failure (🚨), Outdated (⏰), Disconnect (⚠️)
- **iOS App Recovery:** Ready (say *""Send QR code to Telegram""* or type `/qr` in Telegram)

*Tip: You can also chat with me or trigger backups directly in Telegram by typing `/ai [question]` or `/backup full`.*";
            }

            // Agent Profile & Capabilities
            if (lower.Contains("agent") || lower.Contains("profile") || lower.Contains("who are you") || lower.Contains("your role") || lower.Contains("what can you do"))
            {
                return $@"### 🤖 AI Agent Profile: {_config.GetProfileTitle()}
- **Current Role:** {_config.GetProfileInstructions()}
- **Active Engine:** `{_config.Provider.ToUpper()}`
- **Memory Store:** {(_config.EnableLearningMemory ? $"🟢 Active ({AIMemoryStore.GetAll().Count} facts learned)" : "🔴 Disabled")}
- **Security:** Guarded behind PinayPal Zero-Leak Shield & Action Confirmation Rail

*To switch personas, say:*
- *""Switch to SRE Guardian""* — System stability, temperatures, and watchdogs.
- *""Switch to Backup Specialist""* — Database integrity, snapshot diffs, and Mailchimp.
- *""Switch to Speedy""* — Fast, terse answers.";
            }

            // Greetings
            if (lower.Contains("hello") || lower.Contains("hi") || lower.Contains("hey") || lower.Contains("good morning") || lower.Contains("good evening"))
            {
                var memCount = AIMemoryStore.GetAll().Count;
                var memoryNote = memCount > 0 ? $" I remember your {memCount} custom preferences." : "";
                return $"Hello! I'm your **PinayPal AI Assistant** operating as **{_config.GetProfileTitle()}** on **{Environment.MachineName}**.{memoryNote}\n\nAll watchdogs and backup schedulers are active. You can ask me to inspect health, trigger backups, check Telegram alerts, or manage your fleet of computers.";
            }

            // Backup History & Recent Records
            if (lower.Contains("history") || lower.Contains("recent backup") || lower.Contains("last backup") || lower.Contains("when was the last") || lower.Contains("show backups"))
            {
                var history = BackupHistoryService.GetHistory().Take(5).ToList();
                if (!history.Any())
                {
                    return "📋 **No backup records found yet.**\n\nWould you like me to trigger an initial backup for **Website FTP**, **SQL Database**, or **Mailchimp**?";
                }

                var sb = new StringBuilder();
                sb.AppendLine("### 📋 Recent Backup Records");
                foreach (var h in history)
                {
                    var isSuccess = h.Status.Equals("Success", StringComparison.OrdinalIgnoreCase);
                    var icon = isSuccess ? "✅" : "⚠️";
                    var sizeStr = h.SizeBytes > 0 ? (h.SizeBytes > 1024 * 1024 * 1024 ? $"{h.SizeBytes / (1024.0 * 1024 * 1024):F1} GB" : $"{h.SizeBytes / (1024.0 * 1024):F1} MB") : "--";
                    var durStr = h.Duration.TotalSeconds > 0 ? $"{h.Duration.TotalSeconds:F1}s" : "--";
                    var err = !string.IsNullOrEmpty(h.ErrorMessage) ? $" (`{h.ErrorMessage}`)" : "";
                    sb.AppendLine($"- {icon} **{h.Service.ToUpper()}** — {TimeFormat.Compact(h.Timestamp)} | Status: **{h.Status}** | Size: `{sizeStr}` | Duration: `{durStr}`{err}");
                }
                return sb.ToString();
            }

            // Active Backup & Queue Status
            if (lower.Contains("active") || lower.Contains("running") || lower.Contains("progress") || lower.Contains("queue") || lower.Contains("current backup"))
            {
                var isRunning = IsAnyBackupRunning?.Invoke() ?? false;
                if (isRunning)
                {
                    var details = GetActiveBackupDetails?.Invoke() ?? "Processing backup payload...";
                    return $"⚡ **Backup In Progress!**\n- **Active Services:** {details}\n- Automated watchdogs and safe transaction barriers are currently locking file pointers.";
                }
                return "🟢 **Idle & Ready.** No backup operations are currently running. All automated schedulers and watchdogs are actively guarding the system.";
            }

            // Network & Latency Diagnostics
            if (lower.Contains("network") || lower.Contains("ping") || lower.Contains("latency") || lower.Contains("ip") || lower.Contains("connection"))
            {
                var cfUrl = CloudflareTunnelService.ActiveUrl ?? "Inactive";
                var tsUrl = TailscaleNetworkService.GetTailscaleUrl();
                var tsDisplay = string.IsNullOrEmpty(tsUrl) ? "Inactive" : tsUrl;
                var localIps = FileDownloadService.GetAllLocalIPv4Addresses();
                var lanIp = localIps.Count > 0 ? string.Join(", ", localIps) : "127.0.0.1";
                var onlineStr = NetworkConnectivityService.IsOnline ? "🟢 Online" : "🔴 Offline";
                return $@"### 🌐 Network & Failover Routing
- **Internet Connectivity:** {onlineStr}
- **Local LAN IP:** `{lanIp}` (Port 8080)
- **Cloudflare Tunnel:** {(CloudflareTunnelService.IsRunning ? "🟢 Online" : "🔴 Offline")} (`{cfUrl}`)
- **Tailscale Mesh IP:** `{tsDisplay}`
- **Security:** Guarded behind TLS & Zero-Leak Sanitizer";
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
            if (lower.Contains("error") || lower.Contains("fail") || lower.Contains("why") || lower.Contains("logs") || lower.Contains("troubleshoot"))
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

            // Managed computers fleet
            if (lower.Contains("computer") || lower.Contains("devpc") || lower.Contains("dev pc")
                || lower.Contains("mainpc") || lower.Contains("main pc") || lower.Contains("my pc")
                || lower.Contains("other pc") || lower.Contains("fleet"))
            {
                var fleet = await ComputerManagementService.GetFleetAsync(forceRefresh: true);
                return ComputerManagementService.DescribeFleet(fleet)
                    + "\n\nYou can ask me to **wake**, **restart**, **shut down**, **lock**, or **sleep** any of them by name.";
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
        // Conversation memory / prompt assembly
        // ==========================================

        /// <summary>
        /// Builds a full OpenAI/Ollama-compatible message array: a rich system persona,
        /// the recent conversation turns (so follow-ups like "yes" or "that one" resolve),
        /// and finally the new user message.
        /// </summary>
        private static List<Dictionary<string, string>> BuildChatMessages(string prompt, AIProposedAction? action, bool isCloud = false)
        {
            var messages = new List<Dictionary<string, string>>
            {
                new() { ["role"] = "system", ["content"] = BuildSystemPersona(isCloud) }
            };

            var depth = isCloud
                ? Math.Clamp(_config.CloudMaxHistoryTurns, 0, 20)
                : Math.Clamp(_config.ConversationMemoryDepth, 0, 40);

            if (depth > 0)
            {
                List<ChatMessage> recent;
                lock (_historyLock)
                {
                    // Drop turns that already carry an action card; the card itself speaks for them.
                    recent = _sessionHistory
                        .Where(m => m.ProposedAction == null && !string.IsNullOrWhiteSpace(m.Content))
                        .Skip(Math.Max(0, _sessionHistory.Count - depth - 1))
                        .Take(depth)
                        .ToList();
                }

                foreach (var m in recent)
                {
                    var text = SanitizeOutput(m.Content);

                    // Cloud mode never receives machine names, IPs or raw error text.
                    if (isCloud && _config.CloudRedactsContext) text = RedactForCloud(text);

                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (text.Length > 1200) text = text[..1200];
                    messages.Add(new Dictionary<string, string>
                    {
                        ["role"] = m.Role == "user" ? "user" : "assistant",
                        ["content"] = text
                    });
                }
            }

            var userText = prompt;
            if (isCloud && _config.CloudRedactsContext) userText = RedactForCloud(userText);

            messages.Add(new Dictionary<string, string> { ["role"] = "user", ["content"] = userText });

            if (action != null)
            {
                messages.Add(new Dictionary<string, string>
                {
                    ["role"] = "system",
                    ["content"] = $"An action card is already shown to the user for '{action.Title}'. Tell them what it will do and ask them to tap Approve. Do not claim it already ran."
                });
            }

            return messages;
        }

        /// <summary>
        /// Strips identifying details before anything is sent to a cloud provider:
        /// hostnames, IP addresses, URLs, file paths and computer names are replaced
        /// with neutral placeholders. Aggregate numbers (percentages, sizes, statuses)
        /// survive, because they are what the assistant actually reasons about.
        /// </summary>
        private static string RedactForCloud(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // URLs (incl. tunnel URLs) -> neutral marker
            var redacted = Regex.Replace(text, @"https?://[^\s`\)\]]+", "[link]");

            // IPv4 addresses -> neutral marker
            redacted = Regex.Replace(redacted, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", "[address]");

            // MAC addresses -> neutral marker
            redacted = Regex.Replace(redacted, @"\b([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}\b", "[device]");

            // This machine's hostname and any registered computer names
            redacted = Regex.Replace(redacted, Regex.Escape(Environment.MachineName), "[host]", RegexOptions.IgnoreCase);
            foreach (var node in ComputerManagementService.GetNodes())
            {
                if (!string.IsNullOrWhiteSpace(node.DisplayName) && node.DisplayName.Length > 2)
                {
                    redacted = Regex.Replace(redacted, Regex.Escape(node.DisplayName), "[computer]", RegexOptions.IgnoreCase);
                }
            }

            // Windows user directories
            redacted = Regex.Replace(redacted, @"(?i)[a-z]:\\users\\[^\\]+", @"[user-path]");
            redacted = Regex.Replace(redacted, @"(?i)[a-z]:\\(?!users)[^\s`\)\]]*", "[path]");

            // Drive letters like "C:" on their own
            redacted = Regex.Replace(redacted, @"\b[A-Z]:\\", "[drive]");

            return redacted;
        }

        /// <summary>The assistant's standing persona, capabilities and hard security rules.</summary>
        private static string BuildSystemPersona(bool isCloud = false)
        {
            var name = string.IsNullOrWhiteSpace(_config.AssistantName) ? "Antigravity" : _config.AssistantName.Trim();
            var verbosity = _config.Talkativeness switch
            {
                < 30 => "Answer in one or two crisp sentences.",
                < 70 => "Be concise but friendly, using short markdown when helpful.",
                _ => "Be warm and conversational, and add useful context or a next step."
            };

            var memoryBlock = _config.EnableLearningMemory
                ? AIMemoryStore.FormatMemoriesForPrompt()
                : "- (Persistent memory is disabled)";

            return $@"You are {name}, the intelligent AI assistant built into PinayPal Backup Manager on Windows.
ACTIVE AGENT ROLE: {_config.GetProfileTitle()}
{_config.GetProfileInstructions()}

PERSONALITY
{verbosity} You are encouraging, precise, and honest. When something is wrong, say so plainly and
propose the fix. Never invent data — only report what is in the CONTEXT below.

WHAT YOU CAN DO
- Answer questions about backup health, disk space, schedules, and network/tunnel status.
- Inspect hardware telemetry (CPU, GPU, RAM, temperatures) for every managed computer.
- Control Telegram notifications and generate iOS reconnection QR codes.
- Learn and remember user preferences, custom instructions, and operational facts across sessions.
- Propose actions the user can approve with one tap: run backups, recreate the Cloudflare tunnel,
  test Telegram alerts, send iOS QR codes, wake a PC over the network, or restart/shut down computers.

ACTION PROPOSALS & CAPABILITIES
When the user asks you to perform an operational task, or when an issue requires remediation, explain what you will do and append the structured action tag:
[ACTION: action_type(param1=""value1"")]
Permitted action types:
- run_backup(service=""all"" | ""ftp"" | ""sql"" | ""mailchimp"" | ""members"" | ""campaigns"" | ""reports"" | ""merge_fields"" | ""tags"")
- recreate_tunnel()
- run_health_check()
- test_telegram()
- send_telegram_qr()
- computer_power(computerId=""..."", action=""wake"" | ""restart"" | ""shutdown"" | ""lock"" | ""sleep"")
- sync_preview(service=""ftp"" | ""sql"" | ""mailchimp"")
- sync_rollback(service=""ftp"" | ""sql"" | ""mailchimp"")
- unload_model()
- clear_history()
- emergency_stop()
- switch_agent_profile(profile=""guardian"" | ""specialist"" | ""speedy"")
Never claim you already ran the action — tell the user what will happen and that they can tap Approve on the action card.

LEARNED USER PREFERENCES & MEMORIES
{memoryBlock}

HARD SECURITY RULES
- You never see, echo, or request passwords, PINs, API keys, connection strings, or database dumps.
- Every state-changing action requires explicit user approval first. Never bypass confirmation.
- If asked for a secret, politely decline and explain the Zero-Leak Sanitizer.
{(isCloud && _config.CloudRedactsContext ? @"
PRIVACY NOTE FOR THIS REQUEST
This conversation is handled by an online provider. Hostnames, IP addresses, URLs, file paths
and computer names have been replaced with placeholders like [host], [address] and [computer].
Refer to computers by their role (""the Dev PC"", ""the Main PC"") rather than by name.
Never ask the user to repeat identifying details, and never suggest that you can see them." : string.Empty)}

LIVE CONTEXT
{BuildSanitizedSystemContext(isCloud)}";
        }
        // ANCHOR_PROACTIVE
        // ==========================================
        // Proactive updates
        // ==========================================

        private static void StartProactiveScheduler()
        {
            try
            {
                _proactiveTimer?.Dispose();
                _proactiveTimer = null;

                if (!_config.EnableProactiveUpdates || _config.ProactiveIntervalMinutes <= 0) return;

                var period = TimeSpan.FromMinutes(Math.Clamp(_config.ProactiveIntervalMinutes, 5, 1440));
                _proactiveTimer = new System.Threading.Timer(async _ => await RunProactiveTickAsync(), null, period, period);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Proactive scheduler failed to start: {ex.Message}", "Warning", "AI");
            }
        }

        /// <summary>Restarts the proactive timer after configuration changes.</summary>
        public static void RestartProactiveScheduler() => StartProactiveScheduler();

        private static async Task RunProactiveTickAsync()
        {
            try
            {
                // Never interrupt an in-flight backup with a notification.
                if (!_config.EnableProactiveUpdates || IsAnyBackupRunning?.Invoke() == true) return;

                var briefing = await BuildProactiveBriefingAsync();
                if (!string.IsNullOrWhiteSpace(briefing))
                {
                    OnNotificationBubble?.Invoke(briefing);
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Proactive tick failed: {ex.Message}", "Warning", "AI");
            }
        }

        /// <summary>Summarises fleet health and backup outcomes; returns null when nothing is notable.</summary>
        public static async Task<string?> BuildProactiveBriefingAsync()
        {
            try
            {
                await Task.Yield();

                var history = BackupHistoryService.GetHistory().Take(5).ToList();
                var failures = history.Where(h => !h.Status.Equals("Success", StringComparison.OrdinalIgnoreCase)).ToList();

                var telemetry = HardwareTelemetryService.GetTelemetrySync();
                var alerts = new List<string>();

                if (failures.Any())
                {
                    var names = string.Join(", ", failures.Take(2).Select(f => f.Service));
                    alerts.Add($"{failures.Count} of the last {history.Count} backups did not succeed ({names}).");
                }

                if (telemetry.RamUsagePercent > 90)
                    alerts.Add($"Memory is at {telemetry.RamUsagePercent:F0}%.");

                if (telemetry.CpuTempC.HasValue && telemetry.CpuTempC.Value >= 85)
                    alerts.Add($"CPU temperature is high at {telemetry.CpuTempC.Value:F0}°C.");

                if (!CloudflareTunnelService.IsRunning)
                    alerts.Add("The Cloudflare remote-access tunnel is offline.");

                if (alerts.Count == 0) return null;

                return $"🔔 **Status update** — {string.Join(" ", alerts)} Tap me if you want me to fix any of it.";
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[AIAssistant] Briefing build failed: {ex.Message}", "Warning", "AI");
                return null;
            }
        }

        // ==========================================
        // Zero-Leak Sanitizer
        // ==========================================
        public static string SanitizePrompt(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            if (!_config.EnableZeroLeakSanitizer) return input;

            // Redact Telegram Bot API tokens first (e.g. 1234567890:ABCdefGHIjklMNOpqrsTUVwxyz1234567)
            var sanitized = Regex.Replace(input, @"\b\d{8,12}:[A-Za-z0-9_-]{30,45}\b", "[REDACTED_TELEGRAM_TOKEN]");

            // Redact database connection strings.
            sanitized = Regex.Replace(
                sanitized,
                @"(?i)\b(server|data source|host)\s*=\s*[^;]+;(?:[^;]*;)*[^;]*(password|pwd)\s*=\s*[^;]+;?",
                "[REDACTED_CONNECTION_STRING]");

            // Redact credentials embedded in URLs (e.g. ftp://user:pass@host or https://user:pass@host)
            sanitized = Regex.Replace(sanitized, @"(?i)(https?|ftp|sftp)://([^:\s]+):([^@\s]+)@", "$1://$2:[REDACTED]@");

            // Redact bearer / basic authorization headers.
            sanitized = Regex.Replace(sanitized, @"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9_\-\.\+/=]{8,}", "$1 [REDACTED]");

            // Redact Cloudflare Tunnel tokens & JWTs
            sanitized = Regex.Replace(sanitized, @"\beyJ[A-Za-z0-9_-]{30,}\b", "[REDACTED_TUNNEL_TOKEN]");

            // Redact provider-style API keys that appear bare in text (OpenAI, Google Gemini, Anthropic, Mailchimp)
            sanitized = Regex.Replace(sanitized, @"\b(sk-[A-Za-z0-9_\-]{12,}|AIza[0-9A-Za-z_\-]{20,}|[0-9a-f]{32}-us\d+)\b", "[REDACTED_KEY]");

            // Redact SSH/PEM private keys
            sanitized = Regex.Replace(sanitized, @"(?s)-----BEGIN[ A-Z0-9_-]+KEY-----.*?-----END[ A-Z0-9_-]+KEY-----", "[REDACTED_PRIVATE_KEY]");

            // Redact passwords, keys and secrets regardless of separator style (e.g. password: 123, password = 123, password is 123).
            // Uses negative lookahead (?!\[REDACTED) to avoid clobbering specific redaction tags already applied.
            sanitized = Regex.Replace(
                sanitized,
                @"(?i)\b(password|passwd|pwd|secret|pin|token|apikey|api[_-]?key|authorization|auth|credential)\b\s*(?:[:=]|\bis\b)\s*(?!\[REDACTED)\S+",
                "$1=[REDACTED]");

            // Obfuscate Windows user directories.
            sanitized = Regex.Replace(sanitized, @"(?i)[a-z]:\\users\\[^\\]+\\", @"C:\Users\[User]\");

            return sanitized;
        }

        public static string SanitizeOutput(string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            if (!_config.EnableZeroLeakSanitizer) return output;

            // Never let a model echo a secret back to the user, even if it was injected.
            var sanitized = Regex.Replace(output, @"\b\d{8,12}:[A-Za-z0-9_-]{30,45}\b", "[REDACTED_TELEGRAM_TOKEN]");
            sanitized = Regex.Replace(sanitized, @"(?i)(https?|ftp|sftp)://([^:\s]+):([^@\s]+)@", "$1://$2:[REDACTED]@");
            sanitized = Regex.Replace(sanitized, @"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9_\-\.\+/=]{8,}", "$1 [REDACTED]");
            sanitized = Regex.Replace(sanitized, @"\beyJ[A-Za-z0-9_-]{30,}\b", "[REDACTED_TUNNEL_TOKEN]");
            sanitized = Regex.Replace(sanitized, @"\b(sk-[A-Za-z0-9_\-]{12,}|AIza[0-9A-Za-z_\-]{20,}|[0-9a-f]{32}-us\d+)\b", "[REDACTED_KEY]");
            sanitized = Regex.Replace(sanitized, @"(?s)-----BEGIN[ A-Z0-9_-]+KEY-----.*?-----END[ A-Z0-9_-]+KEY-----", "[REDACTED_PRIVATE_KEY]");
            sanitized = Regex.Replace(
                sanitized,
                @"(?i)\b(password|passwd|pwd|secret|pin|token|apikey|api[_-]?key)\b\s*(?:[:=]|\bis\b)\s*(?!\[REDACTED)\S+",
                "$1=[REDACTED]");

            return sanitized;
        }

        private static string BuildSanitizedSystemContext(bool isCloud = false)
        {
            try
            {
                var telemetry = HardwareTelemetryService.GetTelemetrySync();
                var history = BackupHistoryService.GetHistory().Take(3);
                var recentSummaries = string.Join("; ", history.Select(h => $"{h.Service}:{h.Status}({h.Duration.TotalSeconds:F0}s)"));

                // Managed computers (name, online state, headline load) for fleet-aware answers.
                var fleet = ComputerManagementService.GetNodes()
                    .Where(n => n.Enabled)
                    .Select(n =>
                    {
                        var isLocal = n.IsLocal;
                        return isLocal
                            ? $"- {n.DisplayName} (this PC): online, CPU {telemetry.CpuUsagePercent:F0}%, RAM {telemetry.RamUsagePercent:F0}%"
                            : $"- {n.DisplayName}: peer at {(string.IsNullOrWhiteSpace(n.ApiBaseUrl) ? "no URL configured" : n.ApiBaseUrl)}";
                    })
                    .ToList();

                var fleetBlock = fleet.Count > 0 ? string.Join("\n", fleet) : "- (none registered)";

                // In cloud mode we send rollups only: no hostnames, no peer URLs, no paths.
                var context = isCloud
                    ? $@"Host: [host]
OS: {RuntimeInformation.OSDescription}
CPU Load: {telemetry.CpuUsagePercent:F0}% across {telemetry.CpuLogicalCores} threads
RAM: {telemetry.RamUsagePercent:F0}% used ({telemetry.RamFreeGB:F1} GB free)
GPU Load: {telemetry.GpuUsagePercent?.ToString("F0") ?? "n/a"}%, Temp: {telemetry.GpuTempC?.ToString("F0") ?? "n/a"}°C
Tunnel: {(CloudflareTunnelService.IsRunning ? "Active" : "Offline")}
Recent Backups: {recentSummaries}
Managed Computers: {fleet.Count} registered ({string.Join(", ", ComputerManagementService.GetNodes().Where(n => n.Enabled).Select(n => ComputerManagementService.RoleLabel(n.Role)))})"
                    : $@"Host: {Environment.MachineName}
OS: {RuntimeInformation.OSDescription}
CPU: {telemetry.CpuName} ({telemetry.CpuUsagePercent:F0}% load)
RAM: {telemetry.RamUsagePercent:F0}% used ({telemetry.RamFreeGB:F1} GB free)
GPU: {telemetry.GpuName} ({telemetry.GpuTempC?.ToString("F0") ?? "n/a"}°C)
Tunnel: {(CloudflareTunnelService.IsRunning ? "Active" : "Offline")}
Recent Backups: {recentSummaries}
Managed Computers:
{fleetBlock}";

                // Belt and braces: run the whole block through the cloud scrubber too.
                return isCloud && _config.CloudRedactsContext ? RedactForCloud(context) : context;
            }
            catch
            {
                return isCloud
                    ? $"Host: [host], App: PinayPal v{BackupConfig.AppVersion}"
                    : $"Host: {Environment.MachineName}, App: PinayPal v{BackupConfig.AppVersion}";
            }
        }
    }
}
