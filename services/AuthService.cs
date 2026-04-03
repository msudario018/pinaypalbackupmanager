using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public static class AuthService
    {
        private static string _dbPath = string.Empty;
        public static AppUser? CurrentUser { get; private set; }
        public static event Action<AppUser?>? OnUserChanged;

        public static void Initialize()
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager");
            Directory.CreateDirectory(appDataDir);
            _dbPath = Path.Combine(appDataDir, "users.db");
            EnsureDatabase();
            
            // Firebase will be initialized on-demand to avoid blocking
            Console.WriteLine("[AuthService] Firebase ready for on-demand initialization");
        }

        private static string ConnectionString => $"Data Source={_dbPath}";

        private static void EnsureDatabase()
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    PasswordHash TEXT NOT NULL,
                    Salt TEXT NOT NULL,
                    Role TEXT NOT NULL DEFAULT 'User',
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    CreatedAt TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS AppConfig (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }

        public static bool HasAnyUsers()
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        /// <summary>
        /// Register the very first user as Admin (auto-active). Subsequent users need a valid invite code.
        /// </summary>
        public static (bool success, string message) Register(string username, string password, string? inviteCode = null)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, "Username and password are required.");

            if (password.Length < 4)
                return (false, "Password must be at least 4 characters.");

            bool isFirstUser = !HasAnyUsers();

            if (!isFirstUser)
            {
                if (string.IsNullOrWhiteSpace(inviteCode))
                    return (false, "Invite code is required.");

                // Local validation only (avoid UI thread blocking)
                var storedCode = GetInviteCode();
                bool isValid = string.Equals(inviteCode.Trim(), storedCode, StringComparison.Ordinal);
                
                if (!isValid)
                    return (false, "Invalid invite code.");
            }

            var salt = GenerateSalt();
            var hash = HashPassword(password, salt);

            try
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO Users (Username, PasswordHash, Salt, Role, Status, CreatedAt)
                                    VALUES (@u, @h, @s, @r, @st, @c)";
                cmd.Parameters.AddWithValue("@u", username.Trim());
                cmd.Parameters.AddWithValue("@h", hash);
                cmd.Parameters.AddWithValue("@s", salt);
                cmd.Parameters.AddWithValue("@r", isFirstUser ? "Admin" : "User");
                cmd.Parameters.AddWithValue("@st", isFirstUser ? "Active" : "Pending");
                cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();

                if (isFirstUser)
                {
                    RotateInviteCode();
                }

                // Sync new user to Firebase (fire-and-forget, don't block UI)
                var newUser = GetUserByUsername(username.Trim());
                if (newUser != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await FirebaseUserService.SyncUserAsync(newUser);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[AuthService] Failed to sync new user to Firebase: {ex.Message}");
                        }
                    });
                }

                return (true, isFirstUser ? "Admin account created." : "Registration successful! Your account is pending admin approval.");
            }
            catch (SqliteException ex)
            {
                if (ex.SqliteErrorCode == 19)
                    return (false, "Username already exists.");
                return (false, $"Database error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"An unexpected error occurred: {ex.Message}");
            }
        }

        public static (bool success, string message) Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, "Username and password are required.");

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users WHERE Username = @u";
            cmd.Parameters.AddWithValue("@u", username.Trim());

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return (false, "Invalid username or password.");

            var user = ReadUser(reader);

            if (user.Status == "Disabled")
                return (false, "Account is disabled. Contact the admin.");

            if (user.Status == "Pending")
                return (false, "Account is pending approval.");

            var hash = HashPassword(password, user.Salt);
            if (!string.Equals(hash, user.PasswordHash, StringComparison.Ordinal))
                return (false, "Invalid username or password.");

            CurrentUser = user;
            OnUserChanged?.Invoke(user);
            return (true, $"Welcome, {user.Username}!");
        }

        public static void Logout()
        {
            CurrentUser = null;
            OnUserChanged?.Invoke(null);
        }

        public static bool IsAdmin => CurrentUser?.Role == "Admin";

        // ── Invite Code ──

        public static string GetInviteCode()
        {
            // Try to get from Firebase first (with timeout to avoid blocking)
            string? firebaseCode = null;
            try
            {
                var task = Task.Run(async () => await FirebaseInviteService.GetInviteCodeAsync());
                if (task.Wait(TimeSpan.FromSeconds(2)))
                {
                    firebaseCode = task.Result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Failed to get Firebase invite code: {ex.Message}");
            }
            
            // Use Firebase code if available, otherwise fallback to local
            var effectiveCode = !string.IsNullOrEmpty(firebaseCode) ? firebaseCode : GetInviteCodeFromConfig();
            var hardcodedCode = "PINAYPAL2024";
            effectiveCode = !string.IsNullOrEmpty(effectiveCode) ? effectiveCode : hardcodedCode;
            
            // Update local database with the effective code
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppConfig WHERE Key = 'InviteCode'";
            var result = cmd.ExecuteScalar();
            
            if (result == null)
            {
                cmd.CommandText = @"INSERT INTO AppConfig (Key, Value) VALUES ('InviteCode', @v)";
                cmd.Parameters.AddWithValue("@v", effectiveCode);
                cmd.ExecuteNonQuery();
                return effectiveCode;
            }
            
            var storedCode = result.ToString() ?? string.Empty;
            
            // If Firebase has a different code, update local to match
            if (!string.IsNullOrEmpty(firebaseCode) && !string.Equals(storedCode, firebaseCode, StringComparison.Ordinal))
            {
                cmd.CommandText = @"UPDATE AppConfig SET Value = @v WHERE Key = 'InviteCode'";
                cmd.Parameters.AddWithValue("@v", firebaseCode);
                cmd.ExecuteNonQuery();
                Console.WriteLine($"[AuthService] Updated local invite code from Firebase: {firebaseCode}");
                return firebaseCode;
            }
            
            return storedCode;
        }

        private static string GetInviteCodeFromConfig()
        {
            try
            {
                var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager");
                var configPath = Path.Combine(appDataDir, "invite.txt");
                
                if (File.Exists(configPath))
                {
                    var code = File.ReadAllText(configPath).Trim();
                    return !string.IsNullOrEmpty(code) ? code : string.Empty;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Error reading invite code from config: {ex.Message}");
            }
            
            return string.Empty;
        }

        public static string RotateInviteCode()
        {
            var newCode = GenerateInviteCode();
            
            // Update local database
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE AppConfig SET Value = @v WHERE Key = 'InviteCode'";
            cmd.Parameters.AddWithValue("@v", newCode);
            cmd.ExecuteNonQuery();
            
            // Sync to Firebase (fire-and-forget, don't block UI)
            _ = Task.Run(async () =>
            {
                try
                {
                    await FirebaseInviteService.SetInviteCodeAsync(newCode);
                    Console.WriteLine($"[AuthService] Firebase invite code updated: {newCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AuthService] Failed to sync invite code to Firebase: {ex.Message}");
                }
            });
            
            Console.WriteLine($"[AuthService] Invite code rotated: {newCode}");
            return newCode;
        }

        public static AppUser? GetUserByUsername(string username)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users WHERE Username = @u COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@u", username);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return ReadUser(reader);
            return null;
        }

        // ── User Management (Admin) ──

        public static List<AppUser> GetAllUsers()
        {
            var users = new List<AppUser>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users ORDER BY CreatedAt";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                users.Add(ReadUser(reader));
            }
            return users;
        }

        public static async Task<bool> SetUserStatusAsync(int userId, string status)
        {
            // Get username first for Firebase sync
            var user = GetUserById(userId);
            
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET Status = @s WHERE Id = @id AND Role != 'Admin'";
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@id", userId);
            var result = cmd.ExecuteNonQuery() > 0;
            
            // Sync status change to Firebase and wait for completion
            if (result && user != null)
            {
                try
                {
                    await FirebaseUserService.UpdateUserStatusAsync(user.Username, status);
                    Console.WriteLine($"[AuthService] Status synced to Firebase: {user.Username} -> {status}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AuthService] Failed to sync status change: {ex.Message}");
                }
            }
            
            return result;
        }

        [Obsolete("Use SetUserStatusAsync instead")]
        public static bool SetUserStatus(int userId, string status)
        {
            return SetUserStatusAsync(userId, status).GetAwaiter().GetResult();
        }

        public static bool DeleteUser(int userId)
        {
            // Get username first for Firebase sync
            var user = GetUserById(userId);
            
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Users WHERE Id = @id AND Role != 'Admin'";
            cmd.Parameters.AddWithValue("@id", userId);
            var result = cmd.ExecuteNonQuery() > 0;
            
            // Sync deletion to Firebase (fire-and-forget)
            if (result && user != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FirebaseUserService.RemoveUserAsync(user.Username);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AuthService] Failed to sync user deletion: {ex.Message}");
                    }
                });
            }
            
            return result;
        }

        public static AppUser? GetUserById(int userId)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", userId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return ReadUser(reader);
            return null;
        }

        /// <summary>
        /// Sync remote users from Firebase to local database
        /// Call this periodically or when admin opens user management
        /// </summary>
        public static async Task SyncRemoteUsersAsync()
        {
            try
            {
                var remoteUsers = await FirebaseUserService.GetAllUsersAsync();
                var localUsers = GetAllUsers();
                var localUsernames = new HashSet<string>(localUsers.Select(u => u.Username.ToLower()));
                
                foreach (var remoteUser in remoteUsers)
                {
                    // Skip if user already exists locally
                    if (localUsernames.Contains(remoteUser.Username.ToLower()))
                    {
                        // Update status if different - compare timestamps to use most recent
                        var localUser = localUsers.First(u => u.Username.Equals(remoteUser.Username, StringComparison.OrdinalIgnoreCase));
                        if (localUser.Status != remoteUser.Status)
                        {
                            // Simple conflict resolution: prefer "Active" over "Pending"
                            // This prevents approved users from being reverted to pending
                            bool shouldUpdateRemote = true;
                            if (localUser.Status == "Active" && remoteUser.Status == "Pending")
                            {
                                // Local has Active, remote still has Pending - don't downgrade
                                shouldUpdateRemote = false;
                                Console.WriteLine($"[AuthService] Keeping local Active status for {remoteUser.Username}");
                            }
                            
                            if (shouldUpdateRemote)
                            {
                                await SetUserStatusAsync(localUser.Id, remoteUser.Status);
                                Console.WriteLine($"[AuthService] Updated user status from remote: {remoteUser.Username} -> {remoteUser.Status}");
                            }
                        }
                        continue;
                    }
                    
                    // Add remote user to local database (without password - they'll need to reset)
                    using var conn = new SqliteConnection(ConnectionString);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"INSERT INTO Users (Username, PasswordHash, Salt, Role, Status, CreatedAt)
                                        VALUES (@u, @h, @s, @r, @st, @c)";
                    cmd.Parameters.AddWithValue("@u", remoteUser.Username);
                    cmd.Parameters.AddWithValue("@h", "REMOTE_USER"); // Placeholder - user needs to set password
                    cmd.Parameters.AddWithValue("@s", "REMOTE_USER");
                    cmd.Parameters.AddWithValue("@r", remoteUser.Role);
                    cmd.Parameters.AddWithValue("@st", remoteUser.Status);
                    cmd.Parameters.AddWithValue("@c", remoteUser.CreatedAt.ToString("o"));
                    
                    try
                    {
                        cmd.ExecuteNonQuery();
                        Console.WriteLine($"[AuthService] Added remote user: {remoteUser.Username}");
                    }
                    catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                    {
                        // User already exists (race condition)
                        Console.WriteLine($"[AuthService] Remote user already exists: {remoteUser.Username}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Failed to sync remote users: {ex.Message}");
            }
        }

        // ── Helpers ──

        private static AppUser ReadUser(SqliteDataReader reader)
        {
            return new AppUser
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                PasswordHash = reader.GetString(2),
                Salt = reader.GetString(3),
                Role = reader.GetString(4),
                Status = reader.GetString(5),
                CreatedAt = DateTime.Parse(reader.GetString(6))
            };
        }

        private static string GenerateSalt()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static string HashPassword(string password, string salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(password),
                Convert.FromBase64String(salt),
                100_000,
                HashAlgorithmName.SHA256);
            return Convert.ToBase64String(pbkdf2.GetBytes(32));
        }

        private static string GenerateInviteCode()
        {
            var bytes = new byte[6];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            // Produce a readable 8-char uppercase code
            return Convert.ToHexString(bytes).Substring(0, 8).ToUpperInvariant();
        }
    }
}
