using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class ChecksumService
    {
        private static readonly string ChecksumsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinayPalBackupManager", "checksums.json");

        public class ChecksumRecord
        {
            public string FilePath { get; set; } = "";
            public string FileName { get; set; } = "";
            public string Algorithm { get; set; } = "SHA256";
            public string Hash { get; set; } = "";
            public DateTime Created { get; set; } = DateTime.Now;
            public long FileSize { get; set; }
            public string Service { get; set; } = "";
        }

        public class VerificationResult
        {
            public bool IsValid { get; set; }
            public string FilePath { get; set; } = "";
            public string ExpectedHash { get; set; } = "";
            public string ActualHash { get; set; } = "";
            public string Status { get; set; } = "";
            public TimeSpan VerificationTime { get; set; }
        }

        public static async Task<ChecksumRecord> GenerateChecksumAsync(string filePath, string service)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var fileInfo = new FileInfo(filePath);
            var hash = await CalculateSHA256Async(filePath);

            return new ChecksumRecord
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                Algorithm = "SHA256",
                Hash = hash,
                Created = DateTime.Now,
                FileSize = fileInfo.Length,
                Service = service
            };
        }

        public static async Task<VerificationResult> VerifyChecksumAsync(ChecksumRecord record)
        {
            var startTime = DateTime.Now;
            
            try
            {
                if (!File.Exists(record.FilePath))
                {
                    return new VerificationResult
                    {
                        IsValid = false,
                        FilePath = record.FilePath,
                        Status = "File not found",
                        VerificationTime = DateTime.Now - startTime
                    };
                }

                var actualHash = await CalculateSHA256Async(record.FilePath);
                var isValid = actualHash.Equals(record.Hash, StringComparison.OrdinalIgnoreCase);

                return new VerificationResult
                {
                    IsValid = isValid,
                    FilePath = record.FilePath,
                    ExpectedHash = record.Hash,
                    ActualHash = actualHash,
                    Status = isValid ? "Valid" : "Corrupted",
                    VerificationTime = DateTime.Now - startTime
                };
            }
            catch (Exception ex)
            {
                return new VerificationResult
                {
                    IsValid = false,
                    FilePath = record.FilePath,
                    Status = $"Error: {ex.Message}",
                    VerificationTime = DateTime.Now - startTime
                };
            }
        }

        public static async Task<List<VerificationResult>> VerifyAllChecksumsAsync()
        {
            var checksums = LoadChecksums();
            var results = new List<VerificationResult>();

            foreach (var record in checksums)
            {
                var result = await VerifyChecksumAsync(record);
                results.Add(result);
            }

            return results;
        }

        public static async Task<List<VerificationResult>> VerifyServiceChecksumsAsync(string service)
        {
            var checksums = LoadChecksums().Where(c => c.Service.Equals(service, StringComparison.OrdinalIgnoreCase));
            var results = new List<VerificationResult>();

            foreach (var record in checksums)
            {
                var result = await VerifyChecksumAsync(record);
                results.Add(result);
            }

            return results;
        }

        public static async Task SaveChecksumAsync(ChecksumRecord record)
        {
            var checksums = LoadChecksums();
            
            // Remove existing record for same file
            checksums.RemoveAll(c => c.FilePath.Equals(record.FilePath, StringComparison.OrdinalIgnoreCase));
            
            // Add new record
            checksums.Add(record);
            
            // Keep only last 1000 records per service to prevent file from growing too large
            var serviceRecords = checksums.Where(c => c.Service == record.Service).ToList();
            if (serviceRecords.Count > 1000)
            {
                var toRemove = serviceRecords.OrderBy(c => c.Created).Take(serviceRecords.Count - 1000);
                checksums.RemoveAll(r => toRemove.Contains(r));
            }
            
            await SaveChecksumsAsync(checksums);
            
            LogService.WriteLiveLog($"[CHECKSUM] Generated {record.Algorithm} for {record.FileName} ({record.FileSize / 1024.0 / 1024.0:F1} MB)", "", "Information", "SYSTEM");
        }

        public static async Task SaveChecksumsForFolderAsync(string folder, string service)
        {
            if (!Directory.Exists(folder)) return;

            var files = new DirectoryInfo(folder)
                .GetFiles("*", SearchOption.AllDirectories)
                .Where(f => f.Name != "backuplog.txt" && f.Name != "checksums.json")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(50) // Only checksum recent files to avoid performance issues
                .ToList();

            LogService.WriteLiveLog($"[CHECKSUM] Generating checksums for {files.Count} {service} files...", "", "Information", "SYSTEM");

            foreach (var file in files)
            {
                try
                {
                    var record = await GenerateChecksumAsync(file.FullName, service);
                    await SaveChecksumAsync(record);
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[CHECKSUM] Failed to generate checksum for {file.Name}: {ex.Message}", "", "Warning", "SYSTEM");
                }
            }

            LogService.WriteLiveLog($"[CHECKSUM] Completed checksum generation for {service}", "", "Information", "SYSTEM");
        }

        public static List<ChecksumRecord> LoadChecksums()
        {
            try
            {
                if (!File.Exists(ChecksumsFile))
                    return new List<ChecksumRecord>();

                var json = File.ReadAllText(ChecksumsFile);
                return JsonSerializer.Deserialize<List<ChecksumRecord>>(json) ?? new List<ChecksumRecord>();
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[CHECKSUM] Error loading checksums: {ex.Message}", "", "Error", "SYSTEM");
                return new List<ChecksumRecord>();
            }
        }

        public static async Task SaveChecksumsAsync(List<ChecksumRecord> checksums)
        {
            try
            {
                var directory = Path.GetDirectoryName(ChecksumsFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(checksums, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(ChecksumsFile, json);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[CHECKSUM] Error saving checksums: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        public static async Task<bool> VerifyBackupIntegrityAsync(string filePath, string service)
        {
            try
            {
                var checksums = LoadChecksums();
                var record = checksums.FirstOrDefault(c => 
                    c.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                if (record == null)
                {
                    // Generate new checksum if none exists
                    record = await GenerateChecksumAsync(filePath, service);
                    await SaveChecksumAsync(record);
                    return true;
                }

                var result = await VerifyChecksumAsync(record);
                
                if (result.IsValid)
                {
                    LogService.WriteLiveLog($"[CHECKSUM] {Path.GetFileName(filePath)} verified successfully", "", "Information", "SYSTEM");
                    return true;
                }
                else
                {
                    LogService.WriteLiveLog($"[CHECKSUM] CORRUPTION DETECTED: {Path.GetFileName(filePath)} - {result.Status}", "", "Error", "SYSTEM");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[CHECKSUM] Error verifying {Path.GetFileName(filePath)}: {ex.Message}", "", "Error", "SYSTEM");
                return false;
            }
        }

        public static Dictionary<string, int> GetChecksumStatistics()
        {
            var checksums = LoadChecksums();
            var stats = new Dictionary<string, int>();

            foreach (var record in checksums)
            {
                var key = $"{record.Service} ({record.Algorithm})";
                stats[key] = stats.GetValueOrDefault(key, 0) + 1;
            }

            return stats;
        }

        public static async Task CleanupOldChecksumsAsync(int daysToKeep = 90)
        {
            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            var checksums = LoadChecksums();
            
            var originalCount = checksums.Count;
            checksums.RemoveAll(c => c.Created < cutoffDate);
            
            if (checksums.Count < originalCount)
            {
                await SaveChecksumsAsync(checksums);
                LogService.WriteLiveLog($"[CHECKSUM] Cleaned up {originalCount - checksums.Count} old checksum records", "", "Information", "SYSTEM");
            }
        }

        private static async Task<string> CalculateSHA256Async(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }

        public static async Task<(int Valid, int Corrupted, int Missing)> GetVerificationSummaryAsync()
        {
            var checksums = LoadChecksums();
            var valid = 0;
            var corrupted = 0;
            var missing = 0;

            foreach (var record in checksums)
            {
                if (!File.Exists(record.FilePath))
                {
                    missing++;
                    continue;
                }

                try
                {
                    var actualHash = await CalculateSHA256Async(record.FilePath);
                    if (actualHash.Equals(record.Hash, StringComparison.OrdinalIgnoreCase))
                        valid++;
                    else
                        corrupted++;
                }
                catch
                {
                    corrupted++;
                }
            }

            return (valid, corrupted, missing);
        }
    }
}
