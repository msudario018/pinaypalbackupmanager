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

        public static string DataDirectory
        {
            get
            {
                var path = Path.Combine(CurrentDirectory, "Data");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string LogsDirectory
        {
            get
            {
                var path = Path.Combine(DataDirectory, "logs");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string GetDataPath(string fileName)
        {
            return Path.Combine(DataDirectory, fileName);
        }

        public static string GetLogPath(string fileName)
        {
            return Path.Combine(LogsDirectory, fileName);
        }

        public static string UsersDatabasePath => GetDataPath("users.db");

        public static string SystemLogPath => GetLogPath("system_log.txt");

        public static void MigrateFile(string fileName)
        {
            var legacyPath = Path.Combine(LegacyDirectory, fileName);
            var currentPath = GetPath(fileName);
            var dataPath = GetDataPath(fileName);

            // Copy from legacy if exists and not yet in current
            if (File.Exists(legacyPath) && !File.Exists(currentPath) && !File.Exists(dataPath))
            {
                try { File.Copy(legacyPath, currentPath, true); } catch { }
            }

            // Move from current to Data directory
            if (File.Exists(currentPath) && !File.Exists(dataPath))
            {
                try { File.Copy(currentPath, dataPath, true); } catch { }
            }
        }

        public static void MigrateLogFile(string fileName)
        {
            var currentPath = GetPath(fileName);
            var logPath = GetLogPath(fileName);

            if (File.Exists(currentPath) && !File.Exists(logPath))
            {
                try { File.Copy(currentPath, logPath, true); } catch { }
            }
        }

        public static void MigrateKnownFiles()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);

            MigrateFile("users.db");
            MigrateFile("invite.txt");
            MigrateFile("firebase_config.txt");
            MigrateFile("update_prefs.txt");
            MigrateFile("avatar.png");
            MigrateFile("config.json");
            MigrateFile("session.dat");
            MigrateFile("config_salt.bin");

            MigrateLogFile("system_log.txt");
            MigrateLogFile("startup.log");
            MigrateLogFile("live_log.txt");
        }
    }
}
