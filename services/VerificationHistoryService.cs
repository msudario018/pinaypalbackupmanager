using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Service for saving and loading verification history
    /// </summary>
    public static class VerificationHistoryService
    {
        private static readonly string HistoryFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinayPalBackupManager", "VerificationHistory");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        static VerificationHistoryService()
        {
            Directory.CreateDirectory(HistoryFolder);
        }

        /// <summary>
        /// Save verification results to history
        /// </summary>
        public static async Task SaveVerificationAsync(List<ChecksumService.VerificationResult> results, string service = "All")
        {
            try
            {
                var historyEntry = new VerificationHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    Service = service,
                    TotalFiles = results.Count,
                    ValidFiles = results.Count(r => r.IsValid && r.Status != "File not found"),
                    CorruptedFiles = results.Count(r => !r.IsValid && r.Status != "File not found"),
                    MissingFiles = results.Count(r => r.Status == "File not found"),
                    Results = results.Select(r => new VerificationResultSummary
                    {
                        FilePath = r.FilePath,
                        IsValid = r.IsValid,
                        Status = r.Status
                    }).ToList()
                };

                var fileName = $"verification_{DateTime.Now:yyyyMMdd_HHmmss}_{service}.json";
                var filePath = Path.Combine(HistoryFolder, fileName);

                var json = JsonSerializer.Serialize(historyEntry, JsonOptions);
                await File.WriteAllTextAsync(filePath, json);

                // Cleanup old history files (keep last 30 days)
                await CleanupOldHistoryAsync();

                LogService.WriteLiveLog($"[VERIFICATION_HISTORY] Saved verification history to {fileName}", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION_HISTORY] Error saving verification history: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        /// <summary>
        /// Load the most recent verification history
        /// </summary>
        public static async Task<VerificationHistoryEntry?> LoadLastVerificationAsync(string service = "All")
        {
            try
            {
                var files = Directory.GetFiles(HistoryFolder, "verification_*.json")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();

                // Find the most recent file for the specified service
                var servicePattern = service == "All" ? "_" : $"_{service}.json";
                var latestFile = files.FirstOrDefault(f => 
                    service == "All" ? true : f.EndsWith(servicePattern));

                if (latestFile == null || !File.Exists(latestFile))
                    return null;

                var json = await File.ReadAllTextAsync(latestFile);
                var history = JsonSerializer.Deserialize<VerificationHistoryEntry>(json, JsonOptions);
                
                LogService.WriteLiveLog($"[VERIFICATION_HISTORY] Loaded last verification from {Path.GetFileName(latestFile)}", "", "Information", "SYSTEM");
                return history;
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION_HISTORY] Error loading verification history: {ex.Message}", "", "Error", "SYSTEM");
                return null;
            }
        }

        /// <summary>
        /// Get all verification history entries (last 30 days)
        /// </summary>
        public static List<VerificationHistoryEntry> GetVerificationHistory(int days = 30)
        {
            var history = new List<VerificationHistoryEntry>();
            var cutoffDate = DateTime.Now.AddDays(-days);

            try
            {
                var files = Directory.GetFiles(HistoryFolder, "verification_*.json")
                    .Where(f => File.GetLastWriteTime(f) > cutoffDate)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(100) // Limit to 100 entries
                    .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var entry = JsonSerializer.Deserialize<VerificationHistoryEntry>(json, JsonOptions);
                        if (entry != null)
                        {
                            entry.FileName = Path.GetFileName(file);
                            history.Add(entry);
                        }
                    }
                    catch
                    {
                        // Skip corrupted history files
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION_HISTORY] Error getting history: {ex.Message}", "", "Error", "SYSTEM");
            }

            return history;
        }

        /// <summary>
        /// Cleanup old history files, keeping only the last 30 days
        /// </summary>
        private static async Task CleanupOldHistoryAsync()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-30);
                var files = Directory.GetFiles(HistoryFolder, "verification_*.json")
                    .Where(f => File.GetLastWriteTime(f) < cutoffDate)
                    .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                }

                // Also limit total files to 100 most recent
                var allFiles = Directory.GetFiles(HistoryFolder, "verification_*.json")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Skip(100)
                    .ToList();

                foreach (var file in allFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION_HISTORY] Error cleaning up history: {ex.Message}", "", "Debug", "SYSTEM");
            }
        }

        /// <summary>
        /// Get statistics from verification history
        /// </summary>
        public static VerificationStatistics GetStatistics(int days = 7)
        {
            var history = GetVerificationHistory(days);
            
            return new VerificationStatistics
            {
                TotalVerifications = history.Count,
                AverageSuccessRate = history.Any() ? history.Average(h => h.SuccessRate) : 0,
                LastVerificationDate = history.FirstOrDefault()?.Timestamp,
                TotalFilesChecked = history.Sum(h => h.TotalFiles),
                TotalCorruptedFiles = history.Sum(h => h.CorruptedFiles),
                TotalMissingFiles = history.Sum(h => h.MissingFiles)
            };
        }
    }

    /// <summary>
    /// Represents a single verification history entry
    /// </summary>
    public class VerificationHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string Service { get; set; } = "All";
        public int TotalFiles { get; set; }
        public int ValidFiles { get; set; }
        public int CorruptedFiles { get; set; }
        public int MissingFiles { get; set; }
        public List<VerificationResultSummary> Results { get; set; } = new();
        
        [JsonIgnore]
        public string FileName { get; set; } = "";
        
        [JsonIgnore]
        public double SuccessRate => TotalFiles > 0 ? (double)ValidFiles / TotalFiles * 100 : 0;
        
        public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        public string Summary => $"{ValidFiles}/{TotalFiles} valid, {CorruptedFiles} corrupted, {MissingFiles} missing";
    }

    /// <summary>
    /// Summary of a single verification result
    /// </summary>
    public class VerificationResultSummary
    {
        public string FilePath { get; set; } = "";
        public bool IsValid { get; set; }
        public string Status { get; set; } = "";
    }

    /// <summary>
    /// Statistics from verification history
    /// </summary>
    public class VerificationStatistics
    {
        public int TotalVerifications { get; set; }
        public double AverageSuccessRate { get; set; }
        public DateTime? LastVerificationDate { get; set; }
        public int TotalFilesChecked { get; set; }
        public int TotalCorruptedFiles { get; set; }
        public int TotalMissingFiles { get; set; }
    }
}
