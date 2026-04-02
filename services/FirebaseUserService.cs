using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public class FirebaseUserService
    {
        private static readonly string FirebaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
        private static readonly string UsersPath = "users";
        
        private static bool _isInitialized = false;
        private static bool _initAttempted = false;
        private static readonly object _initLock = new object();
        private static HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        
        private static async Task<bool> EnsureInitializedAsync()
        {
            // Fast path - return immediately if already initialized or attempted
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
        /// Add or update a user in Firebase (sync across PCs)
        /// </summary>
        public static async Task<bool> SyncUserAsync(AppUser user)
        {
            if (!await EnsureInitializedAsync()) return false;
            
            try
            {
                // Don't sync password hash and salt for security
                var userData = new
                {
                    Id = user.Id,
                    Username = user.Username,
                    Role = user.Role,
                    Status = user.Status,
                    CreatedAt = user.CreatedAt.ToString("o"),
                    LastUpdated = DateTime.UtcNow.ToString("o"),
                    SourcePc = Environment.MachineName
                };
                
                var json = JsonSerializer.Serialize(userData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync(
                    $"{FirebaseUrl}{UsersPath}/{user.Username}.json", 
                    content);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FirebaseUser] Synced user: {user.Username}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseUser] Failed to sync user: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Remove a user from Firebase (when deleted locally)
        /// </summary>
        public static async Task<bool> RemoveUserAsync(string username)
        {
            if (!await EnsureInitializedAsync()) return false;
            
            try
            {
                var response = await _httpClient.DeleteAsync(
                    $"{FirebaseUrl}{UsersPath}/{username}.json");
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FirebaseUser] Removed user from Firebase: {username}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseUser] Failed to remove user: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Update user status in Firebase (disable/enable)
        /// </summary>
        public static async Task<bool> UpdateUserStatusAsync(string username, string status)
        {
            if (!await EnsureInitializedAsync()) return false;
            
            try
            {
                var updateData = new
                {
                    Status = status,
                    LastUpdated = DateTime.UtcNow.ToString("o"),
                    UpdatedBy = AuthService.CurrentUser?.Username ?? "System",
                    SourcePc = Environment.MachineName
                };
                
                var json = JsonSerializer.Serialize(updateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                // Use PATCH to update only specific fields
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), 
                    $"{FirebaseUrl}{UsersPath}/{username}.json")
                {
                    Content = content
                };
                
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FirebaseUser] Updated status for {username}: {status}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseUser] Failed to update status: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Get all users from Firebase (for cross-PC sync)
        /// </summary>
        public static async Task<List<AppUser>> GetAllUsersAsync()
        {
            var users = new List<AppUser>();
            
            if (!await EnsureInitializedAsync()) return users;
            
            try
            {
                var response = await _httpClient.GetAsync($"{FirebaseUrl}{UsersPath}.json");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(json) && json != "null")
                    {
                        var userDict = JsonSerializer.Deserialize<Dictionary<string, FirebaseUserData>>(json);
                        if (userDict != null)
                        {
                            foreach (var kvp in userDict)
                            {
                                var data = kvp.Value;
                                users.Add(new AppUser
                                {
                                    Id = data.Id,
                                    Username = data.Username,
                                    Role = data.Role,
                                    Status = data.Status,
                                    CreatedAt = DateTime.Parse(data.CreatedAt),
                                    // Password fields will be empty for remote users
                                    PasswordHash = string.Empty,
                                    Salt = string.Empty
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseUser] Failed to get users: {ex.Message}");
            }
            
            return users;
        }
        
        /// <summary>
        /// Listen for user changes from Firebase (polling approach for simplicity)
        /// </summary>
        public static async Task StartListeningForChangesAsync(Action<List<AppUser>> onUsersChanged)
        {
            while (true)
            {
                try
                {
                    var users = await GetAllUsersAsync();
                    onUsersChanged?.Invoke(users);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FirebaseUser] Listener error: {ex.Message}");
                }
                
                // Poll every 10 seconds
                await Task.Delay(10000);
            }
        }
        
        /// <summary>
        /// Sync local users to Firebase (for initial sync or periodic sync)
        /// </summary>
        public static async Task SyncLocalUsersToFirebaseAsync(List<AppUser> localUsers)
        {
            foreach (var user in localUsers)
            {
                await SyncUserAsync(user);
            }
        }
        
        private class FirebaseUserData
        {
            public int Id { get; set; }
            public string Username { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string CreatedAt { get; set; } = string.Empty;
            public string LastUpdated { get; set; } = string.Empty;
            public string SourcePc { get; set; } = string.Empty;
        }
    }
}
