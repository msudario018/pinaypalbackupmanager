using System;
using System.IO;

namespace PinayPalBackupManager.Services
{
    public static class AppDataPaths
    {
        public const string CurrentFolderName = "PinayPal.PinayPalBackupManager";
        public const string LegacyFolderName = "PinayPalBackupManager";

        public static string CurrentDirectory
        {
            get
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CurrentFolderName);
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string LegacyDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyFolderName);

        public static string GetPath(string fileName)
        {
            return Path.Combine(CurrentDirectory, fileName);
        }

        public static string GetExistingOrCurrentPath(string fileName)
        {
            var currentPath = GetPath(fileName);
            if (File.Exists(currentPath))
            {
                return currentPath;
            }

            var legacyPath = Path.Combine(LegacyDirectory, fileName);
            if (File.Exists(legacyPath))
            {
                return legacyPath;
            }

            return currentPath;
        }

        public static void MigrateFile(string fileName)
        {
            var legacyPath = Path.Combine(LegacyDirectory, fileName);
            var currentPath = GetPath(fileName);

            if (!File.Exists(legacyPath) || File.Exists(currentPath))
            {
                return;
            }

            Directory.CreateDirectory(CurrentDirectory);
            File.Copy(legacyPath, currentPath, true);
        }

        public static void MigrateKnownFiles()
        {
            MigrateFile("users.db");
            MigrateFile("invite.txt");
            MigrateFile("firebase_config.txt");
            MigrateFile("update_prefs.txt");
            MigrateFile("avatar.png");
            MigrateFile("startup.log");
        }
    }
}
