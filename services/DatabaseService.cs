using System;
using Microsoft.Data.Sqlite;

namespace PinayPalBackupManager.Services
{
    public static class DatabaseService
    {
        private static string _connectionString = string.Empty;
        
        public static void Initialize(string dbPath)
        {
            // Configure connection string with pooling enabled
            _connectionString = $"Data Source={dbPath};Pooling=True;Cache=Shared;";
        }
        
        public static SqliteConnection GetConnection()
        {
            // SQLite connection pooling reuses underlying connections safely per thread,
            // preventing reader collision and premature disposal across concurrent callers.
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }
        
        public static SqliteConnection CreateNewConnection()
        {
            // Create a new connection when needed (for manual open/close lifecycle)
            return new SqliteConnection(_connectionString);
        }
        
        public static void CloseConnection()
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
