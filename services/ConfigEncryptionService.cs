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
        private static readonly byte[] Salt = new byte[] { 
            0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 
            0x76, 0x65, 0x64, 0x65, 0x76, 0x49, 0x76, 0x61 
        };

        /// <summary>
        /// Derives encryption key from machine-specific information
        /// </summary>
        private static byte[] DeriveKeyFromMachine()
        {
            var machineData = Environment.MachineName + 
                             Environment.UserName + 
                             Environment.OSVersion.ToString();
            
            using var rfc2898 = new Rfc2898DeriveBytes(
                machineData, 
                Salt, 
                10000, 
                HashAlgorithmName.SHA256);
            
            return rfc2898.GetBytes(32); // 256-bit key
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
                return plainText; // Fallback to plain text on error
            }
        }

        /// <summary>
        /// Decrypts encrypted text using AES-256
        /// </summary>
        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedText);
                
                using var aes = Aes.Create();
                aes.Key = DeriveKeyFromMachine();
                
                // Extract IV from the beginning of the encrypted data
                var iv = new byte[aes.IV.Length];
                Array.Copy(encryptedBytes, 0, iv, 0, iv.Length);
                aes.IV = iv;
                
                using var decryptor = aes.CreateDecryptor();
                using var msDecrypt = new MemoryStream(encryptedBytes, iv.Length, encryptedBytes.Length - iv.Length);
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var srDecrypt = new StreamReader(csDecrypt);
                
                return srDecrypt.ReadToEnd();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigEncryption] Decryption failed: {ex.Message}");
                return encryptedText; // Return original if decryption fails
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
