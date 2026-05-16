using System;
using System.Collections.Generic;
using System.Linq;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Provides validation utilities to prevent null reference exceptions
    /// </summary>
    public static class ValidationService
    {
        /// <summary>
        /// Safely gets a value or returns a default if null
        /// </summary>
        public static T GetValueOrDefault<T>(T? value, T defaultValue) where T : class
        {
            return value ?? defaultValue;
        }

        /// <summary>
        /// Safely gets a value or returns a default if null (for value types)
        /// </summary>
        public static T GetValueOrDefault<T>(T? value, T defaultValue) where T : struct
        {
            return value ?? defaultValue;
        }

        /// <summary>
        /// Safely gets a string value or returns empty if null
        /// </summary>
        public static string GetStringOrDefault(string? value)
        {
            return value ?? string.Empty;
        }

        /// <summary>
        /// Safely gets a string value or returns a default if null or empty
        /// </summary>
        public static string GetStringOrDefault(string? value, string defaultValue)
        {
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        /// <summary>
        /// Validates that an object is not null
        /// </summary>
        public static bool IsNotNull<T>(T? value) where T : class
        {
            return value != null;
        }

        /// <summary>
        /// Validates that a string is not null or empty
        /// </summary>
        public static bool IsNotNullOrEmpty(string? value)
        {
            return !string.IsNullOrEmpty(value);
        }

        /// <summary>
        /// Validates that a collection is not null and has items
        /// </summary>
        public static bool IsNotNullOrEmpty<T>(IEnumerable<T>? collection)
        {
            return collection != null && collection.Any();
        }

        /// <summary>
        /// Safely executes an action with null checking
        /// </summary>
        public static void SafeExecute<T>(T? item, Action<T> action) where T : class
        {
            if (item != null)
            {
                action(item);
            }
        }

        /// <summary>
        /// Safely executes a function with null checking
        /// </summary>
        public static TResult SafeExecute<T, TResult>(T? item, Func<T, TResult> func, TResult defaultValue) where T : class
        {
            return item != null ? func(item) : defaultValue;
        }

        /// <summary>
        /// Validates multiple objects are not null
        /// </summary>
        public static bool AreNotNull(params object?[] objects)
        {
            return objects.All(obj => obj != null);
        }

        /// <summary>
        /// Gets a property value safely or returns default
        /// </summary>
        public static TResult GetPropertyOrDefault<T, TResult>(T? obj, Func<T, TResult> property, TResult defaultValue) where T : class
        {
            return obj != null ? property(obj) : defaultValue;
        }

        /// <summary>
        /// Validates a control exists before accessing it
        /// </summary>
        public static bool ValidateControl<T>(T? control, string controlName) where T : class
        {
            if (control == null)
            {
                LogService.WriteSystemLog($"[VALIDATION] Control '{controlName}' is null", "Warning", "UI");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Safely gets text from a text control
        /// </summary>
        public static string GetControlText<T>(T? control, string defaultValue = "") where T : class
        {
            if (control == null) return defaultValue;
            
            // This would need to be implemented based on the specific control type
            // For now, return the default value
            return defaultValue;
        }
    }
}
