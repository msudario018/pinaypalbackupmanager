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

        /// <summary>
        /// Roaming AppData folder (%APPDATA%\PinayPalBackupManager).
        /// This directory is external to the Velopack installation root (%LOCALAPPDATA%\PinayPal.PinayPalBackupManager)
        /// and permanently survives app updates, uninstalls, and setup reinstalls.
        /// </summary>
        public static string RoamingDirectory
        {
            get
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyFolderName);
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string RoamingDataDirectory
        {
            get
            {
                var path = Path.Combine(RoamingDirectory, "Data");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string RoamingUsersDatabasePath => Path.Combine(RoamingDataDirectory, "users.db");

        public static string GetPath(string fileName)
        {
            return Path.Combine(CurrentDirectory, fileName);
        }

        public static string GetExistingOrCurrentPath(string fileName)
        {
            var dataPath = GetDataPath(fileName);
            if (File.Exists(dataPath))
            {
                return dataPath;
            }

            var roamingDataPath = Path.Combine(RoamingDataDirectory, fileName);
            if (File.Exists(roamingDataPath))
            {
                return roamingDataPath;
            }

            var roamingPath = Path.Combine(RoamingDirectory, fileName);
            if (File.Exists(roamingPath))
            {
                return roamingPath;
            }

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

            return dataPath;
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
            var roamingDataPath = Path.Combine(RoamingDataDirectory, fileName);

            // Copy from legacy if exists and not yet in current/data/roaming
            if (File.Exists(legacyPath) && !File.Exists(currentPath) && !File.Exists(dataPath) && !File.Exists(roamingDataPath))
            {
                try { File.Copy(legacyPath, currentPath, true); } catch { }
            }

            // Move from current to Data directory
            if (File.Exists(currentPath) && !File.Exists(dataPath))
            {
                try { File.Copy(currentPath, dataPath, true); } catch { }
            }

            // Smart sync between local Data and Roaming Data with 0-byte corruption guard
            try
            {
                var dataInfo = File.Exists(dataPath) ? new FileInfo(dataPath) : null;
                var roamingInfo = File.Exists(roamingDataPath) ? new FileInfo(roamingDataPath) : null;

                // If local Data is missing or 0 bytes, but Roaming has valid data, recover from Roaming
                if ((dataInfo == null || dataInfo.Length == 0) && roamingInfo != null && roamingInfo.Length > 0)
                {
                    File.Copy(roamingDataPath, dataPath, true);
                }
                // If Roaming is missing or 0 bytes, but local Data has valid data, mirror to Roaming
                else if ((roamingInfo == null || roamingInfo.Length == 0) && dataInfo != null && dataInfo.Length > 0)
                {
                    File.Copy(dataPath, roamingDataPath, true);
                }
                // If both exist and have content, sync whichever has the newer write time
                else if (dataInfo != null && roamingInfo != null && dataInfo.Length > 0 && roamingInfo.Length > 0)
                {
                    if (roamingInfo.LastWriteTimeUtc > dataInfo.LastWriteTimeUtc.AddSeconds(1))
                    {
                        File.Copy(roamingDataPath, dataPath, true);
                    }
                    else if (dataInfo.LastWriteTimeUtc > roamingInfo.LastWriteTimeUtc.AddSeconds(1))
                    {
                        File.Copy(dataPath, roamingDataPath, true);
                    }
                }
            }
            catch { }
        }

        public static void MirrorToRoaming(string fileName)
        {
            try
            {
                var dataPath = GetDataPath(fileName);
                var roamingDataPath = Path.Combine(RoamingDataDirectory, fileName);
                if (File.Exists(dataPath))
                {
                    File.Copy(dataPath, roamingDataPath, true);
                }
            }
            catch { }
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
