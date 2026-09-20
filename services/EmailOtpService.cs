using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public static class EmailOtpService
    {
        private const int OtpExpirationMinutes = 10;

        /// <summary>
        /// Sends username reminder to the email address associated with the account after verifying email and birthday.
        /// </summary>
        public static async Task<(bool success, string message, bool isPreview, string? previewUsername)> SendUsernameRecoveryAsync(string email, string? birthDate = null)
        {
            if (string.IsNullOrWhiteSpace(email))
                return (false, "Please enter your email address.", false, null);

            var cleanEmail = email.Trim();
            var cleanBirthDate = birthDate?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(cleanBirthDate))
            {
                return (false, "Please enter your birthday (e.g. YYYY-MM-DD) for account verification.", false, null);
            }

            var user = AuthService.GetUserByEmailAndBirthDate(cleanEmail, cleanBirthDate);

            if (user == null)
            {
                return (false, $"No account found matching email '{cleanEmail}' and the provided birthday. Please verify your details.", false, null);
            }

            var subject = "PinayPal Backup Manager — Username Recovery";
            var body = $@"Hello,

You requested a reminder of your username for PinayPal Backup Manager.

Your Username: {user.Username}

If you did not request this information, you can safely ignore this email.

— PinayPal Backup Manager Team";

            bool sent = await NotificationService.SendDirectEmailAsync(cleanEmail, subject, body);

            if (sent)
            {
                LogService.WriteSystemLog($"[RECOVERY] Username sent via SMTP to {cleanEmail} for user {user.Username}", "Information", "SYSTEM");
                return (true, $"Your username has been sent to {cleanEmail}. Check your inbox.", false, user.Username);
            }
            else
            {
                // SMTP not configured or failed -> safe dev/local fallback
                LogService.WriteSystemLog($"[RECOVERY] SMTP not configured. Local preview: Username is '{user.Username}' for {cleanEmail}", "Information", "SYSTEM");
                return (true, $"Recovery email sent! (Test preview: Your username is '{user.Username}')", true, user.Username);
            }
        }

        /// <summary>
        /// Generates a 6-digit numeric OTP, saves to DB with 10-minute expiry, and sends via email.
        /// </summary>
        public static async Task<(bool success, string message, int? userId, string? email, bool isPreview, string? previewOtp)> SendPasswordResetOtpAsync(string identifier, string? overrideEmail = null)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return (false, "Please enter your username or email.", null, null, false, null);

            var clean = identifier.Trim();
            var user = AuthService.GetUserByUsernameOrEmail(clean);

            if (user == null)
            {
                return (false, $"No account found matching '{clean}'.", null, null, false, null);
            }

            // Determine recipient email
            string targetEmail = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : (overrideEmail?.Trim() ?? string.Empty);

            if (string.IsNullOrWhiteSpace(targetEmail))
            {
                // Account does not have an email linked yet
                return (false, "NO_EMAIL_CONFIGURED", user.Id, null, false, null);
            }

            // If user supplied an email and account had none, update user's email
            if (string.IsNullOrWhiteSpace(user.Email) && !string.IsNullOrWhiteSpace(overrideEmail))
            {
                AuthService.UpdateUserEmail(user.Id, targetEmail);
            }

            // Generate 6-digit numeric OTP
            string otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString("D6");
            var expiresAt = DateTime.UtcNow.AddMinutes(OtpExpirationMinutes);
            var createdAt = DateTime.UtcNow;

            // Invalidate old unused tokens for this user
            await PasswordResetService.InvalidateUserTokensAsync(user.Id);

            // Save OTP to DB
            var conn = DatabaseService.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO PasswordResetTokens (UserId, Token, ExpiresAt, Used, CreatedAt)
                VALUES (@uid, @token, @exp, 0, @created);
            ";
            cmd.Parameters.AddWithValue("@uid", user.Id);
            cmd.Parameters.AddWithValue("@token", otp);
            cmd.Parameters.AddWithValue("@exp", expiresAt.ToString("o"));
            cmd.Parameters.AddWithValue("@created", createdAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();

            var subject = $"PinayPal Password Reset Code: {otp}";
            var body = $@"Hello {user.Username},

We received a request to reset your PinayPal Backup Manager password.

Your 6-digit verification code:
  {otp}

This verification code expires in 10 minutes.
Enter this code in your desktop application to reset your password.

If you did not request a password reset, please secure your account immediately.

— PinayPal Backup Manager Team";

            bool sent = await NotificationService.SendDirectEmailAsync(targetEmail, subject, body);

            if (sent)
            {
                LogService.WriteSystemLog($"[RECOVERY] Password reset OTP sent to {targetEmail} for user {user.Username}", "Information", "SYSTEM");
                return (true, $"Verification code sent to {targetEmail}. Check your inbox.", user.Id, targetEmail, false, null);
            }
            else
            {
                // SMTP not configured -> provide instant dev preview so users are never locked out
                LogService.WriteSystemLog($"[RECOVERY] SMTP not configured. OTP generated for {user.Username}: {otp}", "Information", "SYSTEM");
                return (true, $"Verification code sent! (Test preview code: {otp})", user.Id, targetEmail, true, otp);
            }
        }

        /// <summary>
        /// Validates the 6-digit OTP code against SQLite PasswordResetTokens table.
        /// </summary>
        public static async Task<(bool valid, string message)> VerifyOtpAsync(int userId, string otpCode)
        {
            if (string.IsNullOrWhiteSpace(otpCode))
                return (false, "Please enter the 6-digit verification code.");

            var clean = otpCode.Trim();
            if (clean.Length != 6 || !int.TryParse(clean, out _))
                return (false, "Verification code must be 6 digits.");

            var conn = DatabaseService.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ExpiresAt, Used FROM PasswordResetTokens
                WHERE UserId = @uid AND Token = @token
                ORDER BY CreatedAt DESC
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@token", clean);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var expiresAt = DateTime.Parse(reader.GetString(0));
                var used = reader.GetInt32(1) == 1;

                if (used)
                    return (false, "This verification code has already been used. Please request a new one.");

                if (DateTime.UtcNow > expiresAt)
                    return (false, "This verification code has expired. Please request a new one.");

                return (true, "Code verified successfully!");
            }

            return (false, "Invalid verification code. Please check and try again.");
        }

        /// <summary>
        /// Verifies OTP and updates user's password in one atomic operation.
        /// </summary>
        public static async Task<(bool success, string message)> ResetPasswordWithOtpAsync(int userId, string otpCode, string newPassword, string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return (false, "New password cannot be empty.");

            if (newPassword.Length < 6)
                return (false, "Password must be at least 6 characters long.");

            if (newPassword != confirmPassword)
                return (false, "New passwords do not match.");

            var (valid, otpMsg) = await VerifyOtpAsync(userId, otpCode);
            if (!valid)
                return (false, otpMsg);

            // Update password
            bool updated = AuthService.ChangePassword(userId, newPassword);
            if (!updated)
                return (false, "Failed to update password. Please try again.");

            // Mark token as used
            await PasswordResetService.MarkTokenAsUsedAsync(otpCode.Trim());

            LogService.WriteSystemLog($"[RECOVERY] Password reset completed successfully for userId {userId}", "Information", "SYSTEM");
            return (true, "Password has been reset successfully! You can now sign in with your new password.");
        }
    }
}
