using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class LogService
    {
        public static event Action<string, string>? OnNewLogEntry;
        
        // Buffered logging
        private static readonly ConcurrentQueue<LogEntry> _logBuffer = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
        private static Timer? _flushTimer;
        private static readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(2);
        private static bool _isInitialized = false;
        private static readonly object _initLock = new();
        
        private readonly record struct LogEntry(string Message, string LogFile);
        
        public static void Initialize()
        {
            lock (_initLock)
            {
                if (_isInitialized) return;
                
                _flushTimer = new Timer(_ => Flush(), null, _flushInterval, _flushInterval);
                _isInitialized = true;
            }
        }
        
        public static void Shutdown()
        {
            lock (_initLock)
            {
                if (!_isInitialized) return;
                
                _flushTimer?.Dispose();
                _flushTimer = null;
                Flush(); // Final flush
                _isInitialized = false;
            }
        }
        
        public static void Flush()
        {
            if (_logBuffer.IsEmpty) return;
            
            var entries = new List<LogEntry>();
            while (_logBuffer.TryDequeue(out var entry))
            {
                entries.Add(entry);
            }
            
            // Group by log file for batch writes
            var grouped = entries.GroupBy(e => e.LogFile);
            
            foreach (var group in grouped)
            {
                var logFile = group.Key;
                var lines = group.Select(e => e.Message).ToList();
                
                if (string.IsNullOrWhiteSpace(logFile)) continue;
                
                var fileLock = _fileLocks.GetOrAdd(logFile, _ => new SemaphoreSlim(1, 1));
                
                // Fire-and-forget with exception handling
                _ = Task.Run(async () =>
                {
                    await fileLock.WaitAsync();
                    try
                    {
                        await File.AppendAllLinesAsync(logFile, lines);
                    }
                    catch
                    {
                        // Ignore write errors
                    }
                    finally
                    {
                        fileLock.Release();
                    }
                });
            }
        }

        public static void WriteLiveLog(string message, string logFile, string level = "Information", string trigger = "SYSTEM")
        {
            Initialize();
            
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
            string logEntry = $"[{timestamp}] [{level.ToUpper()}] [{trigger.ToUpper()}] {message}";

            // Trigger UI update immediately
            OnNewLogEntry?.Invoke(logEntry, logFile);

            // Queue for batch write
            if (!string.IsNullOrWhiteSpace(logFile))
            {
                _logBuffer.Enqueue(new LogEntry(logEntry, logFile));
            }
        }

        public static void WriteSystemLog(string message, string level = "Information", string trigger = "SYSTEM")
        {
            Initialize();
            
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
            string logEntry = $"[{timestamp}] [{level.ToUpper()}] [{trigger.ToUpper()}] {message}";

            // Trigger UI update immediately
            OnNewLogEntry?.Invoke(logEntry, AppDataPaths.SystemLogPath);

            // Queue for batch write
            _logBuffer.Enqueue(new LogEntry(logEntry, AppDataPaths.SystemLogPath));
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
