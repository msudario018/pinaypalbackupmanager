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
        public event Action? OnLogoutRequested;
        private static DateTime _lastLoginTime = DateTime.MinValue;

        public ProfileControl()
        {
            InitializeComponent();
            
            // Setup button handlers
            SetupButtonHandlers();
            
            // Update display
            UpdateProfileDisplay();
            LoadAvatarImage();
            
            // Listen for auth changes
            AuthService.OnUserChanged += (user) =>
            {
                if (user != null)
                {
                    _lastLoginTime = DateTime.Now;
                }
                UpdateProfileDisplay();
                LoadAvatarImage();
            };
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
                btnChangePassword2.Click += (s, e) => ShowChangePasswordDialog();
            }
            
            var btnChangeUsername2 = this.FindControl<Button>("BtnChangeUsername2");
            if (btnChangeUsername2 != null)
            {
                btnChangeUsername2.Click += (s, e) => ShowChangeUsernameDialog();
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

            // Two-Factor Auth
            var btnTwoFactorAuth = this.FindControl<Button>("BtnTwoFactorAuth");
            if (btnTwoFactorAuth != null)
            {
                btnTwoFactorAuth.Click += (s, e) => ShowTwoFactorAuthDialog();
            }

            // Login History
            var btnLoginHistory = this.FindControl<Button>("BtnLoginHistory");
            if (btnLoginHistory != null)
            {
                btnLoginHistory.Click += (s, e) => ShowLoginHistoryDialog();
            }

            // Delete Account
            var btnDeleteAccount = this.FindControl<Button>("BtnDeleteAccount");
            if (btnDeleteAccount != null)
            {
                btnDeleteAccount.Click += async (s, e) => await ShowDeleteAccountDialog();
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
            var btnDeleteAccount = this.FindControl<Button>("BtnDeleteAccount");
            var txtDeleteAdminNote = this.FindControl<TextBlock>("TxtDeleteAdminNote");

            var currentUser = AuthService.CurrentUser;
            Console.WriteLine($"[ProfileControl] UpdateProfileDisplay: CurrentUser={currentUser?.Username}, Role={currentUser?.Role}, IsAdmin={AuthService.IsAdmin}");

            if (currentUser != null)
            {
                txtUsername!.Text = currentUser.Username;
                txtUserRole!.Text = currentUser.Role;
                txtUserStatus!.Text = "● Online";
                txtUserStatus!.Foreground = Avalonia.Media.Brush.Parse("#588157");
                txtAccountType!.Text = currentUser.Role;
                txtMemberSince!.Text = currentUser.CreatedAt.ToString("MMM dd, yyyy");

                // Show admin section only to admins
                adminSection!.IsVisible = AuthService.IsAdmin;

                // Disable delete for admins and show note
                if (btnDeleteAccount != null) btnDeleteAccount.IsEnabled = !string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);
                if (txtDeleteAdminNote != null) txtDeleteAdminNote.IsVisible = string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                txtUsername!.Text = "Guest";
                txtUserRole!.Text = "Not logged in";
                txtUserStatus!.Text = "⚠ Offline";
                txtUserStatus!.Foreground = Avalonia.Media.Brush.Parse("#dad7cd");
                txtAccountType!.Text = "Limited";
                txtMemberSince!.Text = "N/A";
                adminSection!.IsVisible = false;

                if (btnDeleteAccount != null) btnDeleteAccount.IsEnabled = false;
                if (txtDeleteAdminNote != null) txtDeleteAdminNote.IsVisible = false;
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
                var logDir = AppDataPaths.CurrentDirectory;
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

        private void ShowChangePasswordDialog()
        {
            const string dialogKey = "change_password";
            if (NotificationService.IsDialogOpen(dialogKey))
                return;
            
            NotificationService.RegisterDialog(dialogKey);

            var dialog = new ChangePasswordDialog();
            var window = new Window
            {
                Title = "Change Password",
                Content = dialog,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                Background = Avalonia.Media.Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaTitleBarHeightHint = 0,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                SystemDecorations = SystemDecorations.None
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;

            dialog.OnPasswordChanged += (sender, e) =>
            {
                window.Close();
                UpdateProfileDisplay();
                NotificationService.ShowBackupToast("Profile", "Password changed successfully!", "Success");
            };

            dialog.OnCancel += (sender, e) => window.Close();
            window.Closed += (_, _) => NotificationService.UnregisterDialog(dialogKey);
            window.ShowDialog(parentWindow);
        }

        private void ShowChangeUsernameDialog()
        {
            const string dialogKey = "change_username";
            if (NotificationService.IsDialogOpen(dialogKey))
                return;
            
            NotificationService.RegisterDialog(dialogKey);

            var dialog = new ChangeUsernameDialog();
            var window = new Window
            {
                Title = "Change Username",
                Content = dialog,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                Background = Avalonia.Media.Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaTitleBarHeightHint = 0,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                SystemDecorations = SystemDecorations.None
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;

            dialog.OnUsernameChanged += (sender, e) =>
            {
                window.Close();
                UpdateProfileDisplay();
                NotificationService.ShowBackupToast("Profile", "Username changed successfully!", "Success");
            };

            dialog.OnCancel += (sender, e) => window.Close();
            window.Closed += (_, _) => NotificationService.UnregisterDialog(dialogKey);
            window.ShowDialog(parentWindow);
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
                    
                    // Copy to app data directory (per-user)
                    var avatarPath = AppDataPaths.GetPath("avatar.png");
                    File.Copy(localPath, avatarPath, true);

                    // Persist avatar path to the current user profile if available
                    var user = AuthService.CurrentUser;
                    if (user != null)
                    {
                        AuthService.UpdateAvatar(user.Id, avatarPath);
                    }
                    
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
                string? avatarPath = null;

                // Prefer per-user avatar path from AuthService, fallback to legacy app data avatar.png
                var user = AuthService.CurrentUser;
                if (user != null)
                {
                    var userAvatar = AuthService.GetUserAvatar(user.Id);
                    if (!string.IsNullOrWhiteSpace(userAvatar))
                        avatarPath = userAvatar;
                }

                if (string.IsNullOrEmpty(avatarPath))
                {
                    AppDataPaths.MigrateFile("avatar.png");
                    avatarPath = AppDataPaths.GetExistingOrCurrentPath("avatar.png");
                }
                
                if (!string.IsNullOrEmpty(avatarPath) && File.Exists(avatarPath))
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
                    Width = 900,
                    Height = 850,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = true,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Avalonia.Media.Brushes.Transparent,
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                dialog.OnClose += (sender, e) => window.Close();

                await window.ShowDialog(parentWindow);
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
                    Width = 550,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0,
                    ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                    SystemDecorations = SystemDecorations.None,
                    Topmost = true,
                    Background = Avalonia.Media.Brushes.Transparent
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                dialog.OnClose += (sender, e) => window.Close();

                await window.ShowDialog(parentWindow);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async Task ShowLogoutConfirmation()
        {
            const string dialogKey = "logout_confirmation";
            
            Console.WriteLine("[ProfileControl] ShowLogoutConfirmation CALLED - CurrentUser={AuthService.CurrentUser?.Username}");
            
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
                    Topmost = true,
                    Background = Avalonia.Media.Brushes.Transparent
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                dialog.OnLogoutConfirmed += (sender, e) =>
                {
                    window.Close();
                    OnLogoutRequested?.Invoke();
                };

                dialog.OnCancel += (sender, e) => window.Close();

                await window.ShowDialog(parentWindow);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private void ShowTwoFactorAuthDialog()
        {
            var user = AuthService.CurrentUser;
            if (user == null) return;

            var dialog = new TwoFactorAuthDialog(user.Id);
            var window = new Window
            {
                Title = "Two-Factor Authentication",
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaTitleBarHeightHint = 0,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                SystemDecorations = SystemDecorations.None,
                Content = dialog
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;

            dialog.OnClose += (s, e) => window.Close();
            window.ShowDialog(parentWindow);
        }

        private void ShowLoginHistoryDialog()
        {
            var user = AuthService.CurrentUser;
            if (user == null) return;

            var dialog = new LoginHistoryDialog(user.Username);
            var window = new Window
            {
                Title = "Login History",
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaTitleBarHeightHint = 0,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                SystemDecorations = SystemDecorations.None,
                Content = dialog
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;

            dialog.OnClose += (s, e) => window.Close();
            window.ShowDialog(parentWindow);
        }

        private async Task ShowDeleteAccountDialog()
        {
            var user = AuthService.CurrentUser;
            if (user == null) return;

            // First confirmation
            var confirm1 = await ConfirmDialog.ShowAsync(
                "Delete Account",
                "WARNING: This will permanently delete your account and all associated data. This action cannot be undone.\n\nAre you absolutely sure?");

            if (!confirm1) return;

            // Second confirmation
            var confirm2 = await ConfirmDialog.ShowAsync(
                "Confirm Deletion",
                "Please confirm again: Your account, backups, and all data will be permanently removed.");

            if (!confirm2) return;

            // Delete user
            var deleted = AuthService.DeleteUser(user.Id);
            if (deleted)
            {
                NotificationService.ShowBackupToast("Account", "Account deleted. The application will now close.", "Warning");
                await Task.Delay(2000);
                Environment.Exit(0);
            }
            else
            {
                NotificationService.ShowBackupToast("Account", "Failed to delete account. Please try again.", "Error");
            }
        }
    }
}
