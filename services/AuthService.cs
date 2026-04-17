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
        private static AppUser? _currentUser;
        public static AppUser? CurrentUser
        {
            get => _currentUser;
            private set
            {
                Console.WriteLine($"[AuthService] CurrentUser SET: from {_currentUser?.Username} to {value?.Username}");
                _currentUser = value;
            }
        }
        public static event Action<AppUser?>? OnUserChanged;

        public static void Initialize()
        {
            AppDataPaths.MigrateKnownFiles();
            _dbPath = AppDataPaths.GetPath("users.db");
            EnsureDatabase();
            
            // Set connection string for FirebaseUserService
            FirebaseUserService.ConnectionString = ConnectionString;
            
            // Firebase sync listener disabled by default to prevent interference with local user data
            // It can be manually started if needed for bidirectional sync
            // _ = Task.Run(async () => await FirebaseUserService.StartUserSyncListenerAsync());
            
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
                    CreatedAt TEXT NOT NULL,
                    AvatarPath TEXT
                );
                CREATE TABLE IF NOT EXISTS AppConfig (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();

            // Migrate: Add AvatarPath column if not exists
            try
            {
                cmd.CommandText = "ALTER TABLE Users ADD COLUMN AvatarPath TEXT";
                cmd.ExecuteNonQuery();
            }
            catch { /* Column may already exist */ }
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
        public static async Task<(bool success, string message)> RegisterAsync(string username, string password, string? inviteCode = null)
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

                // Validate invite code via Firebase
                bool isValid = await FirebaseInviteService.ValidateInviteCodeAsync(inviteCode.Trim());
                
                if (!isValid)
                    return (false, "Invalid or expired invite code.");
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
                var role = isFirstUser ? "Admin" : "User";
                var status = isFirstUser ? "Active" : "Pending";
                cmd.Parameters.AddWithValue("@r", role);
                cmd.Parameters.AddWithValue("@st", status);
                cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();

                Console.WriteLine($"[AuthService] REGISTER: Username={username}, Role={role}, Status={status}, IsFirstUser={isFirstUser}");

                if (isFirstUser)
                {
                    RotateInviteCode();
                }
                else
                {
                    // Mark invite code as used
                    if (!string.IsNullOrWhiteSpace(inviteCode))
                    {
                        await FirebaseInviteService.UseInviteCodeAsync(inviteCode.Trim(), username.Trim());
                    }
                }

                // Sync user to Firebase for Flutter app
                var user = GetUserByUsername(username);
                if (user != null)
                {
                    await FirebaseUserService.SyncUserAsync(user);
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
        
        /// <summary>
        /// Synchronous wrapper for backward compatibility
        /// </summary>
        public static (bool success, string message) Register(string username, string password, string? inviteCode = null)
        {
            try
            {
                return RegisterAsync(username, password, inviteCode).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return (false, $"Registration error: {ex.Message}");
            }
        }

        public static async Task<(bool success, string message)> LoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, "Username and password are required.");

            using var conn2 = new SqliteConnection(ConnectionString);
            conn2.Open();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users WHERE Username = @u";
            cmd2.Parameters.AddWithValue("@u", username.Trim());

            using var reader = cmd2.ExecuteReader();
            if (!reader.Read())
            {
                // Track failed login (user not found)
                _ = LoginHistoryService.AddLoginAsync(username.Trim(), false, "User not found");
                return (false, "Invalid username or password.");
            }

            var user = ReadUser(reader);
            Console.WriteLine($"[AuthService] LOGIN: Username={user.Username}, Role={user.Role}, Status={user.Status}, Id={user.Id}");

            if (user.Status == "Disabled")
                return (false, "Account is disabled. Contact the admin.");

            if (user.Status == "Deleted")
                return (false, "Account has been deleted. Contact the admin if you believe this is an error.");

            if (user.Status == "Pending")
                return (false, "Account is pending approval.");

            if (!VerifyPassword(password, user.Salt, user.PasswordHash))
            {
                // Track failed login
                _ = LoginHistoryService.AddLoginAsync(user.Username, false, "Invalid password");
                return (false, "Invalid username or password.");
            }

            CurrentUser = user;
            Console.WriteLine($"[AuthService] LOGIN SUCCESS: CurrentUser set to {user.Username} with Role={user.Role}");
            OnUserChanged?.Invoke(user);
            
            // Track successful login
            _ = LoginHistoryService.AddLoginAsync(user.Username, true);
            
            return (true, $"Welcome, {user.Username}!");
        }

        // Synchronous wrapper for backward compatibility
        public static (bool success, string message) Login(string username, string password)
        {
            try
            {
                return LoginAsync(username, password).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return (false, $"Login error: {ex.Message}");
            }
        }

        public static bool LoginById(int userId)
        {
            var user = GetUserById(userId);
            Console.WriteLine($"[AuthService] LOGINBYID: UserId={userId}, User={user?.Username}, Role={user?.Role}, Status={user?.Status}");
            if (user == null || user.Status != "Active") return false;
            CurrentUser = user;
            Console.WriteLine($"[AuthService] LOGINBYID SUCCESS: CurrentUser set to {user.Username} with Role={user.Role}");
            OnUserChanged?.Invoke(user);
            return true;
        }

        /// <summary>
        /// Verify credentials without completing full login (used for 2FA flow).
        /// Returns the user if credentials are valid, without setting CurrentUser.
        /// </summary>
        public static async Task<(bool success, AppUser? user, string message)> VerifyCredentialsAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, null, "Username and password are required.");

            using var conn2 = new SqliteConnection(ConnectionString);
            conn2.Open();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users WHERE Username = @u";
            cmd2.Parameters.AddWithValue("@u", username.Trim());

            using var reader = cmd2.ExecuteReader();
            if (!reader.Read())
            {
                _ = LoginHistoryService.AddLoginAsync(username.Trim(), false, "User not found");
                return (false, null, "Invalid username or password.");
            }

            var user = ReadUser(reader);

            if (user.Status == "Disabled")
                return (false, null, "Account is disabled. Contact the admin.");

            if (user.Status == "Deleted")
                return (false, null, "Account has been deleted. Contact the admin if you believe this is an error.");

            if (user.Status == "Pending")
                return (false, null, "Account is pending approval.");

            if (!VerifyPassword(password, user.Salt, user.PasswordHash))
            {
                _ = LoginHistoryService.AddLoginAsync(user.Username, false, "Invalid password");
                return (false, null, "Invalid username or password.");
            }

            return (true, user, "Credentials verified");
        }

        /// <summary>
        /// Set current user after 2FA verification is complete.
        /// </summary>
        public static void SetCurrentUserFor2FA(AppUser user)
        {
            CurrentUser = user;
            OnUserChanged?.Invoke(user);
            _ = LoginHistoryService.AddLoginAsync(user.Username, true);
        }

        public static void Logout()
        {
            var stackTrace = System.Environment.StackTrace;
            Console.WriteLine($"[AuthService] LOGOUT: CurrentUser was {CurrentUser?.Username} with Role={CurrentUser?.Role}");
            Console.WriteLine($"[AuthService] LOGOUT STACK TRACE:\n{stackTrace}");
            CurrentUser = null;
            OnUserChanged?.Invoke(null);
        }

        public static bool IsAdmin => CurrentUser?.Role == "Admin";

        // ── Invite Code ──

        public static async Task<string> GetInviteCodeAsync()
        {
            // Try to get from Firebase first (with timeout to avoid blocking)
            string? firebaseCode = null;
            try
            {
                var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                firebaseCode = await FirebaseInviteService.GetInviteCodeAsync().ConfigureAwait(false);
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

        // Synchronous wrapper for backward compatibility
        public static string GetInviteCode()
        {
            try
            {
                return GetInviteCodeAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] GetInviteCode error: {ex.Message}");
                return GetInviteCodeFromConfig() ?? "PINAYPAL2024";
            }
        }

        private static string GetInviteCodeFromConfig()
        {
            try
            {
                var configPath = AppDataPaths.GetExistingOrCurrentPath("invite.txt");
                
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
                    await FirebaseInviteService.GenerateInviteCodeAsync(newCode);
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

        public static async Task<bool> DeleteUserAsync(int userId)
        {
            // Get username first for Firebase sync
            var user = GetUserById(userId);
            if (user == null) return false;
            
            // First, mark as deleted in local DB and change password to prevent login
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET Status = 'Deleted', PasswordHash = 'DELETED_USER', Salt = 'DELETED_USER' WHERE Id = @id AND Role != 'Admin'";
            cmd.Parameters.AddWithValue("@id", userId);
            var result = cmd.ExecuteNonQuery() > 0;
            
            // Then sync deletion to Firebase (await this time)
            if (result)
            {
                try
                {
                    await FirebaseUserService.RemoveUserAsync(user.Username);
                    Console.WriteLine($"[AuthService] User deleted from Firebase: {user.Username}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AuthService] Failed to sync user deletion to Firebase: {ex.Message}");
                }
            }
            
            return result;
        }

        [Obsolete("Use DeleteUserAsync instead")]
        public static bool DeleteUser(int userId)
        {
            return DeleteUserAsync(userId).GetAwaiter().GetResult();
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
        /// COMPLETELY DISABLED for debugging - local database only
        /// </summary>
        public static async Task SyncRemoteUsersAsync()
        {
            Console.WriteLine("[AuthService] SyncRemoteUsersAsync completely disabled - local database only");
            return;
        }

        public static bool VerifyPassword(int userId, string password)
        {
            var user = GetUserById(userId);
            if (user == null) return false;
            return VerifyPassword(password, user.Salt, user.PasswordHash);
        }

        public static bool ChangePassword(int userId, string newPassword)
        {
            var user = GetUserById(userId);
            if (user == null) return false;

            var salt = GenerateSalt();
            var hash = HashPassword(newPassword, salt);

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET PasswordHash = @h, Salt = @s WHERE Id = @id";
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.Parameters.AddWithValue("@s", salt);
            cmd.Parameters.AddWithValue("@id", userId);
            var result = cmd.ExecuteNonQuery() > 0;

            if (result)
            {
                Console.WriteLine($"[AuthService] Password changed for user ID {userId}");
            }

            return result;
        }

        public static bool ChangeUsername(int userId, string newUsername)
        {
            var user = GetUserById(userId);
            if (user == null) return false;

            var oldUsername = user.Username;

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET Username = @u WHERE Id = @id";
            cmd.Parameters.AddWithValue("@u", newUsername);
            cmd.Parameters.AddWithValue("@id", userId);
            var result = cmd.ExecuteNonQuery() > 0;

            if (result)
            {
                // Update current user if it's the same user
                if (CurrentUser != null && CurrentUser.Id == userId)
                {
                    CurrentUser.Username = newUsername;
                    OnUserChanged?.Invoke(CurrentUser);
                }

                // Sync to Firebase: remove old entry, add new entry
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FirebaseUserService.RemoveUserAsync(oldUsername);
                        var updatedUser = GetUserById(userId);
                        if (updatedUser != null)
                            await FirebaseUserService.SyncUserAsync(updatedUser);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AuthService] Failed to sync username change to Firebase: {ex.Message}");
                    }
                });
            }

            return result;
        }

        public static bool UpdateAvatar(int userId, string avatarPath)
        {
            try
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Users SET AvatarPath = @a WHERE Id = @id";
                cmd.Parameters.AddWithValue("@a", avatarPath);
                cmd.Parameters.AddWithValue("@id", userId);
                var result = cmd.ExecuteNonQuery() > 0;

                if (result)
                {
                    Console.WriteLine($"[AuthService] Avatar updated for user ID {userId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Failed to update avatar: {ex.Message}");
                return false;
            }
        }

        public static string? GetUserAvatar(int userId)
        {
            try
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT AvatarPath FROM Users WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", userId);
                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
            catch
            {
                return null;
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
            // Use timestamp-based salt for Flutter app compatibility
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(timestamp.ToString()));
        }

        private static string HashPassword(string password, string salt)
        {
            // Use SHA256 with salt for Flutter app compatibility
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var combined = password + salt;
            var bytes = Encoding.UTF8.GetBytes(combined);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
        
        private static string HashPasswordPBKDF2(string password, string salt)
        {
            // Old PBKDF2 method for backward compatibility with existing users
            using var pbkdf2 = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(password),
                Convert.FromBase64String(salt),
                100_000,
                HashAlgorithmName.SHA256);
            return Convert.ToBase64String(pbkdf2.GetBytes(32));
        }
        
        private static bool VerifyPassword(string password, string salt, string storedHash)
        {
            // Try SHA256 first (new method for Flutter app)
            var sha256Hash = HashPassword(password, salt);
            if (string.Equals(sha256Hash, storedHash, StringComparison.Ordinal))
                return true;
            
            // Fallback to PBKDF2 (old method for existing PC app users)
            var pbkdf2Hash = HashPasswordPBKDF2(password, salt);
            return string.Equals(pbkdf2Hash, storedHash, StringComparison.Ordinal);
        }

        private static string GenerateInviteCode()
        {
            // Generate 8-character alphanumeric code (A-Z, 0-9)
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            var code = new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
            return code;
        }
    }
}
