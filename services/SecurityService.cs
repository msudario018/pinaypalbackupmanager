using System;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public static partial class SecurityService
    {
        public static string GetDecryptedFtpPassword()
        {
            return DecryptPowerShellString(BackupConfig.FtpEncryptedPass, BackupConfig.FtpSecretKey, BackupConfig.FtpLogFile);
        }

        public static string GetDecryptedSqlPassword()
        {
            return DecryptPowerShellString(BackupConfig.SqlEncryptedPass, BackupConfig.FtpSecretKey, BackupConfig.SqlLogFile);
        }

        private static string DecryptPowerShellString(string encryptedStr, byte[] key, string logFile)
        {
            try
            {
                if (string.IsNullOrEmpty(encryptedStr) || encryptedStr.Length < 32)
                    return string.Empty;

                LogService.WriteLiveLog("DECRYPTION: Starting secure recovery process...", logFile, "Information", "SYSTEM");

                // 1. Extract IV (First 32 hex chars)
                string ivHex = encryptedStr[..32];
                string base64Part = encryptedStr[32..];
                
                byte[] iv = new byte[16];
                for (int i = 0; i < 16; i++)
                    iv[i] = Convert.ToByte(ivHex.Substring(i * 2, 2), 16);

                LogService.WriteLiveLog($"DECRYPTION: Extracted IV from prefix. Payload length: {base64Part.Length}", logFile, "Information", "SYSTEM");

                // 2. Decode the Base64 part
                byte[] decodedBytes = Convert.FromBase64String(base64Part);
                
                // Decode as Unicode (standard PowerShell SecureString format)
                string decodedStr = Encoding.Unicode.GetString(decodedBytes);

                // If it doesn't start with "2|", it might be UTF8 (less common but possible)
                if (!decodedStr.StartsWith("2|"))
                {
                    string utf8Str = Encoding.UTF8.GetString(decodedBytes);
                    if (utf8Str.StartsWith("2|"))
                    {
                        decodedStr = utf8Str;
                    }
                }

                if (decodedStr.StartsWith("2|"))
                {
                    string[] parts = decodedStr.Split('|');
                    if (parts.Length >= 3)
                    {
                        string ivPart = parts[1];
                        string payload = parts[2];
                        byte[] encryptedPayload;
                        byte[] internalIv;

                        LogService.WriteLiveLog("DECRYPTION: Detected internal SecureString structure.", logFile, "Information", "SYSTEM");

                        // Try to get IV from parts[1] (Base64)
                        try 
                        {
                            internalIv = Convert.FromBase64String(ivPart);
                            if (internalIv.Length == 16)
                            {
                                iv = internalIv;
                                LogService.WriteLiveLog("DECRYPTION: Using internal IV from SecureString structure.", logFile, "Information", "SYSTEM");
                            }
                        }
                        catch { /* Fallback to prefix IV */ }

                        // Check if payload is hex or base64
                        if (MyRegex().IsMatch(payload))
                        {
                            LogService.WriteLiveLog("DECRYPTION: Payload is HEX encoded.", logFile, "Information", "SYSTEM");
                            encryptedPayload = new byte[payload.Length / 2];
                            for (int i = 0; i < encryptedPayload.Length; i++)
                                encryptedPayload[i] = Convert.ToByte(payload.Substring(i * 2, 2), 16);
                        }
                        else
                        {
                            LogService.WriteLiveLog("DECRYPTION: Payload is Base64 encoded.", logFile, "Information", "SYSTEM");
                            encryptedPayload = Convert.FromBase64String(payload);
                        }

                        // 3. Decrypt the payload using AES
                        LogService.WriteLiveLog($"DECRYPTION: Applying AES-CBC (Key: {key.Length} bytes, IV: {iv.Length} bytes)...", logFile, "Information", "SYSTEM");
                        using Aes aes = Aes.Create();
                        aes.Key = key;
                        aes.IV = iv;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;

                        using ICryptoTransform decryptor = aes.CreateDecryptor();
                        byte[] decryptedBytes = decryptor.TransformFinalBlock(encryptedPayload, 0, encryptedPayload.Length);
                        
                        // Standard PowerShell strings are Unicode
                        string result = Encoding.Unicode.GetString(decryptedBytes).TrimEnd('\0');
                        
                        if (string.IsNullOrEmpty(result))
                        {
                            LogService.WriteLiveLog("DECRYPTION WARNING: Result is empty after recovery.", logFile, "Warning", "SYSTEM");
                        }
                        else
                        {
                            LogService.WriteLiveLog($"DECRYPTION SUCCESS: Recovered {result.Length} characters.", logFile, "Information", "SYSTEM");
                        }
                        return result;
                    }
                }
                
                LogService.WriteLiveLog("DECRYPTION FAILED: Structure does not match expected format.", logFile, "Error", "SYSTEM");
                return string.Empty;
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"DECRYPTION ERROR: {ex.Message}", logFile, "Error", "SYSTEM");
                return string.Empty;
            }
        }

        [System.Text.RegularExpressions.GeneratedRegex("^[a-fA-F0-9]+$")]
        private static partial System.Text.RegularExpressions.Regex MyRegex();
    }
}
