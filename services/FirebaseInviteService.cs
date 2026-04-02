using System;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public class FirebaseInviteService
    {
        private static readonly string FirebaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
        private static readonly string InviteCodePath = "inviteCodes/current";
        
        private static bool _initialized = false;
        
        public static void Initialize()
        {
            // Don't initialize automatically - this prevents startup blocking
            Console.WriteLine("[Firebase] Initialize called - but using lazy initialization");
        }
        
        private static async Task<bool> EnsureInitializedAsync()
        {
            if (_initialized) return true;
            
            try
            {
                Console.WriteLine("[Firebase] Starting lazy initialization...");
                
                // Use HttpClient directly instead of FirebaseClient to avoid blocking
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                // Test connection with a simple GET request
                var response = await httpClient.GetAsync($"{FirebaseUrl}.json");
                if (response.IsSuccessStatusCode)
                {
                    _initialized = true;
                    Console.WriteLine("[Firebase] Lazy initialization successful");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase] Lazy initialization failed: {ex.Message}");
            }
            
            return false;
        }
        
        public static async Task<string?> GetInviteCodeAsync()
        {
            if (!await EnsureInitializedAsync()) return null;
            
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var response = await httpClient.GetAsync($"{FirebaseUrl}{InviteCodePath}.json");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Firebase] Retrieved invite code: {json}");
                    return json.Trim('"'); // Remove quotes from JSON string
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase] Error getting invite code: {ex.Message}");
            }
            
            return null;
        }
        
        public static async Task<bool> ValidateInviteCodeAsync(string code)
        {
            var storedCode = await GetInviteCodeAsync();
            return !string.IsNullOrEmpty(storedCode) && 
                   string.Equals(code, storedCode, StringComparison.OrdinalIgnoreCase);
        }
        
        public static async Task<bool> SetInviteCodeAsync(string code)
        {
            if (!await EnsureInitializedAsync()) return false;
            
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var json = $"\"{code}\""; // Wrap in quotes for JSON
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await httpClient.PutAsync($"{FirebaseUrl}{InviteCodePath}.json", content);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Firebase] Set invite code: {code}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase] Error setting invite code: {ex.Message}");
            }
            
            return false;
        }
        
        public static async Task<bool> IsAvailableAsync()
        {
            return await EnsureInitializedAsync();
        }
    }
}
