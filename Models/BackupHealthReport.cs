using System;

namespace PinayPalBackupManager.Models
{
    public class BackupHealthReport
    {
        public string Service { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Color { get; set; } = "Gray";
        public string LastUpdate { get; set; } = "N/A";
        public string? FileName { get; set; }
        public bool NeedsSync { get; set; }
        public string? Missing { get; set; }
    }
}
