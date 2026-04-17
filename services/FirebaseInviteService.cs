using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public class FirebaseInviteService
    {
        private static readonly string FirebaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
        private static readonly string InviteCodesPath = "invite_codes";
        
        private static bool _isInitialized = false;
        private static bool _initAttempted = false;
        private static readonly object _initLock = new object();
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        
        public class InviteCodeData
        {
            public string code { get; set; } = "";
            public string created_at { get; set; } = "";
            public string created_by { get; set; } = "";
            public bool is_used { get; set; }
            public string? used_by { get; set; }
            public string? used_at { get; set; }
        }
        
        private static async Task<bool> EnsureInitializedAsync()
        {
            if (_isInitialized) return true;
            if (_initAttempted) return false;
            
            lock (_initLock)
            {
                if (_isInitialized || _initAttempted) return _isInitialized;
                _initAttempted = true;
            }
            
            try
            {
                var response = await _httpClient.GetAsync($"{FirebaseUrl}.json");
                _isInitialized = response.IsSuccessStatusCode;
                return _isInitialized;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Generate a new invite code (8-character alphanumeric)
        /// </summary>
        public static async Task<string> GenerateInviteCodeAsync(string createdBy = "admin")
        {
            if (!await EnsureInitializedAsync()) return string.Empty;
            
            try
            {
                // Generate 8-character alphanumeric code
                const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                var random = new Random();
                var code = new string(Enumerable.Repeat(chars, 8)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
                
                var data = new InviteCodeData
                {
                    code = code,
                    created_at = DateTime.UtcNow.ToString("o"),
                    created_by = createdBy,
                    is_used = false
                };
                
                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync(
                    $"{FirebaseUrl}{InviteCodesPath}/{code}.json", 
                    content);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FirebaseInvite] Generated invite code: {code}");
                    return code;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseInvite] Failed to generate invite code: {ex.Message}");
            }
            
            return string.Empty;
        }
        
        /// <summary>
        /// Validate an invite code (check if exists and is not used)
        /// </summary>
        public static async Task<bool> ValidateInviteCodeAsync(string code)
        {
            if (!await EnsureInitializedAsync()) return false;
            
            try
            {
                var response = await _httpClient.GetAsync($"{FirebaseUrl}{InviteCodesPath}/{code}.json");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(json) && json != "null")
                    {
                        var data = JsonSerializer.Deserialize<InviteCodeData>(json);
                        if (data != null)
                        {
                            return !data.is_used;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseInvite] Failed to validate invite code: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Mark an invite code as used
        /// </summary>
        public static async Task<bool> UseInviteCodeAsync(string code, string usedBy)
        {
            if (!await EnsureInitializedAsync()) return false;
            
            try
            {
                var updateData = new
                {
                    is_used = true,
                    used_by = usedBy,
                    used_at = DateTime.UtcNow.ToString("o")
                };
                
                var json = JsonSerializer.Serialize(updateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), 
                    $"{FirebaseUrl}{InviteCodesPath}/{code}.json")
                {
                    Content = content
                };
                
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FirebaseInvite] Marked invite code as used: {code} by {usedBy}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseInvite] Failed to mark invite code as used: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Get all invite codes
        /// </summary>
        public static async Task<List<InviteCodeData>> GetAllInviteCodesAsync()
        {
            var codes = new List<InviteCodeData>();
            
            if (!await EnsureInitializedAsync()) return codes;
            
            try
            {
                var response = await _httpClient.GetAsync($"{FirebaseUrl}{InviteCodesPath}.json");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(json) && json != "null")
                    {
                        var codeDict = JsonSerializer.Deserialize<Dictionary<string, InviteCodeData>>(json);
                        if (codeDict != null)
                        {
                            codes = codeDict.Values.ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseInvite] Failed to get invite codes: {ex.Message}");
            }
            
            return codes;
        }
        
        /// <summary>
        /// Delete an invite code
        /// </summary>
        public static async Task<bool> DeleteInviteCodeAsync(string code)
        {
            if (!await EnsureInitializedAsync()) return false;
            
            try
            {
                var response = await _httpClient.DeleteAsync($"{FirebaseUrl}{InviteCodesPath}/{code}.json");
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FirebaseInvite] Deleted invite code: {code}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseInvite] Failed to delete invite code: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Legacy method for backward compatibility - gets the current (most recent) unused invite code
        /// </summary>
        public static async Task<string?> GetInviteCodeAsync()
        {
            var codes = await GetAllInviteCodesAsync();
            var unusedCode = codes.FirstOrDefault(c => !c.is_used);
            return unusedCode?.code;
        }
        
        public static async Task<bool> IsAvailableAsync()
        {
            return await EnsureInitializedAsync();
        }
        
        /// <summary>
        /// Delete all old-format invite codes (those starting with CODE-)
        /// </summary>
        public static async Task<int> DeleteOldFormatCodesAsync()
        {
            if (!await EnsureInitializedAsync()) return 0;
            
            int deletedCount = 0;
            
            try
            {
                var response = await _httpClient.GetAsync($"{FirebaseUrl}{InviteCodesPath}.json");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(json) && json != "null")
                    {
                        var codeDict = JsonSerializer.Deserialize<Dictionary<string, InviteCodeData>>(json);
                        if (codeDict != null)
                        {
                            foreach (var code in codeDict.Keys.Where(k => k.StartsWith("CODE-")))
                            {
                                var deleteResponse = await _httpClient.DeleteAsync($"{FirebaseUrl}{InviteCodesPath}/{code}.json");
                                if (deleteResponse.IsSuccessStatusCode)
                                {
                                    deletedCount++;
                                    Console.WriteLine($"[FirebaseInvite] Deleted old-format code: {code}");
                                }
                            }
                        }
                    }
                }
                
                Console.WriteLine($"[FirebaseInvite] Deleted {deletedCount} old-format invite codes");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseInvite] Failed to delete old-format codes: {ex.Message}");
            }
            
            return deletedCount;
        }
    }
}
