using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class UserManagementControl : UserControl
    {
        private ObservableCollection<InviteCodeViewModel> _inviteCodes = new();
        private ObservableCollection<UserViewModel> _users = new();

        public UserManagementControl()
        {
            InitializeComponent();
            LoadDataAsync();
        }

        private async void LoadDataAsync()
        {
            await LoadInviteCodesAsync();
            await LoadUsersAsync();
        }

        private async Task LoadInviteCodesAsync()
        {
            try
            {
                var codes = await FirebaseInviteService.GetAllInviteCodesAsync();
                _inviteCodes.Clear();
                foreach (var code in codes)
                {
                    _inviteCodes.Add(new InviteCodeViewModel
                    {
                        Code = code.code,
                        CreatedAt = FormatDateTime(code.created_at),
                        CreatedBy = code.created_by,
                        IsUsed = code.is_used,
                        UsedBy = code.used_by ?? "N/A",
                        UsedAt = code.used_at ?? "N/A"
                    });
                }
                LstInviteCodes.ItemsSource = _inviteCodes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserManagement] Error loading invite codes: {ex.Message}");
            }
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                var users = AuthService.GetAllUsers();
                _users.Clear();
                foreach (var user in users)
                {
                    _users.Add(new UserViewModel
                    {
                        username = user.Username,
                        role = user.Role,
                        status = user.Status,
                        is_pending = user.Status == "Pending",
                        is_active = user.Status == "Active",
                        is_disabled = user.Status == "Disabled",
                        id = user.Id
                    });
                }
                LstUsers.ItemsSource = _users;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserManagement] Error loading users: {ex.Message}");
            }
        }

        private string FormatDateTime(string dateTimeString)
        {
            try
            {
                if (DateTime.TryParse(dateTimeString, out var dt))
                {
                    return dt.ToString("yyyy-MM-dd HH:mm");
                }
            }
            catch { }
            return dateTimeString;
        }

        private async void OnGenerateInviteCodeClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var createdBy = AuthService.CurrentUser?.Username ?? "admin";
                var code = await FirebaseInviteService.GenerateInviteCodeAsync(createdBy);
                
                if (!string.IsNullOrEmpty(code))
                {
                    NotificationService.ShowBackupToast("Invite Code", $"Generated: {code}", "Success");
                    await LoadInviteCodesAsync();
                }
                else
                {
                    NotificationService.ShowBackupToast("Invite Code", "Failed to generate code", "Error");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserManagement] Error generating invite code: {ex.Message}");
                NotificationService.ShowBackupToast("Invite Code", "Error generating code", "Error");
            }
        }

        private async void OnRefreshInviteCodesClick(object? sender, RoutedEventArgs e)
        {
            await LoadInviteCodesAsync();
            NotificationService.ShowBackupToast("Invite Codes", "Refreshed", "Info");
        }

        private async void OnCleanupOldCodesClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var deletedCount = await FirebaseInviteService.DeleteOldFormatCodesAsync();
                if (deletedCount > 0)
                {
                    NotificationService.ShowBackupToast("Invite Codes", $"Deleted {deletedCount} old-format codes", "Success");
                    await LoadInviteCodesAsync();
                }
                else
                {
                    NotificationService.ShowBackupToast("Invite Codes", "No old-format codes found", "Info");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserManagement] Error cleaning up old codes: {ex.Message}");
                NotificationService.ShowBackupToast("Invite Codes", "Error cleaning up codes", "Error");
            }
        }

        private async void OnDeleteInviteCodeClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is InviteCodeViewModel code)
                {
                    var result = await FirebaseInviteService.DeleteInviteCodeAsync(code.Code);
                    if (result)
                    {
                        NotificationService.ShowBackupToast("Invite Code", $"Deleted: {code.Code}", "Success");
                        await LoadInviteCodesAsync();
                    }
                    else
                    {
                        NotificationService.ShowBackupToast("Invite Code", "Failed to delete code", "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserManagement] Error deleting invite code: {ex.Message}");
                NotificationService.ShowBackupToast("Invite Code", "Error deleting code", "Error");
            }
        }

        private async void OnRefreshUsersClick(object? sender, RoutedEventArgs e)
        {
            await LoadUsersAsync();
            NotificationService.ShowBackupToast("Users", "Refreshed", "Info");
        }

        private async void OnApproveUserClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is UserViewModel user)
                {
                    var result = await AuthService.SetUserStatusAsync(user.id, "Active");
                    if (result)
                    {
                        await FirebaseUserService.UpdateUserStatusAsync(user.username, "Active");
                        NotificationService.ShowBackupToast("User Management", $"Approved: {user.username}", "Success");
                        await LoadUsersAsync();
                    }
                    else
                    {
                        NotificationService.ShowBackupToast("User Management", "Failed to approve user", "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserManagement] Error approving user: {ex.Message}");
                NotificationService.ShowBackupToast("User Management", "Error approving user", "Error");
            }
        }

        private async void OnDisableUserClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is UserViewModel user)
                {
                    var result = await AuthService.SetUserStatusAsync(user.id, "Disabled");
                    if (result)
                    {
                        await FirebaseUserService.UpdateUserStatusAsync(user.username, "Disabled");
                        NotificationService.ShowBackupToast("User Management", $"Disabled: {user.username}", "Warning");
                        await LoadUsersAsync();
                    }
                    else
                    {
                        NotificationService.ShowBackupToast("User Management", "Failed to disable user", "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserManagement] Error disabling user: {ex.Message}");
                NotificationService.ShowBackupToast("User Management", "Error disabling user", "Error");
            }
        }

        private async void OnEnableUserClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is UserViewModel user)
                {
                    var result = await AuthService.SetUserStatusAsync(user.id, "Active");
                    if (result)
                    {
                        await FirebaseUserService.UpdateUserStatusAsync(user.username, "Active");
                        NotificationService.ShowBackupToast("User Management", $"Enabled: {user.username}", "Success");
                        await LoadUsersAsync();
                    }
                    else
                    {
                        NotificationService.ShowBackupToast("User Management", "Failed to enable user", "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserManagement] Error enabling user: {ex.Message}");
                NotificationService.ShowBackupToast("User Management", "Error enabling user", "Error");
            }
        }

        private async void OnChangePasswordClick(object? sender, RoutedEventArgs e)
        {
            // TODO: Implement password change dialog
            NotificationService.ShowBackupToast("User Management", "Password change dialog coming soon", "Info");
        }

        private async void OnChangeUsernameClick(object? sender, RoutedEventArgs e)
        {
            // TODO: Implement username change dialog
            NotificationService.ShowBackupToast("User Management", "Username change dialog coming soon", "Info");
        }
    }
}

namespace PinayPalBackupManager.UI.UserControls
{
    public class InviteCodeViewModel
    {
        public string Code { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public bool IsUsed { get; set; }
        public string UsedBy { get; set; } = "";
        public string UsedAt { get; set; } = "";
    }

    public class UserViewModel
    {
        public string username { get; set; } = "";
        public string role { get; set; } = "";
        public string status { get; set; } = "";
        public bool is_pending { get; set; }
        public bool is_active { get; set; }
        public bool is_disabled { get; set; }
        public int id { get; set; }
    }
}
