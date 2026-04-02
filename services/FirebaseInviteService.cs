using Firebase.Database;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public class FirebaseInviteService
    {
        private static readonly string FirebaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
        private static readonly string InviteCodePath = "inviteCodes/current";
        
        private static FirebaseClient? _client;
        
        public static void Initialize()
        {
            try
            {
                _client = new FirebaseClient(FirebaseUrl);
                Console.WriteLine("[Firebase] Initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase] Initialization failed: {ex.Message}");
                _client = null;
            }
        }
        
        public static async Task<string?> GetInviteCodeAsync()
        {
            if (_client == null)
            {
                Console.WriteLine("[Firebase] Client not initialized");
                return null;
            }
            
            try
            {
                var result = await _client.Child(InviteCodePath).OnceSingleAsync<string>();
                Console.WriteLine($"[Firebase] Retrieved invite code: {result}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase] Error getting invite code: {ex.Message}");
                return null;
            }
        }
        
        public static async Task<bool> ValidateInviteCodeAsync(string code)
        {
            if (_client == null) return false;
            
            try
            {
                var storedCode = await GetInviteCodeAsync();
                return !string.IsNullOrEmpty(storedCode) && 
                       string.Equals(code, storedCode, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase] Error validating invite code: {ex.Message}");
                return false;
            }
        }
        
        public static async Task<bool> SetInviteCodeAsync(string code)
        {
            if (_client == null) return false;
            
            try
            {
                await _client.Child(InviteCodePath).PutAsync(code);
                Console.WriteLine($"[Firebase] Set invite code: {code}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase] Error setting invite code: {ex.Message}");
                return false;
            }
        }
        
        public static async Task<bool> IsAvailableAsync()
        {
            if (_client == null) return false;
            
            try
            {
                // Try a simple read operation to test connectivity
                await _client.Child(".info/connected").OnceSingleAsync<bool>();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
