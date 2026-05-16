using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.Utils
{
    /// <summary>
    /// Utilities for computing and verifying file checksums (MD5/SHA256).
    /// Stores hashes in local manifest files for integrity verification.
    /// </summary>
    public static class FileHashUtil
    {
        /// <summary>
        /// Computes MD5 hash of a file.
        /// </summary>
        public static async Task<string> ComputeMd5Async(string filePath)
        {
            // Validate file path to prevent path traversal
            var pathValidation = InputValidationService.ValidateFilePath(filePath);
            if (!pathValidation.isValid)
            {
                LogService.WriteSystemLog($"[HASH] Invalid file path: {pathValidation.error}", "Error", "SYSTEM");
                return string.Empty;
            }
            
            if (!File.Exists(pathValidation.sanitized))
                return string.Empty;

            try
            {
                using var md5 = MD5.Create();
                await using var stream = File.OpenRead(pathValidation.sanitized);
                var hash = await md5.ComputeHashAsync(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[HASH] Failed to compute MD5 for {Path.GetFileName(pathValidation.sanitized)}: {ex.Message}", "Error", "SYSTEM");
                return string.Empty;
            }
        }

        /// <summary>
        /// Computes SHA256 hash of a file (more secure, slightly slower).
        /// </summary>
        public static async Task<string> ComputeSha256Async(string filePath)
        {
            // Validate file path to prevent path traversal
            var pathValidation = InputValidationService.ValidateFilePath(filePath);
            if (!pathValidation.isValid)
            {
                LogService.WriteSystemLog($"[HASH] Invalid file path: {pathValidation.error}", "Error", "SYSTEM");
                return string.Empty;
            }
            
            if (!File.Exists(pathValidation.sanitized))
                return string.Empty;

            try
            {
                using var sha256 = SHA256.Create();
                await using var stream = File.OpenRead(pathValidation.sanitized);
                var hash = await sha256.ComputeHashAsync(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[HASH] Failed to compute SHA256 for {Path.GetFileName(pathValidation.sanitized)}: {ex.Message}", "Error", "SYSTEM");
                return string.Empty;
            }
        }

        /// <summary>
        /// Verifies a file against a stored hash in the checksum manifest.
        /// Returns true if file is verified or no prior hash exists.
        /// Returns false if file changed (hash mismatch).
        /// </summary>
        public static async Task<VerifyResult> VerifyFileAsync(string filePath, string folder)
        {
            // Validate file path to prevent path traversal
            var pathValidation = InputValidationService.ValidateFilePath(filePath);
            if (!pathValidation.isValid)
            {
                return new VerifyResult { IsVerified = false, Message = pathValidation.error };
            }
            
            if (!File.Exists(pathValidation.sanitized))
                return new VerifyResult { IsVerified = false, Message = "File not found" };

            var manifestPath = GetManifestPath(folder);
            var fileName = Path.GetFileName(pathValidation.sanitized);
            var fileInfo = new FileInfo(pathValidation.sanitized);

            // Load existing manifest
            var manifest = await LoadManifestAsync(manifestPath);

            if (!manifest.TryGetValue(fileName, out var storedEntry))
            {
                // No prior hash - compute and store
                var hash = await ComputeMd5Async(pathValidation.sanitized);
                if (string.IsNullOrEmpty(hash))
                    return new VerifyResult { IsVerified = false, Message = "Hash computation failed" };

                manifest[fileName] = new HashEntry
                {
                    Hash = hash,
                    Size = fileInfo.Length,
                    Modified = fileInfo.LastWriteTimeUtc,
                    VerifiedAt = DateTime.UtcNow
                };
                await SaveManifestAsync(manifestPath, manifest);

                return new VerifyResult { IsVerified = true, IsNew = true, Hash = hash, Message = "Initial hash stored" };
            }

            // Check if file metadata changed (quick check)
            if (storedEntry.Size != fileInfo.Length || storedEntry.Modified != fileInfo.LastWriteTimeUtc)
            {
                // Re-compute hash
                var currentHash = await ComputeMd5Async(pathValidation.sanitized);
                if (currentHash != storedEntry.Hash)
                {
                    // File changed - update manifest
                    manifest[fileName] = new HashEntry
                    {
                        Hash = currentHash,
                        Size = fileInfo.Length,
                        Modified = fileInfo.LastWriteTimeUtc,
                        VerifiedAt = DateTime.UtcNow
                    };
                    await SaveManifestAsync(manifestPath, manifest);

                    return new VerifyResult
                    {
                        IsVerified = false,
                        Hash = currentHash,
                        ExpectedHash = storedEntry.Hash,
                        Message = "File changed (size or content differs)"
                    };
                }
            }

            // File verified
            return new VerifyResult
            {
                IsVerified = true,
                Hash = storedEntry.Hash,
                VerifiedAt = storedEntry.VerifiedAt,
                Message = $"Verified (hash matches, stored {storedEntry.VerifiedAt:MM/dd HH:mm})"
            };
        }

        /// <summary>
        /// Stores a verified hash for a file after successful download.
        /// </summary>
        public static async Task StoreHashAsync(string filePath, string folder)
        {
            // Validate file path to prevent path traversal
            var pathValidation = InputValidationService.ValidateFilePath(filePath);
            if (!pathValidation.isValid || !File.Exists(pathValidation.sanitized))
                return;

            var manifestPath = GetManifestPath(folder);
            var fileName = Path.GetFileName(pathValidation.sanitized);
            var fileInfo = new FileInfo(pathValidation.sanitized);
            var hash = await ComputeMd5Async(pathValidation.sanitized);

            if (string.IsNullOrEmpty(hash))
                return;

            var manifest = await LoadManifestAsync(manifestPath);
            manifest[fileName] = new HashEntry
            {
                Hash = hash,
                Size = fileInfo.Length,
                Modified = fileInfo.LastWriteTimeUtc,
                VerifiedAt = DateTime.UtcNow
            };
            await SaveManifestAsync(manifestPath, manifest);

            LogService.WriteSystemLog($"[HASH] Stored MD5 for {fileName}: {hash[..16]}...", "Information", "SYSTEM");
        }

        private static string GetManifestPath(string folder)
        {
            return Path.Combine(folder, ".checksums.json");
        }

        private static async Task<Dictionary<string, HashEntry>> LoadManifestAsync(string manifestPath)
        {
            try
            {
                if (File.Exists(manifestPath))
                {
                    var json = await File.ReadAllTextAsync(manifestPath);
                    return JsonSerializer.Deserialize<Dictionary<string, HashEntry>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[HASH] Failed to load manifest: {ex.Message}", "Warning", "SYSTEM");
            }
            return new Dictionary<string, HashEntry>();
        }

        private static async Task SaveManifestAsync(string manifestPath, Dictionary<string, HashEntry> manifest)
        {
            try
            {
                var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(manifestPath, json);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[HASH] Failed to save manifest: {ex.Message}", "Warning", "SYSTEM");
            }
        }
    }

    public class VerifyResult
    {
        public bool IsVerified { get; set; }
        public bool IsNew { get; set; }
        public string Hash { get; set; } = "";
        public string ExpectedHash { get; set; } = "";
        public DateTime VerifiedAt { get; set; }
        public string Message { get; set; } = "";
    }

    public class HashEntry
    {
        public string Hash { get; set; } = "";
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public DateTime VerifiedAt { get; set; }
    }
}
