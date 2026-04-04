using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace PinayPalBackupManager.Services
{
    public static class AutoStartService
    {
        private const string AppName = "PinayPalBackupManager";
        private const string RegKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegKey);
                return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        public static void Enable()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;
                using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
                key?.SetValue(AppName, $"\"{exePath}\"");
            }
            catch { }
        }

        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
                key?.DeleteValue(AppName, throwOnMissingValue: false);
            }
            catch { }
        }

        public static void SetEnabled(bool enabled)
        {
            if (enabled) Enable(); else Disable();
        }
    }
}
