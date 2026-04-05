using System;
using System.Text.Json;

namespace PinayPalBackupManager.Models
{
    public class DashboardCustomization
    {
        public bool ShowSystemStatus { get; set; } = true;
        public bool ShowQuickStats { get; set; } = true;
        public bool ShowTimeSinceBackup { get; set; } = true;
        public bool ShowRecentErrors { get; set; } = true;
        public bool ShowServiceCards { get; set; } = true;
        public bool ShowHealthDashboard { get; set; } = true;
        public bool ShowOperations { get; set; } = true;
        public bool ShowConnectivity { get; set; } = true;
        public bool ShowStatsReporting { get; set; } = true;
        public bool ShowScheduleAdjustment { get; set; } = true;
        public bool ShowStorageUsage { get; set; } = true;
        public bool ShowDailySchedule { get; set; } = true;
        public bool ShowRecentActivity { get; set; } = true;
        public bool ShowSystemLogs { get; set; } = true;
        public bool CompactMode { get; set; } = false;

        private static readonly string ConfigPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PinayPalBackupManager",
            "dashboard-customization.json");

        public static DashboardCustomization Load()
        {
            try
            {
                if (System.IO.File.Exists(ConfigPath))
                {
                    var json = System.IO.File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<DashboardCustomization>(json) ?? new DashboardCustomization();
                }
            }
            catch { }
            return new DashboardCustomization();
        }

        public static void Save(DashboardCustomization settings)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
