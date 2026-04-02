using System;
using System.IO;

namespace PinayPalBackupManager.Services
{
    public static class FirebaseConfig
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PinayPalBackupManager",
            "firebase_config.txt"
        );

        public static bool IsFirebaseEnabled
        {
            get
            {
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        var content = File.ReadAllText(ConfigPath).Trim();
                        return content.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    return false; // Default to disabled
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                    File.WriteAllText(ConfigPath, value ? "true" : "false");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FirebaseConfig] Error saving config: {ex.Message}");
                }
            }
        }

        public static void ToggleFirebase()
        {
            IsFirebaseEnabled = !IsFirebaseEnabled;
            Console.WriteLine($"[FirebaseConfig] Firebase {(IsFirebaseEnabled ? "ENABLED" : "DISABLED")}");
        }
    }
}
