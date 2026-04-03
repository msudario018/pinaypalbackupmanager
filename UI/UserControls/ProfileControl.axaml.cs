using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PinayPalBackupManager.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ProfileControl : UserControl
    {
        public event Action? OnAvatarChanged;
        public event Action? OnUserManagementRequested;
        public event Action? OnLogoutRequested;

        public ProfileControl()
        {
            InitializeComponent();
            
            // Setup button handlers
            SetupButtonHandlers();
            
            // Update display
            UpdateProfileDisplay();
            LoadAvatarImage();
            
            // Listen for auth changes
            AuthService.OnUserChanged += (user) => UpdateProfileDisplay();
        }

        private void SetupButtonHandlers()
        {
            // Admin options
            var btnUserManagement = this.FindControl<Button>("BtnUserManagement");
            if (btnUserManagement != null)
            {
                btnUserManagement.Click += async (s, e) => await ShowUserManagementDialog();
            }
            
            var btnSystemInfo = this.FindControl<Button>("BtnSystemInfo");
            if (btnSystemInfo != null)
            {
                btnSystemInfo.Click += async (s, e) => await ShowSystemInfo();
            }
            
            var btnInviteCodes = this.FindControl<Button>("BtnInviteCodes");
            if (btnInviteCodes != null)
            {
                btnInviteCodes.Click += async (s, e) => await ShowInviteCodesDialog();
            }
            
            var btnLogs = this.FindControl<Button>("BtnLogs");
            if (btnLogs != null)
            {
                btnLogs.Click += (s, e) => ShowLogs();
            }
            
            // Profile actions - only from Security section
            var btnChangePassword2 = this.FindControl<Button>("BtnChangePassword2");
            if (btnChangePassword2 != null)
            {
                btnChangePassword2.Click += async (s, e) => await ShowChangePasswordDialog();
            }
            
            var btnChangeUsername2 = this.FindControl<Button>("BtnChangeUsername2");
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
            await MainWindow.ShowSystemInfoAsync();
        }

        private void ShowInviteCodes()
        {
            // Show invite code popup (deprecated - now using ShowInviteCodesDialog)
            _ = ShowInviteCodesDialog();
        }

        private void ShowLogs()
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager");
                if (Directory.Exists(logDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = logDir,
                        UseShellExecute = true
                    });
                    NotificationService.ShowBackupToast("Profile", "Logs folder opened", "Info");
                }
                else
                {
                    NotificationService.ShowBackupToast("Profile", "No logs directory found", "Warning");
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowBackupToast("Profile", $"Failed to open logs: {ex.Message}", "Error");
            }
        }

        private async System.Threading.Tasks.Task ShowChangePasswordDialog()
        {
            const string dialogKey = "change_password";
            if (NotificationService.IsDialogOpen(dialogKey))
            {
                return;
            }
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var dialog = new ChangePasswordDialog();
                var window = new Window
                {
                    Title = "Change Password",
                    Content = dialog,
                    Width = 400,
                    Height = 350,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Background = Avalonia.Media.Brushes.Transparent
                };

                dialog.OnPasswordChanged += (sender, e) =>
                {
                    window.Close();
                    NotificationService.ShowBackupToast("Profile", "Password changed successfully!", "Success");
                };

                dialog.OnCancel += (sender, e) => window.Close();

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                if (parentWindow != null)
                {
                    await window.ShowDialog(parentWindow);
                }
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task ShowChangeUsernameDialog()
        {
            const string dialogKey = "change_username";
            if (NotificationService.IsDialogOpen(dialogKey))
            {
                return;
            }
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var dialog = new ChangeUsernameDialog();
                var window = new Window
                {
                    Title = "Change Username",
                    Content = dialog,
                    Width = 400,
                    Height = 320,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Background = Avalonia.Media.Brushes.Transparent
                };

                dialog.OnUsernameChanged += (sender, e) =>
                {
                    window.Close();
                    UpdateProfileDisplay();
                    NotificationService.ShowBackupToast("Profile", "Username changed successfully!", "Success");
                };

                dialog.OnCancel += (sender, e) => window.Close();

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                if (parentWindow != null)
                {
                    await window.ShowDialog(parentWindow);
                }
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async System.Threading.Tasks.Task UploadAvatar()
        {
            try
            {
                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null)
                {
                    NotificationService.ShowBackupToast("Profile", "Storage provider not available", "Error");
                    return;
                }

                var options = new FilePickerOpenOptions
                {
                    Title = "Select Avatar Image",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Image Files")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp" }
                        }
                    }
                };

                var files = await storageProvider.OpenFilePickerAsync(options);
                if (files.Count > 0)
                {
                    var file = files[0];
                    var localPath = file.Path.LocalPath;
                    
                    // Copy to app data directory
                    var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager");
                    Directory.CreateDirectory(appDataDir);
                    var avatarPath = Path.Combine(appDataDir, "avatar.png");
                    File.Copy(localPath, avatarPath, true);
                    
                    // Load the avatar image
                    LoadAvatarImage();
                    
                    // Notify that avatar changed (so sidebar updates)
                    OnAvatarChanged?.Invoke();
                    
                    NotificationService.ShowBackupToast("Profile", "Avatar uploaded successfully!", "Success");
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowBackupToast("Profile", $"Failed to upload avatar: {ex.Message}", "Error");
            }
        }

        private void LoadAvatarImage()
        {
            try
            {
                var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager");
                var avatarPath = Path.Combine(appDataDir, "avatar.png");
                
                if (File.Exists(avatarPath))
                {
                    var imgAvatar = this.FindControl<Image>("ImgAvatar");
                    if (imgAvatar != null)
                    {
                        // Load image from file
                        var bitmap = new Avalonia.Media.Imaging.Bitmap(avatarPath);
                        imgAvatar.Source = bitmap;
                        imgAvatar.IsVisible = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProfileControl] Failed to load avatar: {ex.Message}");
            }
        }

        private async Task ShowUserManagementDialog()
        {
            const string dialogKey = "user_management";
            
            if (NotificationService.IsDialogOpen(dialogKey))
            {
                Console.WriteLine("[ProfileControl] User Management dialog already open, skipping");
                return;
            }
            
            NotificationService.RegisterDialog(dialogKey);
            
            try
            {
                var dialog = new UserManagementDialog();
                var window = new Window
                {
                    Title = "User Management",
                    Content = dialog,
                    Width = 500,
                    Height = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Background = Avalonia.Media.Brushes.Transparent
                };

                dialog.OnClose += (sender, e) => window.Close();

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                if (parentWindow != null)
                {
                    await window.ShowDialog(parentWindow);
                }
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async Task ShowInviteCodesDialog()
        {
            const string dialogKey = "invite_codes";
            
            if (NotificationService.IsDialogOpen(dialogKey))
            {
                Console.WriteLine("[ProfileControl] Invite Codes dialog already open, skipping");
                return;
            }
            
            NotificationService.RegisterDialog(dialogKey);
            
            try
            {
                var dialog = new InviteCodesDialog();
                var window = new Window
                {
                    Title = "Invite Codes",
                    Content = dialog,
                    Width = 450,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Background = Avalonia.Media.Brushes.Transparent
                };

                dialog.OnClose += (sender, e) => window.Close();

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                if (parentWindow != null)
                {
                    await window.ShowDialog(parentWindow);
                }
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async Task ShowLogoutConfirmation()
        {
            const string dialogKey = "logout_confirmation";
            
            // Check if dialog already open
            if (NotificationService.IsDialogOpen(dialogKey))
            {
                Console.WriteLine("[ProfileControl] Logout dialog already open, skipping");
                return;
            }
            
            NotificationService.RegisterDialog(dialogKey);
            
            try
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
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }
    }
}
