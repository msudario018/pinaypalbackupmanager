using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Provides encryption and decryption services for backup files
    /// Uses AES-256 encryption with user-provided password
    /// </summary>
    public static class BackupEncryptionService
    {
        private const int KeySize = 256;
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const int Iterations = 10000;

        /// <summary>
        /// Encrypts a file using AES-256 with password-derived key
        /// </summary>
        public static async Task<bool> EncryptFileAsync(string sourceFile, string encryptedFile, string password, CancellationToken cancellationToken = default)
        {
            try
            {
                // Generate salt and derive key
                var salt = new byte[SaltSize];
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(salt);

                using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
                var key = pbkdf2.GetBytes(KeySize / 8);

                using var aes = Aes.Create();
                aes.Key = key;
                aes.GenerateIV();

                using var inputStream = File.OpenRead(sourceFile);
                using var outputStream = File.Create(encryptedFile);

                // Write salt and IV to the beginning of the encrypted file
                await outputStream.WriteAsync(salt, 0, salt.Length, cancellationToken);
                await outputStream.WriteAsync(aes.IV, 0, aes.IV.Length, cancellationToken);

                using var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
                await inputStream.CopyToAsync(cryptoStream, cancellationToken);

                Console.WriteLine($"[BackupEncryption] Successfully encrypted: {Path.GetFileName(sourceFile)}");
                return true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[BackupEncryption] Encryption cancelled for: {Path.GetFileName(sourceFile)}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupEncryption] Failed to encrypt {Path.GetFileName(sourceFile)}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Decrypts a file using AES-256 with password-derived key
        /// </summary>
        public static async Task<bool> DecryptFileAsync(string encryptedFile, string decryptedFile, string password, CancellationToken cancellationToken = default)
        {
            try
            {
                using var inputStream = File.OpenRead(encryptedFile);

                // Read salt and IV from the beginning of the encrypted file
                var salt = new byte[SaltSize];
                var iv = new byte[IvSize];
                
                await inputStream.ReadAsync(salt, 0, salt.Length, cancellationToken);
                await inputStream.ReadAsync(iv, 0, iv.Length, cancellationToken);

                // Derive key from password and salt
                using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
                var key = pbkdf2.GetBytes(KeySize / 8);

                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;

                using var outputStream = File.Create(decryptedFile);
                using var cryptoStream = new CryptoStream(inputStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
                
                await cryptoStream.CopyToAsync(outputStream, cancellationToken);

                Console.WriteLine($"[BackupEncryption] Successfully decrypted: {Path.GetFileName(encryptedFile)}");
                return true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[BackupEncryption] Decryption cancelled for: {Path.GetFileName(encryptedFile)}");
                return false;
            }
            catch (CryptographicException)
            {
                Console.WriteLine($"[BackupEncryption] Invalid password or corrupted file: {Path.GetFileName(encryptedFile)}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupEncryption] Failed to decrypt {Path.GetFileName(encryptedFile)}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a file is encrypted (has salt + IV header)
        /// </summary>
        public static bool IsFileEncrypted(string filePath)
        {
            try
            {
                using var inputStream = File.OpenRead(filePath);
                
                // Check if file has enough bytes for salt + IV
                if (inputStream.Length < SaltSize + IvSize)
                    return false;

                var header = new byte[SaltSize + IvSize];
                inputStream.Read(header, 0, header.Length);

                // Simple heuristic: check if header looks like random data (encrypted)
                // This is not foolproof but provides a reasonable indication
                var entropy = CalculateEntropy(header);
                return entropy > 7.0; // High entropy indicates encrypted data
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Calculates the Shannon entropy of byte data
        /// </summary>
        private static double CalculateEntropy(byte[] data)
        {
            if (data.Length == 0) return 0;

            var frequencies = new int[256];
            foreach (var b in data)
                frequencies[b]++;

            double entropy = 0;
            for (int i = 0; i < 256; i++)
            {
                if (frequencies[i] == 0) continue;

                double probability = (double)frequencies[i] / data.Length;
                entropy -= probability * Math.Log2(probability);
            }

            return entropy;
        }

        
        /// <summary>
        /// Encrypts a string using AES-256
        /// </summary>
        private static string EncryptString(string plainText, string password)
        {
            var salt = new byte[SaltSize];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(salt);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(KeySize / 8);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var msEncrypt = new MemoryStream();
            
            msEncrypt.Write(salt, 0, salt.Length);
            msEncrypt.Write(aes.IV, 0, aes.IV.Length);
            
            using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
            using var swEncrypt = new StreamWriter(csEncrypt);
            swEncrypt.Write(plainText);
            swEncrypt.Flush();
            csEncrypt.FlushFinalBlock();
            
            return Convert.ToBase64String(msEncrypt.ToArray());
        }

        /// <summary>
        /// Decrypts a string using AES-256
        /// </summary>
        private static string DecryptString(string encryptedText, string password)
        {
            var encryptedBytes = Convert.FromBase64String(encryptedText);
            
            using var msDecrypt = new MemoryStream(encryptedBytes);
            
            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            
            msDecrypt.Read(salt, 0, salt.Length);
            msDecrypt.Read(iv, 0, iv.Length);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(KeySize / 8);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);
            
            return srDecrypt.ReadToEnd();
        }

        /// <summary>
        /// Validates if the provided password can decrypt the file
        /// </summary>
        public static async Task<bool> ValidatePasswordAsync(string encryptedFile, string password, CancellationToken cancellationToken = default)
        {
            try
            {
                var tempFile = Path.GetTempFileName();
                var result = await DecryptFileAsync(encryptedFile, tempFile, password, cancellationToken);
                
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                
                return result;
            }
            catch
            {
                return false;
            }
        }
    }
}
