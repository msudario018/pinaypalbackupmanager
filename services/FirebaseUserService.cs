using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;
using Microsoft.Data.Sqlite;

namespace PinayPalBackupManager.Services
{
    public class FirebaseUserService
    {
        private static readonly string FirebaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
        private static readonly string UsersPath = "users";
        private static string _connectionString = string.Empty;

        public static string ConnectionString
        {
            set => _connectionString = value;
        }
        
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
                // Include password hash and salt for Flutter app authentication
                var userData = new
                {
                    Id = user.Id,
                    Username = user.Username,
                    PasswordHash = user.PasswordHash,
                    Salt = user.Salt,
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
        /// Change user password in Firebase (admin can change without current password)
        /// </summary>
        public static async Task<bool> ChangeUserPasswordAsync(string username, string newPasswordHash, string newSalt)
        {
            if (!await EnsureInitializedAsync()) return false;
            
            try
            {
                var updateData = new
                {
                    PasswordHash = newPasswordHash,
                    Salt = newSalt,
                    LastUpdated = DateTime.UtcNow.ToString("o"),
                    UpdatedBy = AuthService.CurrentUser?.Username ?? "System",
                    SourcePc = Environment.MachineName
                };
                
                var json = JsonSerializer.Serialize(updateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), 
                    $"{FirebaseUrl}{UsersPath}/{username}.json")
                {
                    Content = content
                };
                
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FirebaseUser] Changed password for {username}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseUser] Failed to change password: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Change username in Firebase (delete old, create new)
        /// </summary>
        public static async Task<bool> ChangeUsernameAsync(string oldUsername, string newUsername, AppUser user)
        {
            if (!await EnsureInitializedAsync()) return false;
            
            try
            {
                // Delete old username entry
                await RemoveUserAsync(oldUsername);
                
                // Create new entry with updated username
                var userData = new
                {
                    Id = user.Id,
                    Username = newUsername,
                    PasswordHash = user.PasswordHash,
                    Salt = user.Salt,
                    Role = user.Role,
                    Status = user.Status,
                    CreatedAt = user.CreatedAt.ToString("o"),
                    LastUpdated = DateTime.UtcNow.ToString("o"),
                    SourcePc = Environment.MachineName
                };
                
                var json = JsonSerializer.Serialize(userData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync(
                    $"{FirebaseUrl}{UsersPath}/{newUsername}.json", 
                    content);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FirebaseUser] Changed username from {oldUsername} to {newUsername}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseUser] Failed to change username: {ex.Message}");
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
                                DateTime createdAt = DateTime.UtcNow; // Default to current time if parsing fails
                                try
                                {
                                    if (!string.IsNullOrEmpty(data.CreatedAt))
                                        createdAt = DateTime.Parse(data.CreatedAt);
                                }
                                catch
                                {
                                    Console.WriteLine($"[FirebaseUser] Invalid CreatedAt for user {data.Username}: '{data.CreatedAt}', using current time");
                                }
                                
                                users.Add(new AppUser
                                {
                                    Id = data.Id,
                                    Username = data.Username,
                                    Role = data.Role,
                                    Status = data.Status,
                                    CreatedAt = createdAt,
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
        /// Listen for specific user status changes from Firebase (for real-time approval updates)
        /// </summary>
        public static async Task StartListeningForUserStatusAsync(string username, Action<string> onStatusChanged)
        {
            if (!await EnsureInitializedAsync()) return;
            
            string lastStatus = string.Empty;
            
            while (true)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{FirebaseUrl}{UsersPath}/{username}.json");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrEmpty(json) && json != "null")
                        {
                            var userData = JsonSerializer.Deserialize<FirebaseUserData>(json);
                            if (userData != null && userData.Status != lastStatus)
                            {
                                lastStatus = userData.Status;
                                onStatusChanged?.Invoke(userData.Status);
                                Console.WriteLine($"[FirebaseUser] Status change detected for {username}: {userData.Status}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FirebaseUser] User listener error: {ex.Message}");
                }
                
                // Poll every 3 seconds for faster status updates
                await Task.Delay(3000);
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

        /// <summary>
        /// Create a user directly in Firebase (for Flutter app authentication)
        /// </summary>
        public static async Task<bool> CreateUserInFirebaseAsync(string username, string passwordHash, string salt, string role = "User", string status = "Active")
        {
            if (!await EnsureInitializedAsync()) return false;

            try
            {
                // Get max ID from existing users
                var allUsers = await GetAllUsersAsync();
                int maxId = 0;
                foreach (var user in allUsers)
                {
                    if (user.Id > maxId) maxId = user.Id;
                }
                int newId = maxId + 1;

                var userData = new
                {
                    Id = newId,
                    Username = username,
                    PasswordHash = passwordHash, // Include password for Flutter app auth
                    Salt = salt, // Include salt for Flutter app auth
                    Role = role,
                    Status = status,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    LastUpdated = DateTime.UtcNow.ToString("o"),
                    SourcePc = "FlutterApp"
                };

                var json = JsonSerializer.Serialize(userData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync(
                    $"{FirebaseUrl}{UsersPath}/{username}.json",
                    content);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FirebaseUser] Created user in Firebase: {username}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseUser] Failed to create user in Firebase: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Listen for all user changes from Firebase and sync to local database
        /// </summary>
        public static async Task StartUserSyncListenerAsync(Action<string, string>? onUserChanged = null)
        {
            if (!await EnsureInitializedAsync()) return;

            Dictionary<string, FirebaseUserData> lastKnownUsers = new();

            while (true)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{FirebaseUrl}{UsersPath}.json");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrEmpty(json) && json != "null")
                        {
                            var currentUsers = JsonSerializer.Deserialize<Dictionary<string, FirebaseUserData>>(json);
                            if (currentUsers != null)
                            {
                                // Detect new or updated users
                                foreach (var kvp in currentUsers)
                                {
                                    var username = kvp.Key;
                                    var userData = kvp.Value;

                                    // Check if user is new or updated
                                    if (!lastKnownUsers.ContainsKey(username) || 
                                        lastKnownUsers[username].LastUpdated != userData.LastUpdated)
                                    {
                                        // Conflict resolution: use LastUpdated timestamp
                                        // Firebase is considered the source of truth for role/status changes
                                        _ = Task.Run(() => SyncUserToLocalAsync(userData));
                                        onUserChanged?.Invoke(username, userData.Status);
                                        Console.WriteLine($"[FirebaseUser] Synced user from Firebase: {username}");
                                    }
                                }

                                // Detect deleted users
                                foreach (var username in lastKnownUsers.Keys)
                                {
                                    if (!currentUsers.ContainsKey(username))
                                    {
                                        // Remove from local database
                                        _ = Task.Run(() => RemoveUserFromLocalAsync(username));
                                        onUserChanged?.Invoke(username, "Deleted");
                                        Console.WriteLine($"[FirebaseUser] Removed user from local DB: {username}");
                                    }
                                }

                                lastKnownUsers = currentUsers;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FirebaseUser] Sync listener error: {ex.Message}");
                }

                // Poll every 5 seconds
                await Task.Delay(5000);
            }
        }

        /// <summary>
        /// Sync a Firebase user to local database
        /// </summary>
        private static async Task SyncUserToLocalAsync(FirebaseUserData firebaseUser)
        {
            try
            {
                var existingUser = AuthService.GetUserByUsername(firebaseUser.Username);
                
                if (existingUser != null)
                {
                    // Update existing user (only sync non-sensitive fields)
                    using var conn = new SqliteConnection(_connectionString);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        UPDATE Users 
                        SET Role = @r, Status = @s 
                        WHERE Username = @u";
                    cmd.Parameters.AddWithValue("@r", firebaseUser.Role);
                    cmd.Parameters.AddWithValue("@s", firebaseUser.Status);
                    cmd.Parameters.AddWithValue("@u", firebaseUser.Username);
                    cmd.ExecuteNonQuery();

                    Console.WriteLine($"[FirebaseUser] Updated local user: {firebaseUser.Username}");
                }
                else
                {
                    // User doesn't exist locally - this is expected for remote users
                    // We don't create local entries for remote users without passwords
                    Console.WriteLine($"[FirebaseUser] Skipping remote user without password: {firebaseUser.Username}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseUser] Failed to sync user to local DB: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove a user from local database
        /// </summary>
        private static void RemoveUserFromLocalAsync(string username)
        {
            try
            {
                var user = AuthService.GetUserByUsername(username);
                if (user != null)
                {
                    AuthService.DeleteUser(user.Id);
                    Console.WriteLine($"[FirebaseUser] Removed user from local DB: {username}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseUser] Failed to remove user from local DB: {ex.Message}");
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
