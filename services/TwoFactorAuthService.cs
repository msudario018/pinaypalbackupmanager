using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class TwoFactorAuthService
    {
        private static readonly string TfaFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinayPalBackupManager", "2fa.json");

        private static readonly string FirebaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
        private static readonly string TfaPath = "2fa";
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public class TfaSettings
        {
            public int UserId { get; set; }
            public bool IsEnabled { get; set; }
            public string SecretKey { get; set; } = "";
            public List<string> BackupCodes { get; set; } = new();
            public DateTime SetupDate { get; set; }
        }

        public static async Task<bool> EnableTfaAsync(int userId)
        {
            // Back-compat: enable with a new generated secret if none is requested
            var secret = EnsureSecret(userId);
            return await EnableTfaAsync(userId, secret);
        }

        public static async Task<bool> EnableTfaAsync(int userId, string secret)
        {
            var settings = LoadSettings();

            var entry = settings.FirstOrDefault(s => s.UserId == userId);
            if (entry == null)
            {
                entry = new TfaSettings
                {
                    UserId = userId,
                    SecretKey = string.IsNullOrWhiteSpace(secret) ? GenerateSecretKey() : secret,
                    IsEnabled = false,
                    BackupCodes = new List<string>(),
                    SetupDate = DateTime.Now
                };
                settings.Add(entry);
            }

            entry.SecretKey = string.IsNullOrWhiteSpace(secret) ? entry.SecretKey : secret;
            entry.IsEnabled = true;
            if (entry.BackupCodes == null || entry.BackupCodes.Count == 0)
                entry.BackupCodes = GenerateBackupCodes();

            await SaveSettingsAsync(settings);
            _ = SyncToFirebaseAsync(userId, entry);
            LogService.WriteLiveLog($"[2FA] Enabled for user {userId}", "", "Information", "SYSTEM");
            return true;
        }

        public static async Task<bool> DisableTfaAsync(int userId)
        {
            var settings = LoadSettings();
            var userTfa = settings.FirstOrDefault(s => s.UserId == userId);
            
            if (userTfa == null || !userTfa.IsEnabled)
                return false;

            // Keep the entry and secret for convenience, just mark disabled
            userTfa.IsEnabled = false;
            await SaveSettingsAsync(settings);
            _ = SyncToFirebaseAsync(userId, userTfa);
            
            LogService.WriteLiveLog($"[2FA] Disabled for user {userId}", "", "Information", "SYSTEM");
            return true;
        }

        public static bool IsEnabled(int userId)
        {
            var settings = LoadSettings();
            return settings.Any(s => s.UserId == userId && s.IsEnabled);
        }

        public static bool VerifyCode(int userId, string code)
        {
            var settings = LoadSettings();
            var userTfa = settings.FirstOrDefault(s => s.UserId == userId);
            
            if (userTfa == null || !userTfa.IsEnabled)
                return true; // 2FA not enabled, allow

            // Check if it's a backup code
            if (userTfa.BackupCodes.Contains(code))
            {
                userTfa.BackupCodes.Remove(code);
                _ = SaveSettingsAsync(settings); // Remove used backup code
                return true;
            }

            // Verify TOTP code
            var expectedCode = GenerateTotpCode(userTfa.SecretKey);
            return code == expectedCode;
        }

        public static string GetSecretKey(int userId)
        {
            var settings = LoadSettings();
            var userTfa = settings.FirstOrDefault(s => s.UserId == userId);
            return userTfa?.SecretKey ?? "";
        }

        public static string EnsureSecret(int userId)
        {
            var settings = LoadSettings();
            var userTfa = settings.FirstOrDefault(s => s.UserId == userId);
            if (userTfa != null && !string.IsNullOrWhiteSpace(userTfa.SecretKey))
                return userTfa.SecretKey;

            var secret = GenerateSecretKey();
            if (userTfa == null)
            {
                userTfa = new TfaSettings { UserId = userId, SecretKey = secret, IsEnabled = false, BackupCodes = new List<string>(), SetupDate = DateTime.Now };
                settings.Add(userTfa);
            }
            else
            {
                userTfa.SecretKey = secret;
                userTfa.IsEnabled = false;
            }

            // Fire and forget save
            _ = SaveSettingsAsync(settings);
            return secret;
        }

        public static List<string> GetBackupCodes(int userId)
        {
            var settings = LoadSettings();
            var userTfa = settings.FirstOrDefault(s => s.UserId == userId);
            return userTfa?.BackupCodes ?? new List<string>();
        }

        public static async Task<List<string>> RegenerateBackupCodesAsync(int userId)
        {
            var settings = LoadSettings();
            var userTfa = settings.FirstOrDefault(s => s.UserId == userId);
            
            if (userTfa == null || !userTfa.IsEnabled)
                return new List<string>();

            userTfa.BackupCodes = GenerateBackupCodes();
            await SaveSettingsAsync(settings);
            _ = SyncToFirebaseAsync(userId, userTfa);
            
            return userTfa.BackupCodes;
        }

        private static List<TfaSettings> LoadSettings()
        {
            try
            {
                if (!File.Exists(TfaFile))
                    return new List<TfaSettings>();

                var json = File.ReadAllText(TfaFile);
                return JsonSerializer.Deserialize<List<TfaSettings>>(json) ?? new List<TfaSettings>();
            }
            catch
            {
                return new List<TfaSettings>();
            }
        }

        /// <summary>
        /// Pull 2FA settings from Firebase for a user and merge into local storage.
        /// Call this when the 2FA dialog opens to get the latest cross-PC state.
        /// </summary>
        public static async Task SyncFromFirebaseAsync(int userId)
        {
            try
            {
                var username = GetUsernameForId(userId);
                if (string.IsNullOrEmpty(username)) return;

                var response = await _httpClient.GetAsync($"{FirebaseUrl}{TfaPath}/{username}.json");
                if (!response.IsSuccessStatusCode) return;

                var json = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(json) || json == "null") return;

                var remote = JsonSerializer.Deserialize<FirebaseTfaData>(json);
                if (remote == null) return;

                var settings = LoadSettings();
                var local = settings.FirstOrDefault(s => s.UserId == userId);

                // Use the most recently updated version
                var remoteDate = DateTime.TryParse(remote.LastUpdated, out var rd) ? rd : DateTime.MinValue;
                var localDate = local?.SetupDate ?? DateTime.MinValue;

                if (remoteDate > localDate || local == null)
                {
                    if (local == null)
                    {
                        local = new TfaSettings { UserId = userId };
                        settings.Add(local);
                    }
                    local.IsEnabled = remote.IsEnabled;
                    local.SecretKey = remote.SecretKey;
                    local.BackupCodes = remote.BackupCodes ?? new List<string>();
                    local.SetupDate = remoteDate != DateTime.MinValue ? remoteDate : DateTime.Now;
                    await SaveSettingsAsync(settings);
                    Console.WriteLine($"[2FA] Synced from Firebase for {username}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[2FA] Firebase pull failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Push local 2FA settings to Firebase for cross-PC sync.
        /// </summary>
        private static async Task SyncToFirebaseAsync(int userId, TfaSettings entry)
        {
            try
            {
                var username = GetUsernameForId(userId);
                if (string.IsNullOrEmpty(username)) return;

                var data = new FirebaseTfaData
                {
                    IsEnabled = entry.IsEnabled,
                    SecretKey = entry.SecretKey,
                    BackupCodes = entry.BackupCodes,
                    LastUpdated = DateTime.UtcNow.ToString("o"),
                    SourcePc = Environment.MachineName
                };

                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync($"{FirebaseUrl}{TfaPath}/{username}.json", content);

                if (response.IsSuccessStatusCode)
                    Console.WriteLine($"[2FA] Synced to Firebase for {username}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[2FA] Firebase push failed: {ex.Message}");
            }
        }

        private static string? GetUsernameForId(int userId)
        {
            var user = AuthService.GetUserById(userId);
            return user?.Username;
        }

        private class FirebaseTfaData
        {
            public bool IsEnabled { get; set; }
            public string SecretKey { get; set; } = "";
            public List<string> BackupCodes { get; set; } = new();
            public string LastUpdated { get; set; } = "";
            public string SourcePc { get; set; } = "";
        }

        private static async Task SaveSettingsAsync(List<TfaSettings> settings)
        {
            try
            {
                var directory = Path.GetDirectoryName(TfaFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(TfaFile, json);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[2FA] Failed to save settings: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private static string GenerateSecretKey()
        {
            // Generate 80-bit (10 bytes) secret and encode as Base32 (A-Z2-7) for Google Authenticator compatibility
            var bytes = new byte[10];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Base32Encode(bytes);
        }

        private static List<string> GenerateBackupCodes()
        {
            var codes = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                var bytes = new byte[4];
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(bytes);
                codes.Add(Convert.ToHexString(bytes).Substring(0, 8));
            }
            return codes;
        }

        private static string GenerateTotpCode(string secretKey)
        {
            // RFC 6238 TOTP (SHA1, 30s, 6 digits) with Base32 secret
            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timeStep = unixTime / 30; // 30-second window

            var keyBytes = Base32Decode(secretKey);
            var timeBytes = BitConverter.GetBytes(timeStep);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(timeBytes);

            using var hmac = new HMACSHA1(keyBytes);
            var hash = hmac.ComputeHash(timeBytes);

            var offset = hash[hash.Length - 1] & 0x0F;
            var binaryCode = ((hash[offset] & 0x7F) << 24) |
                             ((hash[offset + 1] & 0xFF) << 16) |
                             ((hash[offset + 2] & 0xFF) << 8) |
                             (hash[offset + 3] & 0xFF);
            var code = binaryCode % 1_000_000;
            return code.ToString("D6");
        }

        private static readonly char[] Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

        private static string Base32Encode(byte[] data)
        {
            if (data.Length == 0) return string.Empty;
            int outputLength = (int)Math.Ceiling(data.Length / 5d) * 8;
            var result = new StringBuilder(outputLength);
            int bitBuffer = 0;
            int bitCount = 0;

            foreach (var b in data)
            {
                bitBuffer = (bitBuffer << 8) | b;
                bitCount += 8;
                while (bitCount >= 5)
                {
                    int index = (bitBuffer >> (bitCount - 5)) & 0x1F;
                    result.Append(Base32Alphabet[index]);
                    bitCount -= 5;
                }
            }
            if (bitCount > 0)
            {
                int index = (bitBuffer << (5 - bitCount)) & 0x1F;
                result.Append(Base32Alphabet[index]);
            }
            return result.ToString();
        }

        private static byte[] Base32Decode(string base32)
        {
            if (string.IsNullOrWhiteSpace(base32)) return Array.Empty<byte>();
            base32 = base32.Trim().Replace(" ", string.Empty).ToUpperInvariant();
            int byteCount = base32.Length * 5 / 8;
            var result = new List<byte>(byteCount);
            int bitBuffer = 0;
            int bitCount = 0;

            foreach (char c in base32)
            {
                int val = Array.IndexOf(Base32Alphabet, c);
                if (val < 0) continue;
                bitBuffer = (bitBuffer << 5) | val;
                bitCount += 5;
                if (bitCount >= 8)
                {
                    int b = (bitBuffer >> (bitCount - 8)) & 0xFF;
                    result.Add((byte)b);
                    bitCount -= 8;
                }
            }
            return result.ToArray();
        }
    }
}
