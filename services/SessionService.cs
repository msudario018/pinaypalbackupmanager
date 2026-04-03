using System;
using System.IO;

namespace PinayPalBackupManager.Services
{
    public static class SessionService
    {
        private static readonly string SessionFile = AppDataPaths.GetPath("session.dat");

        public static void SaveSession(int userId)
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.CurrentDirectory);
                File.WriteAllText(SessionFile, userId.ToString());
            }
            catch { }
        }

        public static int? LoadSession()
        {
            try
            {
                if (!File.Exists(SessionFile)) return null;
                var text = File.ReadAllText(SessionFile).Trim();
                if (int.TryParse(text, out int id)) return id;
            }
            catch { }
            return null;
        }

        public static void ClearSession()
        {
            try
            {
                if (File.Exists(SessionFile))
                    File.Delete(SessionFile);
            }
            catch { }
        }
    }
}
