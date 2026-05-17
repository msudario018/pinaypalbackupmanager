using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Provides encryption and decryption services for sensitive configuration data
    /// Uses AES-256 encryption with machine-specific key derivation
    /// </summary>
    public static class ConfigEncryptionService
    {
        // Legacy hardcoded salt for backward-compatible decryption
        private static readonly byte[] LegacySalt = new byte[] { 
            0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 
            0x76, 0x65, 0x64, 0x65, 0x76, 0x49, 0x76, 0x61 
        };

        private static readonly string SaltFilePath = AppDataPaths.GetPath("config_salt.bin");
        private static byte[]? _cachedSalt;
        private static byte[]? _cachedKey;

        private static byte[] GetOrCreateSalt()
        {
            if (_cachedSalt != null) return _cachedSalt;

            try
            {
                if (File.Exists(SaltFilePath))
                {
                    _cachedSalt = File.ReadAllBytes(SaltFilePath);
                    return _cachedSalt;
                }
            }
            catch { /* Ignore read errors, generate new salt */ }

            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            try
            {
                File.WriteAllBytes(SaltFilePath, salt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigEncryption] Failed to persist salt: {ex.Message}");
            }

            _cachedSalt = salt;
            return salt;
        }

        /// <summary>
        /// Derives encryption key from machine-specific information
        /// </summary>
        private static byte[] DeriveKeyFromMachine()
        {
            if (_cachedKey != null) return _cachedKey;

            var machineData = Environment.MachineName + 
                             Environment.UserName + 
                             Environment.OSVersion.ToString();
            
            using var rfc2898 = new Rfc2898DeriveBytes(
                machineData, 
                GetOrCreateSalt(), 
                10000, 
                HashAlgorithmName.SHA256);
            
            _cachedKey = rfc2898.GetBytes(32); // 256-bit key
            return _cachedKey;
        }

        /// <summary>
        /// Encrypts plain text using AES-256
        /// </summary>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                using var aes = Aes.Create();
                aes.Key = DeriveKeyFromMachine();
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                using var msEncrypt = new MemoryStream();
                
                // Write IV to the beginning of the stream
                msEncrypt.Write(aes.IV, 0, aes.IV.Length);
                
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
                using var swEncrypt = new StreamWriter(csEncrypt);
                swEncrypt.Write(plainText);
                swEncrypt.Flush();
                csEncrypt.FlushFinalBlock();
                
                var encrypted = msEncrypt.ToArray();
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigEncryption] Encryption failed: {ex.Message}");
                throw new InvalidOperationException("Failed to encrypt sensitive configuration value.", ex);
            }
        }

        /// <summary>
        /// Decrypts encrypted text using AES-256
        /// </summary>
        private static string DecryptWithSalt(string encryptedText, byte[] salt)
        {
            var encryptedBytes = Convert.FromBase64String(encryptedText);

            var machineData = Environment.MachineName +
                             Environment.UserName +
                             Environment.OSVersion.ToString();

            using var rfc2898 = new Rfc2898DeriveBytes(machineData, salt, 10000, HashAlgorithmName.SHA256);
            var key = rfc2898.GetBytes(32);

            using var aes = Aes.Create();
            aes.Key = key;

            var iv = new byte[aes.IV.Length];
            Array.Copy(encryptedBytes, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var msDecrypt = new MemoryStream(encryptedBytes, iv.Length, encryptedBytes.Length - iv.Length);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);

            return srDecrypt.ReadToEnd();
        }

        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            try
            {
                // Try per-install random salt first
                var result = DecryptWithSalt(encryptedText, GetOrCreateSalt());
                Console.WriteLine("[ConfigEncryption] Decrypted with per-install random salt.");
                return result;
            }
            catch (Exception ex1)
            {
                try
                {
                    // Fallback to legacy hardcoded salt for backward compatibility
                    var result = DecryptWithSalt(encryptedText, LegacySalt);
                    Console.WriteLine("[ConfigEncryption] Decrypted with legacy hardcoded salt.");
                    return result;
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[ConfigEncryption] Decryption failed with both salts. New salt error: {ex1.Message}. Legacy salt error: {ex2.Message}");
                    throw new CryptographicException("Failed to decrypt configuration value. The encryption salt may have changed or the value is corrupted.", ex2);
                }
            }
        }

        public static (bool success, string decrypted) TryDecrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return (true, string.Empty);

            try
            {
                var result = Decrypt(encryptedText);
                return (true, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigEncryption] TryDecrypt failed: {ex.Message}");
                return (false, encryptedText);
            }
        }

        
        /// <summary>
        /// Decrypts sensitive configuration values
        /// </summary>
        public static string DecryptSensitiveValue(string encryptedValue)
        {
            return Decrypt(encryptedValue);
        }

        /// <summary>
        /// Checks if a value appears to be encrypted (Base64 format)
        /// </summary>
        public static bool IsEncrypted(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            try
            {
                // Simple heuristic: encrypted values are Base64 strings longer than 20 chars
                if (value.Length <= 20) return false;
                
                // Try to parse as Base64
                var buffer = new Span<byte>(new byte[value.Length]);
                return Convert.TryFromBase64String(value, buffer, out _);
            }
            catch
            {
                return false;
            }
        }
    }
}
