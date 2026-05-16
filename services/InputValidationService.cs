using System;
using System.Text.RegularExpressions;
using System.IO;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Provides comprehensive input validation and sanitization for security
    /// </summary>
    public static class InputValidationService
    {
        private static readonly Regex DangerousChars = new(@"[<>""'&%$;(){}[\]|\\\/]", RegexOptions.Compiled);
        private static readonly Regex SqlInjection = new(@"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|UNION|SCRIPT)\b|--|\/\*|\*\/|;|\bOR\b|\bAND\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PathTraversal = new(@"(\.\.[\\\/]|[\\\/]\.\.[\\\/]|[\\\/]\.\.$)", RegexOptions.Compiled);
        private static readonly Regex ValidUsername = new(@"^[a-zA-Z0-9_]{3,30}$", RegexOptions.Compiled);
        private static readonly Regex ValidEmail = new(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);

        /// <summary>
        /// Validates and sanitizes a string input
        /// </summary>
        public static (bool isValid, string sanitized, string error) ValidateString(string input, string fieldName, int maxLength = 255)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (false, string.Empty, $"{fieldName} cannot be empty.");

            if (input.Length > maxLength)
                return (false, string.Empty, $"{fieldName} cannot exceed {maxLength} characters.");

            // Check for dangerous characters
            if (DangerousChars.IsMatch(input))
                return (false, string.Empty, $"{fieldName} contains invalid characters.");

            // Check for SQL injection patterns
            if (SqlInjection.IsMatch(input))
                return (false, string.Empty, $"{fieldName} contains potentially dangerous content.");

            // Sanitize by removing any potentially harmful characters
            var sanitized = DangerousChars.Replace(input, "");
            sanitized = sanitized.Trim();

            return (true, sanitized, string.Empty);
        }

        /// <summary>
        /// Validates and sanitizes username
        /// </summary>
        public static (bool isValid, string sanitized, string error) ValidateUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return (false, string.Empty, "Username cannot be empty.");

            username = username.Trim();

            if (!ValidUsername.IsMatch(username))
                return (false, string.Empty, "Username must be 3-30 characters long and contain only letters, numbers, and underscores.");

            return (true, username, string.Empty);
        }

        /// <summary>
        /// Validates and sanitizes file path to prevent path traversal attacks
        /// </summary>
        public static (bool isValid, string sanitized, string error) ValidateFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return (false, string.Empty, "File path cannot be empty.");

            // Check for path traversal attempts
            if (PathTraversal.IsMatch(path))
                return (false, string.Empty, "File path contains invalid path traversal characters.");

            try
            {
                // Get full path and validate it
                var fullPath = Path.GetFullPath(path);
                
                // Additional validation: ensure path doesn't point to system directories
                var systemDirs = new[] { "Windows", "System32", "Program Files", "Program Files (x86)" };
                foreach (var dir in systemDirs)
                {
                    if (fullPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
                        return (false, string.Empty, "Access to system directories is not allowed.");
                }

                return (true, fullPath, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Invalid file path: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates email format
        /// </summary>
        public static (bool isValid, string error) ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return (false, "Email cannot be empty.");

            email = email.Trim();

            if (!ValidEmail.IsMatch(email))
                return (false, "Invalid email format.");

            return (true, string.Empty);
        }

        /// <summary>
        /// Validates numeric input
        /// </summary>
        public static (bool isValid, int value, string error) ValidateInteger(string input, string fieldName, int min = 0, int max = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (false, 0, $"{fieldName} cannot be empty.");

            if (!int.TryParse(input.Trim(), out int value))
                return (false, 0, $"{fieldName} must be a valid number.");

            if (value < min || value > max)
                return (false, 0, $"{fieldName} must be between {min} and {max}.");

            return (true, value, string.Empty);
        }

        /// <summary>
        /// Validates and sanitizes database connection strings
        /// </summary>
        public static (bool isValid, string sanitized, string error) ValidateConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return (false, string.Empty, "Connection string cannot be empty.");

            // Check for dangerous patterns in connection strings
            var dangerousPatterns = new[]
            {
                "DROP ", "DELETE FROM", "TRUNCATE", "INSERT INTO", "UPDATE SET",
                "--", "/*", "*/", "xp_", "sp_executesql"
            };

            foreach (var pattern in dangerousPatterns)
            {
                if (connectionString.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return (false, string.Empty, "Connection string contains potentially dangerous content.");
            }

            return (true, connectionString.Trim(), string.Empty);
        }

        /// <summary>
        /// Sanitizes log messages to prevent log injection
        /// </summary>
        public static string SanitizeLogMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            // Remove or escape characters that could interfere with log parsing
            return message
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t")
                .Replace("\0", "\\0")
                .Trim();
        }

        /// <summary>
        /// Validates backup file names
        /// </summary>
        public static (bool isValid, string sanitized, string error) ValidateBackupFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return (false, string.Empty, "File name cannot be empty.");

            // Remove any path components
            fileName = Path.GetFileName(fileName);

            // Check for invalid characters
            var invalidChars = Path.GetInvalidFileNameChars();
            if (fileName.IndexOfAny(invalidChars) >= 0)
                return (false, string.Empty, "File name contains invalid characters.");

            // Check for dangerous patterns
            if (DangerousChars.IsMatch(fileName))
                return (false, string.Empty, "File name contains invalid characters.");

            // Ensure reasonable length
            if (fileName.Length > 255)
                return (false, string.Empty, "File name is too long.");

            return (true, fileName, string.Empty);
        }
    }
}
