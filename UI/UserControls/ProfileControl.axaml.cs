using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ProfileControl : UserControl
    {
        public event Action? OnAvatarChanged;
        public event Action? OnLogoutRequested;
        private static DateTime _lastLoginTime = DateTime.MinValue;
        private Action<AppUser?>? _userChangedHandler;

        public ProfileControl()
        {
            InitializeComponent();
            
            // Setup button handlers
            SetupButtonHandlers();
            
            // Update display
            UpdateProfileDisplay();
            LoadAvatarImage();
            UpdateUserStatistics();
            
            // Listen for auth changes
            _userChangedHandler = (user) =>
            {
                if (user != null)
                {
                    _lastLoginTime = DateTime.Now;
                }
                UpdateProfileDisplay();
                LoadAvatarImage();
                UpdateUserStatistics();
            };
            AuthService.OnUserChanged += _userChangedHandler;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            if (_userChangedHandler != null)
                AuthService.OnUserChanged -= _userChangedHandler;
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

            var btnEditProfile = this.FindControl<Button>("BtnEditProfile");
            if (btnEditProfile != null)
            {
                btnEditProfile.Click += async (s, e) => await ShowEditProfileDialog();
            }

            var btnResetUserDatabase = this.FindControl<Button>("BtnResetUserDatabase");
            if (btnResetUserDatabase != null)
            {
                btnResetUserDatabase.Click += async (s, e) => await ShowResetUserDatabaseConfirmation();
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
            var txtUserEmail = this.FindControl<TextBlock>("TxtUserEmail");
            var txtUserBirthDate = this.FindControl<TextBlock>("TxtUserBirthDate");
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

                if (txtUserEmail != null)
                    txtUserEmail.Text = string.IsNullOrWhiteSpace(currentUser.Email) ? "Not configured" : currentUser.Email;
                if (txtUserBirthDate != null)
                    txtUserBirthDate.Text = string.IsNullOrWhiteSpace(currentUser.BirthDate) ? "Not configured" : currentUser.BirthDate;

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
                if (txtUserEmail != null) txtUserEmail.Text = "N/A";
                if (txtUserBirthDate != null) txtUserBirthDate.Text = "N/A";
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
                var logDir = AppDataPaths.LogsDirectory;
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
                // No Topmost - ShowDialog makes it modal to parent only
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
            parentWindow?.Activate();
            window.ShowDialog(parentWindow!);
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
                // No Topmost - ShowDialog makes it modal to parent only
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
            parentWindow?.Activate();
            window.ShowDialog(parentWindow!);
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
                    var avatarPath = AppDataPaths.GetDataPath("avatar.png");
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
                    var userAvatar = AuthService.GetAvatarPath(user.Id);
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
                    // No Topmost - ShowDialog makes it modal to parent only
                    Background = Avalonia.Media.Brushes.Transparent,
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                dialog.OnClose += (sender, e) => window.Close();
                parentWindow?.Activate();
                await window.ShowDialog(parentWindow!);
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
                    // No Topmost - ShowDialog makes it modal to parent only
                    Background = Avalonia.Media.Brushes.Transparent
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                dialog.OnClose += (sender, e) => window.Close();

                await window.ShowDialog(parentWindow!);
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
                    // No Topmost - ShowDialog makes it modal to parent only
                    Background = Avalonia.Media.Brushes.Transparent,
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0,
                    ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                    SystemDecorations = SystemDecorations.None
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                dialog.OnLogoutConfirmed += (sender, e) =>
                {
                    window.Close();
                    OnLogoutRequested?.Invoke();
                };

                dialog.OnCancel += (sender, e) => window.Close();
                parentWindow?.Activate();
                await window.ShowDialog(parentWindow!);
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
                // No Topmost - ShowDialog makes it modal to parent only
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaTitleBarHeightHint = 0,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                SystemDecorations = SystemDecorations.None,
                Content = dialog
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;

            dialog.OnClose += (s, e) => window.Close();
            parentWindow?.Activate();
            window.ShowDialog(parentWindow!);
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
                // No Topmost - ShowDialog makes it modal to parent only
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaTitleBarHeightHint = 0,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                SystemDecorations = SystemDecorations.None,
                Content = dialog
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;

            dialog.OnClose += (s, e) => window.Close();
            window.ShowDialog(parentWindow!);
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
            var deleted = await AuthService.DeleteUserAsync(user.Id);
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
        
        private void UpdateUserStatistics()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    // Get all backup logs (reduced for performance)
                    var ftpLogs = LogService.ImportLatestLogs(BackupConfig.FtpLogFile, 500);
                    var mcLogs = LogService.ImportLatestLogs(BackupConfig.McLogFile, 500);
                    var sqlLogs = LogService.ImportLatestLogs(BackupConfig.SqlLogFile, 500);
                    var allLogs = ftpLogs.Concat(mcLogs).Concat(sqlLogs).ToList();
                    
                    // Filter logs for last 30 days for more relevant statistics
                    var dateFilter = DateTime.Now.AddDays(-30);
                    var filteredLogs = allLogs.Where(log => 
                    {
                        if (TryParseLogLine(log, out var timestamp, out _, out _))
                            return timestamp >= dateFilter;
                        return false;
                    }).ToList();
                    
                    // Calculate statistics
                    int totalBackups = 0;
                    int successfulBackups = 0;
                    DateTime lastBackup = DateTime.MinValue;
                    long totalStorage = 0;
                    
                    foreach (var log in filteredLogs)
                    {
                        if (log.Contains("COMPLETE") || log.Contains("SUCCESS"))
                        {
                            totalBackups++;
                            successfulBackups++;
                            
                            // Try to extract timestamp
                            if (TryParseLogLine(log, out var timestamp, out _, out _) && timestamp > lastBackup)
                                lastBackup = timestamp;
                        }
                        else if (log.Contains("ERROR") || log.Contains("FAILED"))
                        {
                            totalBackups++;
                        }
                    }
                    
                    // Calculate storage usage with error handling
                    var folders = new[]
                    {
                        BackupConfig.FtpLocalFolder,
                        BackupConfig.MailchimpFolder,
                        BackupConfig.SqlLocalFolder
                    };
                    
                    foreach (var folder in folders)
                    {
                        try
                        {
                            if (Directory.Exists(folder))
                            {
                                var dirInfo = new DirectoryInfo(folder);
                                // Limit to top-level files for performance
                                totalStorage += dirInfo.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly).Sum(f => f.Length);
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip folders we can't access
                            continue;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Profile] Error calculating storage for {folder}: {ex.Message}");
                            continue;
                        }
                    }
                    
                    // Calculate account age (from first log or current user creation)
                    var accountAge = TimeSpan.Zero;
                    if (allLogs.Count > 0)
                    {
                        var firstLog = allLogs.LastOrDefault();
                        if (!string.IsNullOrEmpty(firstLog) && TryParseLogLine(firstLog, out var firstTimestamp, out _, out _))
                            accountAge = DateTime.Now - firstTimestamp;
                    }
                    else
                    {
                        // Fallback to current user creation time
                        accountAge = DateTime.Now - _lastLoginTime;
                    }
                    
                    // Format values
                    var successRate = totalBackups > 0 ? (successfulBackups * 100.0 / totalBackups) : 100.0;
                    var storageFormatted = FormatBytes(totalStorage);
                    var accountAgeFormatted = accountAge.Days > 0 ? $"{accountAge.Days} days" : 
                                           accountAge.Hours > 0 ? $"{accountAge.Hours} hours" : 
                                           $"{accountAge.Minutes} minutes";
                    var lastBackupFormatted = lastBackup == DateTime.MinValue ? "Never" : 
                                           lastBackup.Date == DateTime.Today ? "Today" :
                                           lastBackup.Date == DateTime.Today.AddDays(-1) ? "Yesterday" :
                                           lastBackup.ToString("MMM dd, yyyy");
                    
                    // Update UI
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var txtTotalBackups = this.FindControl<TextBlock>("TxtTotalBackups");
                        var txtSuccessRate = this.FindControl<TextBlock>("TxtSuccessRate");
                        var txtAccountAge = this.FindControl<TextBlock>("TxtAccountAge");
                        var txtStorageUsed = this.FindControl<TextBlock>("TxtStorageUsed");
                        var txtLastBackup = this.FindControl<TextBlock>("TxtLastBackup");
                        
                        if (txtTotalBackups != null) txtTotalBackups.Text = totalBackups.ToString();
                        if (txtSuccessRate != null) txtSuccessRate.Text = $"{successRate:F1}%";
                        if (txtAccountAge != null) txtAccountAge.Text = accountAgeFormatted;
                        if (txtStorageUsed != null) txtStorageUsed.Text = storageFormatted;
                        if (txtLastBackup != null) txtLastBackup.Text = lastBackupFormatted;
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Profile] Error updating statistics: {ex.Message}");
                }
            });
        }
        
        private void UpdateActivityHeatmap()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    // Get all backup logs from the last 52 weeks
                    var ftpLogs = LogService.ImportLatestLogs(BackupConfig.FtpLogFile, 5000);
                    var mcLogs = LogService.ImportLatestLogs(BackupConfig.McLogFile, 5000);
                    var sqlLogs = LogService.ImportLatestLogs(BackupConfig.SqlLogFile, 5000);
                    var allLogs = ftpLogs.Concat(mcLogs).Concat(sqlLogs).ToList();
                    
                    // Calculate date range (52 weeks ago to today)
                    var endDate = DateTime.Today;
                    var startDate = endDate.AddDays(-52 * 7);
                    var totalDays = (endDate - startDate).Days + 1;
                    
                    // Count backups per day
                    var dailyCounts = new Dictionary<DateTime, int>();
                    for (int i = 0; i < totalDays; i++)
                    {
                        var date = startDate.AddDays(i);
                        dailyCounts[date] = 0;
                    }
                    
                    foreach (var log in allLogs)
                    {
                        if (log.Contains("COMPLETE") || log.Contains("SUCCESS"))
                        {
                            if (TryParseLogLine(log, out var timestamp, out _, out _))
                            {
                                var date = timestamp.Date;
                                if (dailyCounts.ContainsKey(date))
                                    dailyCounts[date]++;
                            }
                        }
                    }
                    
                    // Calculate statistics
                    var totalBackups = dailyCounts.Values.Sum();
                    var maxCount = dailyCounts.Values.Max();
                    var currentStreak = CalculateCurrentStreak(dailyCounts, endDate);
                    
                    // Generate heatmap data (52 weeks × 7 days)
                    var heatmapData = new List<List<int>>();
                    for (int week = 0; week < 52; week++)
                    {
                        var weekData = new List<int>();
                        for (int day = 0; day < 7; day++)
                        {
                            var date = startDate.AddDays(week * 7 + day);
                            weekData.Add(dailyCounts.ContainsKey(date) ? dailyCounts[date] : 0);
                        }
                        heatmapData.Add(weekData);
                    }
                    
                    // Update UI
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var container = this.FindControl<StackPanel>("HeatmapContainer");
                        var monthLabels = this.FindControl<Grid>("MonthLabelsContainer");
                        var summary = this.FindControl<TextBlock>("HeatmapSummary");
                        var streak = this.FindControl<TextBlock>("HeatmapStreak");
                        
                        if (container != null)
                        {
                            container.Children.Clear();
                            

                            // Create heatmap cells (52 weeks × 7 days)
                            for (int week = 0; week < 52; week++)
                            {
                                var weekColumn = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Spacing = 2 };
                                

                                for (int day = 0; day < 7; day++)
                                {
                                    var count = week < heatmapData.Count && day < heatmapData[week].Count 
                                        ? heatmapData[week][day] : 0;
                                    
                                    var color = GetHeatmapColor(count, maxCount);
                                    var cell = new Border
                                    {
                                        Width = 11,
                                        Height = 11,
                                        Background = new SolidColorBrush(Color.Parse(color)),
                                        CornerRadius = new Avalonia.CornerRadius(2)
                                    };
                                    ToolTip.SetTip(cell, $"Day {week * 7 + day + 1}: {count} backup(s)");
                                    
                                    weekColumn.Children.Add(cell);
                                }
                                

                                container.Children.Add(weekColumn);
                            }
                        }
                        
                        if (monthLabels != null)
                        {
                            monthLabels.Children.Clear();
                            var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
                            var startMonth = startDate.Month;
                            

                            for (int i = 0; i < 12; i++)
                            {
                                var monthIndex = (startMonth + i - 1) % 12;
                                var label = new TextBlock
                                {
                                    Text = months[monthIndex],
                                    FontSize = 9,
                                    Foreground = new SolidColorBrush(Color.Parse("#6E7681")),
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                                };
                                Grid.SetColumn(label, i);
                                monthLabels.Children.Add(label);
                            }
                        }
                        
                        if (summary != null)
                            summary.Text = $"{totalBackups} backups in the last year";
                        
                        if (streak != null)
                            streak.Text = $"Current streak: {currentStreak} days";
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Profile] Error updating heatmap: {ex.Message}");
                }
            });
        }
        
        private static int CalculateCurrentStreak(Dictionary<DateTime, int> dailyCounts, DateTime endDate)
        {
            var streak = 0;
            var currentDate = endDate;
            
            while (currentDate >= endDate.AddDays(-52 * 7))
            {
                if (dailyCounts.ContainsKey(currentDate) && dailyCounts[currentDate] > 0)
                    streak++;
                else
                    break;
                    
                currentDate = currentDate.AddDays(-1);
            }
            
            return streak;
        }
        
        private static string GetHeatmapColor(int count, int maxCount)
        {
            if (count == 0) return "#161B22"; // No activity
            if (maxCount == 0) return "#0E4429"; // Default green
            
            var intensity = (double)count / maxCount;
            
            return intensity switch
            {
                < 0.25 => "#0E4429",  // Light green
                < 0.5 => "#006D32",   // Medium green
                < 0.75 => "#26A641",  // Bright green
                _ => "#39D353"       // Brightest green
            };
        }
        
        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:F1} {sizes[order]}";
        }
        
        private static bool TryParseLogLine(string line, out DateTime ts, out string level, out string msg)
        {
            ts = DateTime.MinValue; level = "INFO"; msg = line;
            try
            {
                if (!line.StartsWith("[")) return false;
                var p1 = line.IndexOf(']');
                if (p1 < 0) return false;
                var dateStr = line.Substring(1, p1 - 1);
                if (DateTime.TryParse(dateStr, out ts))
                {
                    var rest = line.Substring(p1 + 1).Trim();
                    var p2 = rest.IndexOf(']');
                    if (p2 > 0)
                    {
                        level = rest.Substring(0, p2);
                        msg = rest.Substring(p2 + 1).Trim();
                    }
                    return true;
                }
            }
            catch { }
            return false;
        }

        private async Task ShowEditProfileDialog()
        {
            var user = AuthService.CurrentUser;
            if (user == null) return;

            var txtEmail = new TextBox
            {
                Text = user.Email ?? string.Empty,
                Watermark = "e.g. you@example.com"
            };

            var txtBirthDate = new TextBox
            {
                Text = user.BirthDate ?? string.Empty,
                Watermark = "YYYY-MM-DD (e.g. 1990-05-15)"
            };

            var txtError = new TextBlock
            {
                Foreground = Avalonia.Media.Brush.Parse("#F38BA8"),
                FontSize = 11,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                IsVisible = false
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Classes = { "Secondary" },
                Padding = new Thickness(16, 8),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 0)
            };

            var btnSave = new Button
            {
                Content = "Save Changes",
                Classes = { "Primary" },
                Padding = new Thickness(16, 8),
                CornerRadius = new CornerRadius(8),
                FontWeight = FontWeight.Bold
            };

            var window = new Window
            {
                Title = "Edit Profile",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Background = Avalonia.Media.Brush.Parse("#1E1E2E"),
                SystemDecorations = SystemDecorations.Full
            };

            var content = new Border
            {
                Background = Avalonia.Media.Brush.Parse("#1E1E2E"),
                Padding = new Thickness(24)
            };

            var layout = new StackPanel { Spacing = 14 };
            layout.Children.Add(new TextBlock
            {
                Text = "Edit Profile Details",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = Avalonia.Media.Brushes.White
            });
            layout.Children.Add(new TextBlock
            {
                Text = "Set your email and birthday to enable account recovery and verification.",
                FontSize = 12,
                Foreground = Avalonia.Media.Brush.Parse("#A6ADC8"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            var emailPanel = new StackPanel { Spacing = 4 };
            emailPanel.Children.Add(new TextBlock { Text = "Email Address", Foreground = Avalonia.Media.Brush.Parse("#CDD6F4"), FontSize = 12, FontWeight = FontWeight.SemiBold });
            emailPanel.Children.Add(txtEmail);
            layout.Children.Add(emailPanel);

            var birthPanel = new StackPanel { Spacing = 4 };
            birthPanel.Children.Add(new TextBlock { Text = "Birthday (YYYY-MM-DD)", Foreground = Avalonia.Media.Brush.Parse("#CDD6F4"), FontSize = 12, FontWeight = FontWeight.SemiBold });
            birthPanel.Children.Add(txtBirthDate);
            layout.Children.Add(birthPanel);

            layout.Children.Add(txtError);

            var btnPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnSave);
            layout.Children.Add(btnPanel);

            content.Child = layout;
            window.Content = content;

            btnCancel.Click += (_, _) => window.Close();
            btnSave.Click += (_, _) =>
            {
                var email = txtEmail.Text?.Trim();
                var birthDate = txtBirthDate.Text?.Trim();

                if (!string.IsNullOrWhiteSpace(email) && (!email.Contains("@") || !email.Contains(".")))
                {
                    txtError.Text = "Please enter a valid email address.";
                    txtError.IsVisible = true;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(birthDate) && !DateTime.TryParse(birthDate, out _))
                {
                    txtError.Text = "Birthday format should be YYYY-MM-DD.";
                    txtError.IsVisible = true;
                    return;
                }

                var updated = AuthService.UpdateUserProfile(user.Id, email, birthDate);
                if (updated)
                {
                    window.Close();
                    UpdateProfileDisplay();
                    NotificationService.ShowBackupToast("Profile", "Profile details updated successfully!", "Success");
                }
                else
                {
                    txtError.Text = "Failed to update profile details.";
                    txtError.IsVisible = true;
                }
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            await window.ShowDialog(parentWindow!);
        }

        private async Task ShowResetUserDatabaseConfirmation()
        {
            var btnCancel = new Button
            {
                Content = "Cancel",
                Classes = { "Secondary" },
                Padding = new Thickness(16, 8),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 0)
            };

            var btnReset = new Button
            {
                Content = "RESET USER DATABASE",
                Classes = { "Danger" },
                Background = Avalonia.Media.Brush.Parse("#F38BA8"),
                Foreground = Avalonia.Media.Brushes.White,
                Padding = new Thickness(16, 8),
                CornerRadius = new CornerRadius(8),
                FontWeight = FontWeight.Bold
            };

            var window = new Window
            {
                Title = "Reset User Database",
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Background = Avalonia.Media.Brush.Parse("#1E1E2E"),
                SystemDecorations = SystemDecorations.Full
            };

            var content = new Border
            {
                Background = Avalonia.Media.Brush.Parse("#1E1E2E"),
                Padding = new Thickness(24)
            };

            var layout = new StackPanel { Spacing = 14 };
            layout.Children.Add(new TextBlock
            {
                Text = "Reset User Database",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = Avalonia.Media.Brush.Parse("#F38BA8")
            });
            layout.Children.Add(new TextBlock
            {
                Text = "Are you sure you want to reset the user database?\n\nThis will remove all local user accounts and restart the Initial Setup Wizard so you can set up everything fresh.\n\nA backup of the current database will be saved before reset.",
                FontSize = 13,
                Foreground = Avalonia.Media.Brush.Parse("#CDD6F4"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            var btnPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnReset);
            layout.Children.Add(btnPanel);

            content.Child = layout;
            window.Content = content;

            btnCancel.Click += (_, _) => window.Close();
            btnReset.Click += async (_, _) =>
            {
                btnReset.IsEnabled = false;
                btnReset.Content = "Resetting...";
                var (success, msg) = await AuthService.ResetUserDatabaseAsync();
                if (success)
                {
                    window.Close();
                    var parentWindow = TopLevel.GetTopLevel(this) as Window;

                    if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        var setupWizard = new SetupWizardWindow();
                        setupWizard.OnSetupComplete += () =>
                        {
                            var newMain = new MainWindow();
                            desktop.MainWindow = newMain;
                            newMain.Show();
                            setupWizard.Close();
                        };
                        desktop.MainWindow = setupWizard;
                        setupWizard.Show();
                    }

                    parentWindow?.Close();
                }
                else
                {
                    NotificationService.ShowBackupToast("Reset Error", msg, "Error");
                    btnReset.IsEnabled = true;
                    btnReset.Content = "RESET USER DATABASE";
                }
            };

            var parent = TopLevel.GetTopLevel(this) as Window;
            await window.ShowDialog(parent!);
        }
    }
}
