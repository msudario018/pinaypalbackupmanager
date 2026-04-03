using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PinayPalBackupManager.Services
{
    public static class LogService
    {
        public static event Action<string, string>? OnNewLogEntry;

        public static void WriteLiveLog(string message, string logFile, string level = "Information", string trigger = "SYSTEM")
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logEntry = $"[{timestamp}] [{level.ToUpper()}] [{trigger.ToUpper()}] {message}";

            // Trigger UI update
            OnNewLogEntry?.Invoke(logEntry, logFile);

            // Save to file
            if (!string.IsNullOrWhiteSpace(logFile))
            {
                try
                {
                    File.AppendAllLines(logFile, [logEntry]);
                }
                catch
                {
                    // Ignore write errors (similar to PowerShell's SilentlyContinue)
                }
            }
        }

        public static List<string> ImportLatestLogs(string logFile, int lineCount = 50)
        {
            if (File.Exists(logFile))
            {
                try
                {
                    // Materialize the lines first to ensure Reverse() works on a solid collection
                    var allLines = File.ReadAllLines(logFile);
                    return allLines.Reverse().Take(lineCount).ToList();
                }
                catch
                {
                    return new List<string>();
                }
            }
            return new List<string>();
        }

        public static void ClearLogs(string logFile)
        {
            if (string.IsNullOrWhiteSpace(logFile)) return;
            try
            {
                if (File.Exists(logFile))
                {
                    File.WriteAllText(logFile, string.Empty);
                }
            }
            catch
            {
                // Ignore errors
            }
        }
    }
}
