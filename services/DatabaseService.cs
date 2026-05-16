using System;
using Microsoft.Data.Sqlite;

namespace PinayPalBackupManager.Services
{
    public static class DatabaseService
    {
        private static string _connectionString = string.Empty;
        private static SqliteConnection? _cachedConnection;
        private static readonly object _connectionLock = new object();
        
        public static void Initialize(string dbPath)
        {
            // Configure connection string with pooling enabled
            _connectionString = $"Data Source={dbPath};Pooling=True;Cache=Shared;";
        }
        
        public static SqliteConnection GetConnection()
        {
            lock (_connectionLock)
            {
                // For SQLite, we can reuse a single connection for better performance
                // SQLite handles concurrent access with locking at the file level
                if (_cachedConnection == null || _cachedConnection.State != System.Data.ConnectionState.Open)
                {
                    _cachedConnection = new SqliteConnection(_connectionString);
                    _cachedConnection.Open();
                }
                
                return _cachedConnection;
            }
        }
        
        public static SqliteConnection CreateNewConnection()
        {
            // Create a new connection when needed (for parallel operations)
            return new SqliteConnection(_connectionString);
        }
        
        public static void CloseConnection()
        {
            lock (_connectionLock)
            {
                if (_cachedConnection != null)
                {
                    _cachedConnection.Close();
                    _cachedConnection.Dispose();
                    _cachedConnection = null;
                }
            }
        }
    }
}
