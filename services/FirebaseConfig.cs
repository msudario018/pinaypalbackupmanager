using System;
using System.IO;

namespace PinayPalBackupManager.Services
{
    public static class FirebaseConfig
    {
        private static string ConfigPath
        {
            get
            {
                AppDataPaths.MigrateFile("firebase_config.txt");
                return AppDataPaths.GetPath("firebase_config.txt");
            }
        }

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
