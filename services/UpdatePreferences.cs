using System;
using System.IO;

namespace PinayPalBackupManager.Services
{
    public static class UpdatePreferences
    {
        private static string PrefsPath
        {
            get
            {
                AppDataPaths.MigrateFile("update_prefs.txt");
                return AppDataPaths.GetPath("update_prefs.txt");
            }
        }

        public static bool LoadAutoCheckOnStartup()
        {
            try
            {
                if (!File.Exists(PrefsPath)) return true;
                var text = File.ReadAllText(PrefsPath).Trim();
                if (bool.TryParse(text, out var val)) return val;
                return true;
            }
            catch
            {
                return true;
            }
        }

        public static void SaveAutoCheckOnStartup(bool enabled)
        {
            try
            {
                File.WriteAllText(PrefsPath, enabled.ToString());
            }
            catch
            {
                // ignore
            }
        }
    }
}
