using System;
using System.Collections.Generic;
using System.IO;
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
            
            // Initialize Firebase service
            FirebaseInviteService.Initialize();
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

                // Try Firebase validation first
                bool isValid = false;
                try
                {
                    isValid = await FirebaseInviteService.ValidateInviteCodeAsync(inviteCode.Trim());
                    if (isValid)
                    {
                        Console.WriteLine("[AuthService] Invite code validated via Firebase");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AuthService] Firebase validation failed: {ex.Message}");
                }
                
                // Fallback to local validation
                if (!isValid)
                {
                    var storedCode = GetInviteCode();
                    isValid = string.Equals(inviteCode.Trim(), storedCode, StringComparison.Ordinal);
                    if (isValid)
                    {
                        Console.WriteLine("[AuthService] Invite code validated via local database");
                    }
                }
                
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
                cmd.Parameters.AddWithValue("@st", isFirstUser ? "Active" : "Active"); // invite code = auto-active
                cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();

                if (isFirstUser)
                {
                    // Generate the first invite code
                    RotateInviteCode();
                }

                return (true, isFirstUser ? "Admin account created." : "Registration successful!");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint
            {
                return (false, "Username already exists.");
            }
        }
        
        /// <summary>
        /// Synchronous version for compatibility
        /// </summary>
        public static (bool success, string message) Register(string username, string password, string? inviteCode = null)
        {
            return RegisterAsync(username, password, inviteCode).Result;
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

        public static async Task<string> GetInviteCodeAsync()
        {
            // Try Firebase first (online), then config file, then hardcoded
            try
            {
                var firebaseCode = await FirebaseInviteService.GetInviteCodeAsync();
                if (!string.IsNullOrEmpty(firebaseCode))
                {
                    Console.WriteLine("[AuthService] Using Firebase invite code");
                    return firebaseCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Firebase failed: {ex.Message}");
            }
            
            // Fallback to local methods
            var configCode = GetInviteCodeFromConfig();
            var hardcodedCode = "PINAYPAL2024";
            var effectiveCode = !string.IsNullOrEmpty(configCode) ? configCode : hardcodedCode;
            Console.WriteLine("[AuthService] Using local invite code");
            
            return effectiveCode;
        }
        
        public static string GetInviteCode()
        {
            // Synchronous version for compatibility
            try
            {
                var firebaseCode = FirebaseInviteService.GetInviteCodeAsync().Result;
                if (!string.IsNullOrEmpty(firebaseCode))
                {
                    return firebaseCode;
                }
            }
            catch
            {
                // Firebase not available, continue with local
            }
            
            var configCode = GetInviteCodeFromConfig();
            var hardcodedCode = "PINAYPAL2024";
            var effectiveCode = !string.IsNullOrEmpty(configCode) ? configCode : hardcodedCode;
            
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppConfig WHERE Key = 'InviteCode'";
            var result = cmd.ExecuteScalar();
            
            if (result == null)
            {
                // Use effective code as default if none exists
                cmd.CommandText = @"INSERT INTO AppConfig (Key, Value) VALUES ('InviteCode', @v)";
                cmd.Parameters.AddWithValue("@v", effectiveCode);
                cmd.ExecuteNonQuery();
                return effectiveCode;
            }
            
            var storedCode = result.ToString() ?? string.Empty;
            
            // Always accept the effective code regardless of what's stored
            if (string.Equals(storedCode, effectiveCode, StringComparison.Ordinal))
            {
                return storedCode;
            }
            
            // If stored code is different, update it to the effective code
            cmd.CommandText = @"UPDATE AppConfig SET Value = @v WHERE Key = 'InviteCode'";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@v", effectiveCode);
            cmd.ExecuteNonQuery();
            
            return effectiveCode;
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
            // Use the same logic as GetInviteCode for consistency
            var configCode = GetInviteCodeFromConfig();
            var hardcodedCode = "PINAYPAL2024";
            var effectiveCode = !string.IsNullOrEmpty(configCode) ? configCode : hardcodedCode;
            
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE AppConfig SET Value = @v WHERE Key = 'InviteCode'";
            cmd.Parameters.AddWithValue("@v", effectiveCode);
            cmd.ExecuteNonQuery();
            
            return effectiveCode;
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

        public static bool SetUserStatus(int userId, string status)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET Status = @s WHERE Id = @id AND Role != 'Admin'";
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@id", userId);
            return cmd.ExecuteNonQuery() > 0;
        }

        public static bool DeleteUser(int userId)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Users WHERE Id = @id AND Role != 'Admin'";
            cmd.Parameters.AddWithValue("@id", userId);
            return cmd.ExecuteNonQuery() > 0;
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
