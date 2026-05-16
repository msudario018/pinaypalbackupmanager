using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PinayPalBackupManager.Services
{
    public static class FileHashCacheService
    {
        private static readonly ConcurrentDictionary<string, FileHashCacheEntry> _cache = new ConcurrentDictionary<string, FileHashCacheEntry>();
        private static readonly object _cleanupLock = new object();
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromHours(1);
        private static Timer? _cleanupTimer;
        
        static FileHashCacheService()
        {
            // Run cleanup every 30 minutes
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        }
        
        public static string? GetFileHash(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;
            
            var cacheKey = GetCacheKey(filePath);
            
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                // Check if cache entry is still valid
                if (DateTime.UtcNow - entry.Timestamp < _cacheDuration)
                {
                    // Verify file hasn't been modified since cache entry
                    try
                    {
                        var currentFileInfo = new FileInfo(filePath);
                        if (currentFileInfo.LastWriteTimeUtc == entry.FileLastModified && currentFileInfo.Length == entry.FileSize)
                        {
                            return entry.Hash;
                        }
                    }
                    catch
                    {
                        // File access failed, invalidate cache
                        _cache.TryRemove(cacheKey, out _);
                    }
                }
                else
                {
                    // Cache expired
                    _cache.TryRemove(cacheKey, out _);
                }
            }
            
            // Calculate hash and cache it
            var hash = CalculateFileHash(filePath);
            if (hash != null)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var newEntry = new FileHashCacheEntry
                    {
                        Hash = hash,
                        Timestamp = DateTime.UtcNow,
                        FileLastModified = fileInfo.LastWriteTimeUtc,
                        FileSize = fileInfo.Length
                    };
                    _cache.TryAdd(cacheKey, newEntry);
                }
                catch
                {
                    // File info failed, just return hash without caching
                }
            }
            
            return hash;
        }
        
        public static void InvalidateCache(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;
            
            var cacheKey = GetCacheKey(filePath);
            _cache.TryRemove(cacheKey, out _);
        }
        
        public static void InvalidateAllCache()
        {
            _cache.Clear();
        }
        
        private static string? CalculateFileHash(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hashBytes = sha256.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }
        
        private static string GetCacheKey(string filePath)
        {
            // Normalize the path for consistent caching
            return Path.GetFullPath(filePath).ToLowerInvariant();
        }
        
        private static void CleanupExpiredEntries(object? state)
        {
            lock (_cleanupLock)
            {
                var now = DateTime.UtcNow;
                foreach (var kvp in _cache)
                {
                    if (now - kvp.Value.Timestamp >= _cacheDuration)
                    {
                        _cache.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }
        
        public static int GetCacheCount()
        {
            return _cache.Count;
        }
        
        public static void ClearCache()
        {
            _cache.Clear();
        }
    }
    
    internal class FileHashCacheEntry
    {
        public string Hash { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public DateTime FileLastModified { get; set; }
        public long FileSize { get; set; }
    }
}
