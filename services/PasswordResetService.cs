using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.Services
{
    public static class PasswordResetService
    {
        private const int TokenExpirationHours = 1;
        private const int TokenLengthBytes = 32;

        public static async Task InitializeAsync()
        {
            using var connection = DatabaseService.GetConnection();
            // Connection is already opened by GetConnection()

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS PasswordResetTokens (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Token TEXT NOT NULL UNIQUE,
                    ExpiresAt TEXT NOT NULL,
                    Used INTEGER DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (UserId) REFERENCES Users(Id)
                );
            ";

            await command.ExecuteNonQueryAsync();
        }

        public static async Task<string> GenerateResetTokenAsync(int userId)
        {
            var token = GenerateSecureToken();
            var expiresAt = DateTime.UtcNow.AddHours(TokenExpirationHours);
            var createdAt = DateTime.UtcNow;

            using var connection = DatabaseService.GetConnection();
            // Connection is already opened by GetConnection()

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO PasswordResetTokens (UserId, Token, ExpiresAt, Used, CreatedAt)
                VALUES (@UserId, @Token, @ExpiresAt, 0, @CreatedAt);
            ";
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Token", token);
            command.Parameters.AddWithValue("@ExpiresAt", expiresAt.ToString("o"));
            command.Parameters.AddWithValue("@CreatedAt", createdAt.ToString("o"));

            await command.ExecuteNonQueryAsync();

            LogService.WriteLiveLog($"[PASSWORD_RESET] Generated reset token for user {userId}", "", "Information", "SYSTEM");

            return token;
        }

        public static async Task<bool> ValidateTokenAsync(string token)
        {
            using var connection = DatabaseService.GetConnection();
            // Connection is already opened by GetConnection()

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ExpiresAt, Used FROM PasswordResetTokens
                WHERE Token = @Token
                ORDER BY CreatedAt DESC
                LIMIT 1;
            ";
            command.Parameters.AddWithValue("@Token", token);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var expiresAt = DateTime.Parse(reader.GetString(0));
                var used = reader.GetInt32(1) == 1;

                if (used)
                {
                    LogService.WriteLiveLog("[PASSWORD_RESET] Token already used", "", "Warning", "SYSTEM");
                    return false;
                }

                if (DateTime.UtcNow > expiresAt)
                {
                    LogService.WriteLiveLog("[PASSWORD_RESET] Token expired", "", "Warning", "SYSTEM");
                    return false;
                }

                return true;
            }

            LogService.WriteLiveLog("[PASSWORD_RESET] Invalid token", "", "Warning", "SYSTEM");
            return false;
        }

        public static async Task<int?> GetUserIdByTokenAsync(string token)
        {
            using var connection = DatabaseService.GetConnection();
            // Connection is already opened by GetConnection()

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT UserId FROM PasswordResetTokens
                WHERE Token = @Token AND Used = 0
                ORDER BY CreatedAt DESC
                LIMIT 1;
            ";
            command.Parameters.AddWithValue("@Token", token);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return reader.GetInt32(0);
            }

            return null;
        }

        public static async Task MarkTokenAsUsedAsync(string token)
        {
            using var connection = DatabaseService.GetConnection();
            // Connection is already opened by GetConnection()

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE PasswordResetTokens
                SET Used = 1
                WHERE Token = @Token;
            ";
            command.Parameters.AddWithValue("@Token", token);

            await command.ExecuteNonQueryAsync();

            LogService.WriteLiveLog("[PASSWORD_RESET] Token marked as used", "", "Information", "SYSTEM");
        }

        public static async Task CleanupExpiredTokensAsync()
        {
            using var connection = DatabaseService.GetConnection();
            // Connection is already opened by GetConnection()

            var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM PasswordResetTokens
                WHERE ExpiresAt < @Now OR Used = 1;
            ";
            command.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));

            var deletedCount = await command.ExecuteNonQueryAsync();

            if (deletedCount > 0)
            {
                LogService.WriteLiveLog($"[PASSWORD_RESET] Cleaned up {deletedCount} expired/used tokens", "", "Information", "SYSTEM");
            }
        }

        public static async Task InvalidateUserTokensAsync(int userId)
        {
            using var connection = DatabaseService.GetConnection();
            // Connection is already opened by GetConnection()

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE PasswordResetTokens
                SET Used = 1
                WHERE UserId = @UserId AND Used = 0;
            ";
            command.Parameters.AddWithValue("@UserId", userId);

            var invalidatedCount = await command.ExecuteNonQueryAsync();

            LogService.WriteLiveLog($"[PASSWORD_RESET] Invalidated {invalidatedCount} tokens for user {userId}", "", "Information", "SYSTEM");
        }

        private static string GenerateSecureToken()
        {
            var randomBytes = new byte[TokenLengthBytes];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }

        public static async Task<string> SendPasswordResetEmailAsync(string email, string resetToken)
        {
            // In a real implementation, this would send an actual email
            // For now, we'll log the reset link and return it for testing purposes
            
            var resetLink = $"pinaypal://reset-password?token={resetToken}";
            
            LogService.WriteLiveLog($"[PASSWORD_RESET] Password reset link for {email}: {resetLink}", "", "Information", "SYSTEM");
            
            // Simulate email sending delay
            await Task.Delay(100);
            
            return resetLink;
        }
    }
}
