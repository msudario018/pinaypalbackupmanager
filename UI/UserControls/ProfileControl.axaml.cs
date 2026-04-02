using Avalonia.Controls;
using Avalonia.Interactivity;
using PinayPalBackupManager.Services;
using System;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ProfileControl : UserControl
    {
        public event Action? OnUserManagementRequested;
        public event Action? OnLogoutRequested;

        public ProfileControl()
        {
            InitializeComponent();
            
            // Setup button handlers
            SetupButtonHandlers();
            
            // Update display
            UpdateProfileDisplay();
            
            // Listen for auth changes
            AuthService.OnUserChanged += (user) => UpdateProfileDisplay();
        }

        private void SetupButtonHandlers()
        {
            // Admin options
            var btnUserManagement = this.FindControl<Button>("BtnUserManagement");
            if (btnUserManagement != null)
            {
                btnUserManagement.Click += (s, e) => OnUserManagementRequested?.Invoke();
            }
            
            var btnSystemInfo = this.FindControl<Button>("BtnSystemInfo");
            if (btnSystemInfo != null)
            {
                btnSystemInfo.Click += async (s, e) => await ShowSystemInfo();
            }
            
            var btnInviteCodes = this.FindControl<Button>("BtnInviteCodes");
            if (btnInviteCodes != null)
            {
                btnInviteCodes.Click += (s, e) => ShowInviteCodes();
            }
            
            var btnLogs = this.FindControl<Button>("BtnLogs");
            if (btnLogs != null)
            {
                btnLogs.Click += (s, e) => ShowLogs();
            }
            
            // Profile actions
            var btnChangePassword = this.FindControl<Button>("BtnChangePassword");
            var btnChangePassword2 = this.FindControl<Button>("BtnChangePassword2");
            if (btnChangePassword != null)
            {
                btnChangePassword.Click += async (s, e) => await ShowChangePasswordDialog();
            }
            if (btnChangePassword2 != null)
            {
                btnChangePassword2.Click += async (s, e) => await ShowChangePasswordDialog();
            }
            
            var btnChangeUsername = this.FindControl<Button>("BtnChangeUsername");
            var btnChangeUsername2 = this.FindControl<Button>("BtnChangeUsername2");
            if (btnChangeUsername != null)
            {
                btnChangeUsername.Click += async (s, e) => await ShowChangeUsernameDialog();
            }
            if (btnChangeUsername2 != null)
            {
                btnChangeUsername2.Click += async (s, e) => await ShowChangeUsernameDialog();
            }
            
            var btnUploadAvatar = this.FindControl<Button>("BtnUploadAvatar");
            if (btnUploadAvatar != null)
            {
                btnUploadAvatar.Click += async (s, e) => await UploadAvatar();
            }
            
            // Logout
            var btnLogout = this.FindControl<Button>("BtnLogout");
            if (btnLogout != null)
            {
                btnLogout.Click += async (s, e) => await ShowLogoutConfirmation();
            }
        }

        private void UpdateProfileDisplay()
        {
            var txtUsername = this.FindControl<TextBlock>("TxtUsername");
            var txtUserRole = this.FindControl<TextBlock>("TxtUserRole");
            var txtUserStatus = this.FindControl<TextBlock>("TxtUserStatus");
            var txtAccountType = this.FindControl<TextBlock>("TxtAccountType");
            var txtMemberSince = this.FindControl<TextBlock>("TxtMemberSince");
            var adminSection = this.FindControl<Border>("AdminSection");

            if (AuthService.CurrentUser != null)
            {
                var user = AuthService.CurrentUser;
                txtUsername!.Text = user.Username;
                txtUserRole!.Text = user.Role;
                txtUserStatus!.Text = user.Status == "Active" ? "✓ Active" : user.Status;
                txtUserStatus!.Foreground = user.Status == "Active" 
                    ? Avalonia.Media.Brush.Parse("#A6E3A1") 
                    : Avalonia.Media.Brush.Parse("#F38BA8");
                
                txtAccountType!.Text = user.Role == "Admin" ? "Administrator" : "Standard";
                txtMemberSince!.Text = user.CreatedAt.ToString("MMM dd, yyyy");
                
                // Show admin section only to admins
                adminSection!.IsVisible = AuthService.IsAdmin;
            }
            else
            {
                txtUsername!.Text = "Guest";
                txtUserRole!.Text = "Not logged in";
                txtUserStatus!.Text = "⚠ Offline";
                txtUserStatus!.Foreground = Avalonia.Media.Brush.Parse("#F9E2AF");
                txtAccountType!.Text = "Limited";
                txtMemberSince!.Text = "N/A";
                adminSection!.IsVisible = false;
            }
        }

        private async System.Threading.Tasks.Task ShowSystemInfo()
        {
            // TODO: Implement system info dialog
            NotificationService.ShowBackupToast("Profile", "System info feature coming soon!", "Info");
        }

        private void ShowInviteCodes()
        {
            // TODO: Navigate to invite codes section
            NotificationService.ShowBackupToast("Profile", "Invite codes feature coming soon!", "Info");
        }

        private void ShowLogs()
        {
            // TODO: Open logs directory or show logs viewer
            NotificationService.ShowBackupToast("Profile", "Logs feature coming soon!", "Info");
        }

        private async System.Threading.Tasks.Task ShowChangePasswordDialog()
        {
            // TODO: Implement change password dialog
            NotificationService.ShowBackupToast("Profile", "Password change feature coming soon!", "Info");
        }

        private async System.Threading.Tasks.Task ShowChangeUsernameDialog()
        {
            // TODO: Implement change username dialog
            NotificationService.ShowBackupToast("Profile", "Username change feature coming soon!", "Info");
        }

        private async System.Threading.Tasks.Task UploadAvatar()
        {
            // TODO: Implement avatar upload
            NotificationService.ShowBackupToast("Profile", "Avatar upload feature coming soon!", "Info");
        }

        private async Task ShowLogoutConfirmation()
        {
            var dialog = new LogoutConfirmationDialog();
            var window = new Window
            {
                Title = "Confirm Logout",
                Content = dialog,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Background = Avalonia.Media.Brushes.Transparent
            };

            dialog.OnLogoutConfirmed += (sender, e) =>
            {
                window.Close();
                OnLogoutRequested?.Invoke();
            };

            dialog.OnCancel += (sender, e) => window.Close();

            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await window.ShowDialog(parentWindow);
            }
        }
    }
}
