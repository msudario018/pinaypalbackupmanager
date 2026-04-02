using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace PinayPalBackupManager
{
    public class DebugAuth
    {
        public static void CheckDatabase()
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager");
            var dbPath = Path.Combine(appDataDir, "users.db");
            
            Console.WriteLine($"Database path: {dbPath}");
            Console.WriteLine($"Database exists: {File.Exists(dbPath)}");
            
            if (File.Exists(dbPath))
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={dbPath}");
                    conn.Open();
                    
                    // Check users
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Username, Role, Status FROM Users";
                    using var reader = cmd.ExecuteReader();
                    
                    Console.WriteLine("\nExisting users:");
                    while (reader.Read())
                    {
                        Console.WriteLine($"- {reader["Username"]} ({reader["Role"]}) - {reader["Status"]}");
                    }
                    
                    // Check AppConfig
                    cmd.CommandText = "SELECT Key, Value FROM AppConfig";
                    using var reader2 = cmd.ExecuteReader();
                    
                    Console.WriteLine("\nAppConfig:");
                    while (reader2.Read())
                    {
                        Console.WriteLine($"- {reader2["Key"]}: {reader2["Value"]}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading database: {ex.Message}");
                }
            }
        }
        
        public static void ResetDatabase()
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager");
            var dbPath = Path.Combine(appDataDir, "users.db");
            
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
                Console.WriteLine("Database reset successfully");
            }
            else
            {
                Console.WriteLine("Database file not found");
            }
        }
    }
}
