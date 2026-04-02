using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
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

                var storedCode = GetInviteCode();
                if (!string.Equals(inviteCode.Trim(), storedCode, StringComparison.Ordinal))
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

                return (true, isFirstUser ? "Admin account created." : "Account registered successfully.");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint
            {
                return (false, "Username already exists.");
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
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppConfig WHERE Key = 'InviteCode'";
            var result = cmd.ExecuteScalar();
            return result?.ToString() ?? string.Empty;
        }

        public static string RotateInviteCode()
        {
            var code = GenerateInviteCode();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO AppConfig (Key, Value) VALUES ('InviteCode', @v)
                                ON CONFLICT(Key) DO UPDATE SET Value = @v";
            cmd.Parameters.AddWithValue("@v", code);
            cmd.ExecuteNonQuery();
            return code;
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
