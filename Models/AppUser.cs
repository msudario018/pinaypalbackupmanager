using System;

namespace PinayPalBackupManager.Models
{
    public class AppUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public string Role { get; set; } = "User";       // "Admin" or "User"
        public string Status { get; set; } = "Pending";   // "Active", "Pending", "Disabled"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
