using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Represents a persistent memory entry learned by the AI Assistant.
    /// Strictly sanitized to prevent storing any credentials or secrets.
    /// </summary>
    public class AIMemoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Category { get; set; } = "preference"; // "preference", "fact", "instruction", "note"
        public string Key { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Thread-safe local storage for AI persistent knowledge, user preferences, and operational notes.
    /// Kept strictly local to AppData and bounded to prevent prompt injection or memory bloat.
    /// </summary>
    public static class AIMemoryStore
    {
        private static readonly string MemoryFilePath = AppDataPaths.GetPath("ai_memory.json");
        private static readonly object _lock = new();
        private static List<AIMemoryItem> _items = new();
        private const int MaxMemories = 50;

        static AIMemoryStore()
        {
            LoadMemories();
        }

        public static void LoadMemories()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(MemoryFilePath))
                    {
                        var json = File.ReadAllText(MemoryFilePath);
                        _items = JsonSerializer.Deserialize<List<AIMemoryItem>>(json) ?? new List<AIMemoryItem>();
                    }
                    else
                    {
                        _items = new List<AIMemoryItem>();
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[AIMemoryStore] Error loading memory: {ex.Message}", "Warning", "AI");
                    _items = new List<AIMemoryItem>();
                }
            }
        }

        private static void SaveMemories()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(MemoryFilePath, json);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[AIMemoryStore] Error saving memory: {ex.Message}", "Error", "AI");
                }
            }
        }

        public static IReadOnlyList<AIMemoryItem> GetAll()
        {
            lock (_lock)
            {
                return _items.OrderByDescending(i => i.UpdatedAt).ToList();
            }
        }

        /// <summary>
        /// Stores a learned memory or preference, applying Zero-Leak sanitization first.
        /// </summary>
        public static (bool ok, string message) Remember(string category, string key, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return (false, "Cannot store empty memory.");

            var cleanCategory = string.IsNullOrWhiteSpace(category) ? "preference" : category.Trim().ToLowerInvariant();
            var cleanKey = string.IsNullOrWhiteSpace(key) ? "general" : key.Trim().ToLowerInvariant();

            // Run content through Zero-Leak sanitizer to prevent secret leakage
            var sanitizedContent = AIAssistantService.SanitizePrompt(content.Trim());

            // If the content or key contains credentials or is redacted, block it
            if (sanitizedContent.Contains("[REDACTED") || sanitizedContent.Contains("[REDACTED_KEY]") ||
                Regex.IsMatch(content, @"(?i)\b(password|passwd|pwd|secret|token|apikey)\b") ||
                Regex.IsMatch(cleanKey, @"(?i)\b(password|passwd|pwd|secret|token|apikey)\b"))
            {
                return (false, "Security Guard: Credentials, tokens, or passwords cannot be stored in AI memory.");
            }

            lock (_lock)
            {
                // Check if key already exists in this category
                var existing = _items.FirstOrDefault(i =>
                    i.Category.Equals(cleanCategory, StringComparison.OrdinalIgnoreCase) &&
                    i.Key.Equals(cleanKey, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.Content = sanitizedContent;
                    existing.UpdatedAt = DateTime.UtcNow;
                    SaveMemories();
                    return (true, $"Updated learned knowledge for '{cleanKey}'.");
                }

                // Bound max entries
                if (_items.Count >= MaxMemories)
                {
                    var oldest = _items.OrderBy(i => i.UpdatedAt).First();
                    _items.Remove(oldest);
                }

                _items.Add(new AIMemoryItem
                {
                    Category = cleanCategory,
                    Key = cleanKey,
                    Content = sanitizedContent,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                SaveMemories();
                return (true, $"Learned and remembered: \"{sanitizedContent}\"");
            }
        }

        public static bool Forget(string keyOrId)
        {
            if (string.IsNullOrWhiteSpace(keyOrId)) return false;
            var target = keyOrId.Trim();

            lock (_lock)
            {
                var removed = _items.RemoveAll(i =>
                    i.Id.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                    i.Key.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                    i.Content.Contains(target, StringComparison.OrdinalIgnoreCase));

                if (removed > 0)
                {
                    SaveMemories();
                    return true;
                }
                return false;
            }
        }

        public static void ClearAll()
        {
            lock (_lock)
            {
                _items.Clear();
                SaveMemories();
            }
        }

        /// <summary>
        /// Formats learned memories into a compact markdown block to inject into the LLM system prompt.
        /// </summary>
        public static string FormatMemoriesForPrompt()
        {
            lock (_lock)
            {
                if (_items.Count == 0) return "- (No custom preferences or memories recorded yet)";

                var sb = new StringBuilder();
                foreach (var item in _items.Take(25))
                {
                    var tag = char.ToUpper(item.Category[0]) + item.Category.Substring(1);
                    sb.AppendLine($"- [{tag}] {item.Key}: {item.Content}");
                }
                return sb.ToString().TrimEnd();
            }
        }
    }
}
