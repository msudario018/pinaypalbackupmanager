using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;
using BCrypt.Net;

namespace PinayPalBackupManager.Services
{
    public static class AuthService
    {
        private static string _dbPath = string.Empty;
        private static AppUser? _currentUser;
        private static readonly object _userLock = new object();
        
        public static AppUser? CurrentUser
        {
            get
            {
                lock (_userLock)
                {
                    return _currentUser;
                }
            }
            private set
            {
                lock (_userLock)
                {
                    _currentUser = value;
                }
            }
        }
        public static event Action<AppUser?>? OnUserChanged;

        public static async Task InitializeAsync()
        {
            AppDataPaths.MigrateKnownFiles();
            _dbPath = AppDataPaths.GetPath("users.db");
            DatabaseService.Initialize(_dbPath);
            EnsureDatabase();
            
            // Initialize password reset service
            await PasswordResetService.InitializeAsync();
            
            // Set connection string for FirebaseUserService
            FirebaseUserService.ConnectionString = ConnectionString;
            
            // Firebase sync listener disabled by default to prevent interference with local user data
            // It can be manually started if needed for bidirectional sync
            // _ = Task.Run(async () => await FirebaseUserService.StartUserSyncListenerAsync());
            
            // Firebase will be initialized on-demand to avoid blocking
        }

        private static string ConnectionString => $"Data Source={_dbPath}";

        private static void EnsureDatabase()
        {
            using var conn = DatabaseService.GetConnection();
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
                );
                CREATE TABLE IF NOT EXISTS FailedLoginAttempts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL,
                    AttemptTime TEXT NOT NULL,
                    IpAddress TEXT
                );
                CREATE TABLE IF NOT EXISTS AuditLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Action TEXT NOT NULL,
                    Actor TEXT NOT NULL,
                    TargetUser TEXT,
                    Details TEXT,
                    Timestamp TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_failed_login_username ON FailedLoginAttempts(Username);
                CREATE INDEX IF NOT EXISTS idx_failed_login_time ON FailedLoginAttempts(AttemptTime);
                CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON AuditLog(Timestamp);";
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
            using var conn = DatabaseService.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Users LIMIT 1";
            return cmd.ExecuteReader().HasRows;
        }

        /// <summary>
        /// Register the very first user as Admin (auto-active). Subsequent users need a valid invite code.
        /// </summary>
        public static async Task<(bool success, string message)> RegisterAsync(string username, string password, string? inviteCode = null)
        {
            // Validate username
            var usernameValidation = InputValidationService.ValidateUsername(username);
            if (!usernameValidation.isValid)
                return (false, usernameValidation.error);
            
            if (string.IsNullOrWhiteSpace(password))
                return (false, "Password is required.");

            // Strong password requirements
            if (password.Length < 8)
                return (false, "Password must be at least 8 characters long.");
            
            if (!password.Any(char.IsUpper))
                return (false, "Password must contain at least one uppercase letter.");
            
            if (!password.Any(char.IsLower))
                return (false, "Password must contain at least one lowercase letter.");
            
            if (!password.Any(char.IsDigit))
                return (false, "Password must contain at least one digit.");
            
            // Check for special characters
            var specialChars = "!@#$%^&*()_+-=[]{}|;':\",.<>?";
            if (!password.Any(c => specialChars.Contains(c)))
                return (false, "Password must contain at least one special character.");

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

            // Generate secure hash with BCrypt (includes salt internally)
            var hash = HashPassword(password, string.Empty);
            var salt = string.Empty; // BCrypt handles salt internally

            try
            {
                // Insert user into database
                using var conn = DatabaseService.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Users (Username, PasswordHash, Salt, Role, Status, CreatedAt) VALUES (@u, @h, @s, @r, @st, @ca)";
                cmd.Parameters.AddWithValue("@u", usernameValidation.sanitized);
                cmd.Parameters.AddWithValue("@h", hash);
                cmd.Parameters.AddWithValue("@s", salt);
                cmd.Parameters.AddWithValue("@r", isFirstUser ? "Admin" : "User");
                cmd.Parameters.AddWithValue("@st", isFirstUser ? "Active" : "Pending");
                cmd.Parameters.AddWithValue("@ca", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();

                // Log user creation
                LogAuditEvent("USER_CREATED", usernameValidation.sanitized, $"Role: {(isFirstUser ? "Admin" : "User")}, Status: {(isFirstUser ? "Active" : "Pending")}");

                // Sync to Firebase (fire-and-forget, don't block registration)
                if (!isFirstUser)
                {
                    var newUser = GetUserByUsername(usernameValidation.sanitized);
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
                                LogService.WriteLiveLog($"[AuthService] Firebase sync failed: {ex.Message}", "", "Debug", "SYSTEM");
                            }
                        });
                    }
                }

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
            // Validate username
            var usernameValidation = InputValidationService.ValidateUsername(username);
            if (!usernameValidation.isValid)
                return (false, usernameValidation.error);
            
            if (string.IsNullOrWhiteSpace(password))
                return (false, "Password is required.");

            // Check rate limiting - count failed attempts in last 15 minutes
            if (IsAccountLocked(usernameValidation.sanitized))
            {
                var lockoutTime = GetLockoutTime(usernameValidation.sanitized);
                return (false, $"Account temporarily locked due to too many failed attempts. Try again in {lockoutTime} minutes.");
            }

            using var conn2 = DatabaseService.GetConnection();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users WHERE TRIM(Username) = @u COLLATE NOCASE";
            cmd2.Parameters.AddWithValue("@u", usernameValidation.sanitized);

            using var reader = cmd2.ExecuteReader();
            if (!reader.Read())
            {
                // Track failed login (user not found)
                RecordFailedLoginAttempt(usernameValidation.sanitized);
                _ = LoginHistoryService.AddLoginAsync(usernameValidation.sanitized, false, "User not found");
                return (false, "Invalid username or password.");
            }

            var user = ReadUser(reader);

            if (user.Status == "Disabled")
                return (false, "Account is disabled. Contact the admin.");

            if (user.Status == "Deleted")
                return (false, "Account has been deleted. Contact the admin if you believe this is an error.");

            if (user.Status == "Pending")
                return (false, "Account is pending approval.");

            if (!VerifyPassword(password, user.Salt, user.PasswordHash))
            {
                // Track failed login
                RecordFailedLoginAttempt(user.Username);
                _ = LoginHistoryService.AddLoginAsync(user.Username, false, "Invalid password");
                
                var failedCount = GetFailedLoginCount(user.Username);
                if (failedCount >= 5)
                {
                    return (false, "Account temporarily locked due to too many failed attempts. Please wait 15 minutes before trying again.");
                }
                
                var remainingAttempts = 5 - failedCount;
                return (false, $"Invalid username or password. {remainingAttempts} attempts remaining before account lockout.");
            }

            // Clear failed login attempts on successful login
            ClearFailedLoginAttempts(user.Username);
            
            CurrentUser = user;
            OnUserChanged?.Invoke(user);
            
            // Track successful login
            _ = LoginHistoryService.AddLoginAsync(user.Username, true);
            
            // Start session timeout monitoring
            SessionTimeoutService.Start();
            SessionTimeoutService.OnSessionTimeout += HandleSessionTimeout;
            
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
            if (user == null || user.Status != "Active") return false;
            CurrentUser = user;
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

            using var conn2 = DatabaseService.GetConnection();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users WHERE TRIM(Username) = @u COLLATE NOCASE";
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
            // Stop session timeout monitoring
            SessionTimeoutService.Stop();
            SessionTimeoutService.OnSessionTimeout -= HandleSessionTimeout;
            
            CurrentUser = null;
            OnUserChanged?.Invoke(null);
        }

        private static void HandleSessionTimeout()
        {
            // Log the session timeout
            LogAuditEvent("SESSION_TIMEOUT", CurrentUser?.Username ?? "Unknown", "Session expired due to inactivity");
            
            // Perform logout
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
                LogService.WriteLiveLog($"[AuthService] Firebase fetch failed: {ex.Message}", "", "Debug", "SYSTEM");
            }
            
            // Use Firebase code if available, otherwise fallback to local config
            var effectiveCode = !string.IsNullOrEmpty(firebaseCode) ? firebaseCode : GetInviteCodeFromConfig();
            
            // No hardcoded fallback - if no code is configured, return null
            if (string.IsNullOrEmpty(effectiveCode))
            {
                return string.Empty;
            }
            
            // Update local database with the effective code
            using var conn = DatabaseService.GetConnection();
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
            if (storedCode != effectiveCode)
            {
                cmd.CommandText = "UPDATE AppConfig SET Value = @v WHERE Key = 'InviteCode'";
                cmd.Parameters.AddWithValue("@v", effectiveCode);
                cmd.ExecuteNonQuery();
                return effectiveCode;
            }

            return effectiveCode;
        }

        // Synchronous wrapper for backward compatibility
        public static string GetInviteCode()
        {
            try
            {
                return GetInviteCodeAsync().GetAwaiter().GetResult();
            }
            catch
            {
                var configCode = GetInviteCodeFromConfig();
                return configCode ?? string.Empty;
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
                LogService.WriteLiveLog($"[AuthService] Config read failed: {ex.Message}", "", "Debug", "SYSTEM");
            }
            
            return string.Empty;
        }

        public static string RotateInviteCode()
        {
            var newCode = GenerateInviteCode();

            // Update local database (INSERT or UPDATE)
            using var conn = DatabaseService.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            // Try to update first, if no rows affected, insert
            cmd.CommandText = @"
                INSERT INTO AppConfig (Key, Value)
                VALUES ('InviteCode', @v)
                ON CONFLICT(Key) DO UPDATE SET Value = @v;
            ";
            cmd.Parameters.AddWithValue("@v", newCode);
            cmd.ExecuteNonQuery();

            // Sync to Firebase (fire-and-forget, don't block UI)
            _ = Task.Run(async () =>
            {
                try
                {
                    await FirebaseInviteService.GenerateInviteCodeAsync(createdBy: "admin", code: newCode);
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[AuthService] Firebase sync failed: {ex.Message}", "", "Debug", "SYSTEM");
                }
            });

            return newCode;
        }

        public static AppUser? GetUserByUsername(string username)
        {
            using var conn = DatabaseService.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users WHERE TRIM(Username) = @u COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@u", username);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return ReadUser(reader);
            return null;
        }

        // ── User Management (Admin) ──

        /// <summary>
        /// Admin creates a user directly (no invite code required). Returns (success, message).
        /// </summary>
        public static (bool success, string message) CreateUser(string username, string password, string role = "User", string status = "Active")
        {
            var usernameValidation = InputValidationService.ValidateUsername(username);
            if (!usernameValidation.isValid)
                return (false, usernameValidation.error);

            if (string.IsNullOrWhiteSpace(password))
                return (false, "Password is required.");

            if (password.Length < 8)
                return (false, "Password must be at least 8 characters long.");
            if (!password.Any(char.IsUpper))
                return (false, "Password must contain at least one uppercase letter.");
            if (!password.Any(char.IsLower))
                return (false, "Password must contain at least one lowercase letter.");
            if (!password.Any(char.IsDigit))
                return (false, "Password must contain at least one digit.");
            var specialChars = "!@#$%^&*()_+-=[]{}|;':\",.<>?";
            if (!password.Any(c => specialChars.Contains(c)))
                return (false, "Password must contain at least one special character.");

            var hash = HashPassword(password, string.Empty);
            var salt = string.Empty;

            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Users (Username, PasswordHash, Salt, Role, Status, CreatedAt) VALUES (@u, @h, @s, @r, @st, @ca)";
                cmd.Parameters.AddWithValue("@u", usernameValidation.sanitized);
                cmd.Parameters.AddWithValue("@h", hash);
                cmd.Parameters.AddWithValue("@s", salt);
                cmd.Parameters.AddWithValue("@r", role);
                cmd.Parameters.AddWithValue("@st", status);
                cmd.Parameters.AddWithValue("@ca", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();

                LogAuditEvent("USER_CREATED", usernameValidation.sanitized, $"Role: {role}, Status: {status}, Created by admin");

                // Sync to Firebase
                var newUser = GetUserByUsername(usernameValidation.sanitized);
                if (newUser != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await FirebaseUserService.SyncUserAsync(newUser); }
                        catch (Exception ex) { LogService.WriteLiveLog($"[AuthService] Firebase sync failed: {ex.Message}", "", "Debug", "SYSTEM"); }
                    });
                }

                return (true, $"User '{usernameValidation.sanitized}' created successfully.");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return (false, "Username already exists.");
            }
            catch (Exception ex)
            {
                return (false, $"Database error: {ex.Message}");
            }
        }

        public static List<AppUser> GetAllUsers()
        {
            var users = new List<AppUser>();
            using var conn = DatabaseService.GetConnection();
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
            
            using var conn = DatabaseService.GetConnection();
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
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[AuthService] Firebase sync failed: {ex.Message}", "", "Debug", "SYSTEM");
                }

                // Log status change
                LogAuditEvent("USER_STATUS_CHANGED", user.Username, $"New status: {status}");
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
            using var conn = DatabaseService.GetConnection();
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
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[AuthService] Firebase sync failed: {ex.Message}", "", "Debug", "SYSTEM");
                }

                // Log user deletion
                LogAuditEvent("USER_DELETED", user.Username, "User account deleted by admin");
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
            using var conn = DatabaseService.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Username, PasswordHash, Salt, Role, Status, CreatedAt FROM Users WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", userId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return ReadUser(reader);
            return null;
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

            using var conn = DatabaseService.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET PasswordHash = @h, Salt = @s WHERE Id = @id";
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.Parameters.AddWithValue("@s", salt);
            cmd.Parameters.AddWithValue("@id", userId);
            var result = cmd.ExecuteNonQuery() > 0;

            if (result)
            {
                // Log password change
                LogAuditEvent("PASSWORD_CHANGED", user.Username, "Password changed by user or admin");
            }

            return result;
        }

        public static bool ChangeUsername(int userId, string newUsername)
        {
            var user = GetUserById(userId);
            if (user == null) return false;

            // Validate and sanitize the new username
            var validation = InputValidationService.ValidateUsername(newUsername);
            if (!validation.isValid)
                return false;

            var sanitized = validation.sanitized;
            var oldUsername = user.Username;

            // Prevent changing to the same username (case-insensitive check)
            if (string.Equals(oldUsername, sanitized, StringComparison.OrdinalIgnoreCase))
                return true; // Already the same, nothing to do

            using var conn = DatabaseService.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET Username = @u WHERE Id = @id";
            cmd.Parameters.AddWithValue("@u", sanitized);
            cmd.Parameters.AddWithValue("@id", userId);
            var result = cmd.ExecuteNonQuery() > 0;

            if (result)
            {
                // Update current user if it's the same user
                if (CurrentUser != null && CurrentUser.Id == userId)
                {
                    CurrentUser.Username = sanitized;
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
                        LogService.WriteLiveLog($"[AuthService] Firebase sync failed: {ex.Message}", "", "Debug", "SYSTEM");
                    }
                });

                // Log username change
                LogAuditEvent("USERNAME_CHANGED", newUsername, $"Changed from: {oldUsername}");
            }

            return result;
        }

        public static bool UpdateAvatar(int userId, string avatarPath)
        {
            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Users SET AvatarPath = @a WHERE Id = @id";
                cmd.Parameters.AddWithValue("@a", avatarPath);
                cmd.Parameters.AddWithValue("@id", userId);
                var result = cmd.ExecuteNonQuery() > 0;

                return result;
            }
            catch
            {
                return false;
            }
        }

        public static string? GetAvatarPath(int userId)
        {
            try
            {
                using var conn = DatabaseService.GetConnection();
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

        [Obsolete("Use GetAvatarPath instead")]
        public static string? GetUserAvatar(int userId)
        {
            return GetAvatarPath(userId);
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
            // Generate cryptographically secure random salt
            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            return Convert.ToBase64String(salt);
        }

        private static string HashPassword(string password, string salt)
        {
            // Use BCrypt for secure password hashing
            // BCrypt automatically handles salt generation, but we store external salt for compatibility
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
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
            try
            {
                // BCrypt is the only supported hashing algorithm
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            }
            catch
            {
                return false;
            }
        }

        private static string GenerateInviteCode()
        {
            // Generate 8-character alphanumeric code (A-Z, 0-9) using CSPRNG
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var codeChars = new char[8];
            using (var rng = RandomNumberGenerator.Create())
            {
                var buffer = new byte[8];
                rng.GetBytes(buffer);
                for (int i = 0; i < 8; i++)
                {
                    codeChars[i] = chars[buffer[i] % chars.Length];
                }
            }
            return new string(codeChars);
        }

        // ── Rate Limiting ──

        private static bool IsAccountLocked(string username)
        {
            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM FailedLoginAttempts 
                    WHERE TRIM(Username) = @u COLLATE NOCASE
                    AND AttemptTime > datetime('now', '-15 minutes')";
                cmd.Parameters.AddWithValue("@u", username);
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                return count >= 5;
            }
            catch
            {
                return false;
            }
        }

        private static int GetLockoutTime(string username)
        {
            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT datetime(AttemptTime, '+15 minutes') - datetime('now') as remaining_minutes
                    FROM FailedLoginAttempts 
                    WHERE TRIM(Username) = @u COLLATE NOCASE
                    ORDER BY AttemptTime DESC 
                    LIMIT 1";
                cmd.Parameters.AddWithValue("@u", username);
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    var remaining = Convert.ToInt32(result);
                    return Math.Max(1, remaining);
                }
                return 15;
            }
            catch
            {
                return 15;
            }
        }

        private static void RecordFailedLoginAttempt(string username)
        {
            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO FailedLoginAttempts (Username, AttemptTime, IpAddress) VALUES (@u, datetime('now'), NULL)";
                cmd.Parameters.AddWithValue("@u", username);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignore errors - rate limiting is a security feature, not critical
            }
        }

        private static void ClearFailedLoginAttempts(string username)
        {
            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM FailedLoginAttempts WHERE TRIM(Username) = @u COLLATE NOCASE";
                cmd.Parameters.AddWithValue("@u", username);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignore errors - rate limiting is a security feature, not critical
            }
        }

        private static int GetFailedLoginCount(string username)
        {
            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM FailedLoginAttempts 
                    WHERE TRIM(Username) = @u COLLATE NOCASE
                    AND AttemptTime > datetime('now', '-15 minutes')";
                cmd.Parameters.AddWithValue("@u", username);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch
            {
                return 0;
            }
        }

        public static void ClearFailedLoginAttemptsForUser(string username)
        {
            ClearFailedLoginAttempts(username);
        }

        /// <summary>
        /// Returns true if running in a development environment (debug build or IDE attached)
        /// </summary>
        public static bool IsDevEnvironment()
        {
            var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return System.Diagnostics.Debugger.IsAttached
                || assemblyPath.Contains("\\Debug\\", StringComparison.OrdinalIgnoreCase)
                || assemblyPath.Contains("/Debug/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Create a temporary/emergency admin account (dev-only, for recovery or testing)
        /// </summary>
        public static (bool success, string message) CreateEmergencyAdmin(string username = "admin", string password = "admin123")
        {
            if (!IsDevEnvironment())
                return (false, "Emergency admin creation is only available in development mode.");

            if (HasAnyUsers())
                return (false, "Users already exist. Emergency admin can only be created when the database is empty.");

            var usernameValidation = InputValidationService.ValidateUsername(username);
            if (!usernameValidation.isValid)
                return (false, usernameValidation.error);

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                return (false, "Password must be at least 8 characters.");

            var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

            try
            {
                using var conn = DatabaseService.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Users (Username, PasswordHash, Salt, Role, Status, CreatedAt) VALUES (@u, @h, @s, @r, @st, @ca)";
                cmd.Parameters.AddWithValue("@u", usernameValidation.sanitized);
                cmd.Parameters.AddWithValue("@h", hash);
                cmd.Parameters.AddWithValue("@s", string.Empty);
                cmd.Parameters.AddWithValue("@r", "Admin");
                cmd.Parameters.AddWithValue("@st", "Active");
                cmd.Parameters.AddWithValue("@ca", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();

                LogAuditEvent("EMERGENCY_ADMIN_CREATED", usernameValidation.sanitized, "Emergency admin account created");
                return (true, $"Emergency admin '{usernameValidation.sanitized}' created.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to create emergency admin: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset all users from local database (destructive — requires confirmation)
        /// </summary>
        public static (bool success, string message) ResetAllUsers()
        {
            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                
                // Clear all related tables
                cmd.CommandText = "DELETE FROM Users";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "DELETE FROM FailedLoginAttempts";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "DELETE FROM AuditLog";
                cmd.ExecuteNonQuery();
                
                // Reset SQLite auto-increment
                cmd.CommandText = "DELETE FROM sqlite_sequence WHERE name='Users'";
                try { cmd.ExecuteNonQuery(); } catch { /* sqlite_sequence may not exist */ }
                cmd.CommandText = "DELETE FROM sqlite_sequence WHERE name='FailedLoginAttempts'";
                try { cmd.ExecuteNonQuery(); } catch { }
                cmd.CommandText = "DELETE FROM sqlite_sequence WHERE name='AuditLog'";
                try { cmd.ExecuteNonQuery(); } catch { }
                
                CurrentUser = null;
                OnUserChanged?.Invoke(null);
                
                LogAuditEvent("ALL_USERS_RESET", "System", "All users cleared from local database");
                return (true, "All local users have been reset.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to reset users: {ex.Message}");
            }
        }

        // ── Audit Logging ──

        private static void LogAuditEvent(string action, string? targetUser = null, string? details = null)
        {
            try
            {
                var actor = CurrentUser?.Username ?? "System";
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO AuditLog (Action, Actor, TargetUser, Details, Timestamp)
                    VALUES (@action, @actor, @target, @details, datetime('now'))";
                cmd.Parameters.AddWithValue("@action", action);
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@target", targetUser ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@details", details ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[AuthService] Audit logging failed: {ex.Message}", "", "Debug", "SYSTEM");
            }
        }

        public static List<AuditLogEntry> GetAuditLogs(string? targetUser = null, int limit = 100)
        {
            var logs = new List<AuditLogEntry>();
            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                if (string.IsNullOrEmpty(targetUser))
                {
                    cmd.CommandText = @"
                        SELECT Action, Actor, TargetUser, Details, Timestamp
                        FROM AuditLog
                        ORDER BY Timestamp DESC
                        LIMIT @limit";
                    cmd.Parameters.AddWithValue("@limit", limit);
                }
                else
                {
                    cmd.CommandText = @"
                        SELECT Action, Actor, TargetUser, Details, Timestamp
                        FROM AuditLog
                        WHERE TargetUser = @target OR Actor = @target
                        ORDER BY Timestamp DESC
                        LIMIT @limit";
                    cmd.Parameters.AddWithValue("@target", targetUser);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    logs.Add(new AuditLogEntry
                    {
                        Action = reader.GetString(0),
                        Actor = reader.GetString(1),
                        TargetUser = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Details = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Timestamp = DateTime.Parse(reader.GetString(4))
                    });
                }
            }
            catch
            {
                // Return empty list on error
            }
            return logs;
        }

        public static void ClearAuditLogs(int daysToKeep = 90)
        {
            try
            {
                using var conn = DatabaseService.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM AuditLog WHERE Timestamp < datetime('now', '-' || @days || ' days')";
                cmd.Parameters.AddWithValue("@days", daysToKeep);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>
        /// Request a password reset for a user by username
        /// </summary>
        public static async Task<(bool success, string message)> RequestPasswordResetAsync(string username)
        {
            // Validate username
            var usernameValidation = InputValidationService.ValidateUsername(username);
            if (!usernameValidation.isValid)
                return (false, usernameValidation.error);

            // Get user by username
            var user = GetUserByUsername(usernameValidation.sanitized);
            if (user == null)
            {
                // Don't reveal if user exists for security
                LogService.WriteLiveLog($"[PASSWORD_RESET] Password reset requested for non-existent user: {usernameValidation.sanitized}", "", "Warning", "SYSTEM");
                return (true, "If the account exists, a password reset link has been sent to the associated email.");
            }

            // Check if user is active
            if (user.Status != "Active")
            {
                LogService.WriteLiveLog($"[PASSWORD_RESET] Password reset requested for inactive user: {usernameValidation.sanitized}", "", "Warning", "SYSTEM");
                return (true, "If the account exists and is active, a password reset link has been sent.");
            }

            try
            {
                // Invalidate any existing tokens for this user
                await PasswordResetService.InvalidateUserTokensAsync(user.Id);

                // Generate new reset token
                var resetToken = await PasswordResetService.GenerateResetTokenAsync(user.Id);

                // Send password reset email (in production, this would send actual email)
                var resetLink = await PasswordResetService.SendPasswordResetEmailAsync(user.Username, resetToken);

                // Log the password reset request
                LogAuditEvent("PASSWORD_RESET_REQUESTED", user.Username, $"Password reset requested");

                LogService.WriteLiveLog($"[PASSWORD_RESET] Password reset requested for user: {usernameValidation.sanitized}", "", "Information", "SYSTEM");

                return (true, "If the account exists, a password reset link has been sent to the associated email.");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[PASSWORD_RESET] Error requesting password reset: {ex.Message}", "", "Error", "SYSTEM");
                return (false, "An error occurred while requesting password reset. Please try again.");
            }
        }

        /// <summary>
        /// Reset password using a valid reset token
        /// </summary>
        public static async Task<(bool success, string message)> ResetPasswordAsync(string token, string newPassword)
        {
            // Validate new password
            if (string.IsNullOrWhiteSpace(newPassword))
                return (false, "Password is required.");

            if (newPassword.Length < 8)
                return (false, "Password must be at least 8 characters long.");

            if (!newPassword.Any(char.IsUpper))
                return (false, "Password must contain at least one uppercase letter.");

            if (!newPassword.Any(char.IsLower))
                return (false, "Password must contain at least one lowercase letter.");

            if (!newPassword.Any(char.IsDigit))
                return (false, "Password must contain at least one digit.");

            var specialChars = "!@#$%^&*()_+-=[]{}|;':\",.<>?";
            if (!newPassword.Any(c => specialChars.Contains(c)))
                return (false, "Password must contain at least one special character.");

            // Validate token
            var isValidToken = await PasswordResetService.ValidateTokenAsync(token);
            if (!isValidToken)
            {
                LogService.WriteLiveLog("[PASSWORD_RESET] Invalid or expired reset token", "", "Warning", "SYSTEM");
                return (false, "Invalid or expired reset token. Please request a new password reset.");
            }

            // Get user ID from token
            var userId = await PasswordResetService.GetUserIdByTokenAsync(token);
            if (userId == null)
            {
                LogService.WriteLiveLog("[PASSWORD_RESET] Could not find user for token", "", "Error", "SYSTEM");
                return (false, "Invalid reset token.");
            }

            try
            {
                // Get user
                var user = GetUserById(userId.Value);
                if (user == null)
                {
                    LogService.WriteLiveLog($"[PASSWORD_RESET] User not found for ID: {userId}", "", "Error", "SYSTEM");
                    return (false, "User not found.");
                }

                // Generate new password hash
                var newHash = HashPassword(newPassword, string.Empty);

                // Update password in database
                using var conn = DatabaseService.GetConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Users SET PasswordHash = @hash WHERE Id = @userId";
                cmd.Parameters.AddWithValue("@hash", newHash);
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                await cmd.ExecuteNonQueryAsync();

                // Mark token as used
                await PasswordResetService.MarkTokenAsUsedAsync(token);

                // Invalidate all other tokens for this user
                await PasswordResetService.InvalidateUserTokensAsync(userId.Value);

                // Clear failed login attempts for this user
                ClearFailedLoginAttemptsForUser(user.Username);

                // Log the password reset
                LogAuditEvent("PASSWORD_RESET_COMPLETED", user.Username, "Password reset successfully");

                LogService.WriteLiveLog($"[PASSWORD_RESET] Password reset completed for user: {user.Username}", "", "Information", "SYSTEM");

                return (true, "Password has been reset successfully. You can now log in with your new password.");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[PASSWORD_RESET] Error resetting password: {ex.Message}", "", "Error", "SYSTEM");
                return (false, "An error occurred while resetting password. Please try again.");
            }
        }

        /// <summary>
        /// Clean up expired password reset tokens (should be called periodically)
        /// </summary>
        public static async Task CleanupExpiredResetTokensAsync()
        {
            try
            {
                await PasswordResetService.CleanupExpiredTokensAsync();
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[PASSWORD_RESET] Error cleaning up expired tokens: {ex.Message}", "", "Error", "SYSTEM");
            }
        }
    }
}

public class AuditLogEntry
{
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string? TargetUser { get; set; }
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; }
}
