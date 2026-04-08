using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PinayPalBackupManager.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class UserManagementDialog : UserControl
    {
        public event EventHandler? OnClose;

        private Brush GetThemeResource(string key, Brush fallback)
        {
            try
            {
                var resource = Application.Current.FindResource(key);
                if (resource is Brush brush)
                    return brush;
                return fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public UserManagementDialog()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var btnRefresh = this.FindControl<Button>("BtnRefresh");
            var btnClose = this.FindControl<Button>("BtnClose");

            if (btnRefresh != null) btnRefresh.Click += (_, _) => RefreshUserList();
            if (btnClose != null) btnClose.Click += (_, _) => OnClose?.Invoke(this, EventArgs.Empty);

            RefreshUserList();
        }

        private void RefreshUserList()
        {
            var userListPanel = this.FindControl<StackPanel>("UserListPanel");
            var txtNoUsers = this.FindControl<TextBlock>("TxtNoUsers");
            if (userListPanel == null) return;

            userListPanel.Children.Clear();

            // Sync remote users from Firebase (fire-and-forget, don't block UI)
            _ = Task.Run(async () =>
            {
                try
                {
                    await AuthService.SyncRemoteUsersAsync();
                    // Refresh UI after sync
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => RefreshUserListUI());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UserManagementDialog] Failed to sync remote users: {ex.Message}");
                }
            });

            RefreshUserListUI();
        }

        private void RefreshUserListUI()
        {
            var userListPanel = this.FindControl<StackPanel>("UserListPanel");
            var txtNoUsers = this.FindControl<TextBlock>("TxtNoUsers");
            if (userListPanel == null) return;

            userListPanel.Children.Clear();

            var users = AuthService.GetAllUsers();
            var currentUser = AuthService.CurrentUser;

            if (txtNoUsers != null) txtNoUsers.IsVisible = users.Count == 0;

            foreach (var user in users)
            {
                // Skip deleted users
                if (user.Status == "Deleted")
                    continue;
                    
                var isCurrentUser = currentUser != null && user.Id == currentUser.Id;
                
                // Create card-style container for each user
                var userCard = new Border
                {
                    Background = GetThemeResource("AppCard", new SolidColorBrush(Color.FromRgb(128, 128, 128))),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    BorderBrush = GetThemeResource("AppBorder", new SolidColorBrush(Color.FromRgb(128, 128, 128))),
                    BorderThickness = new Thickness(1)
                };

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                // User info section
                var userInfo = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                
                var nameText = new TextBlock
                {
                    Text = user.Username + (isCurrentUser ? " (You)" : ""),
                    Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))),
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold
                };
                userInfo.Children.Add(nameText);

                var detailsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                
                var roleText = new TextBlock
                {
                    Text = user.Role,
                    Foreground = GetThemeResource("AccentWebsite", new SolidColorBrush(Color.FromRgb(255, 165, 0))),
                    FontSize = 11,
                    FontWeight = FontWeight.Medium
                };
                detailsRow.Children.Add(roleText);

                var statusColor = user.Status == "Active" ? "AccentFtp" : user.Status == "Disabled" ? "AppWarning" : "AccentSql";
                var statusText = new TextBlock
                {
                    Text = "• " + user.Status,
                    Foreground = GetThemeResource(statusColor, new SolidColorBrush(Color.FromRgb(128, 128, 128))),
                    FontSize = 11,
                    FontWeight = FontWeight.Medium
                };
                detailsRow.Children.Add(statusText);
                
                userInfo.Children.Add(detailsRow);
                Grid.SetColumn(userInfo, 0);
                row.Children.Add(userInfo);

                // Action buttons
                var buttonPanel = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

                // View Details button - available for all users when admin
                if (AuthService.IsAdmin)
                {
                    var viewUser = user;
                    var btnView = new Button { Content = "View Details", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = GetThemeResource("AppBorder", new SolidColorBrush(Color.FromRgb(128, 128, 128))), Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    btnView.Click += async (_, _) => await ShowUserDetailsDialog(viewUser);
                    buttonPanel.Children.Add(btnView);
                }

                if (AuthService.IsAdmin && !isCurrentUser)
                {
                    // Change Password button
                    var btnChangePassword = new Button { Content = "Change Password", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = GetThemeResource("AccentWebsite", new SolidColorBrush(Color.FromRgb(255, 165, 0))), Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    var targetUserId = user.Id;
                    var targetUsername = user.Username;
                    btnChangePassword.Click += async (_, _) => await ShowAdminChangePasswordDialog(targetUserId, targetUsername);
                    buttonPanel.Children.Add(btnChangePassword);

                    // Change Username button
                    var btnChangeUsername = new Button { Content = "Change Username", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = GetThemeResource("AccentMailchimp", new SolidColorBrush(Color.FromRgb(0, 255, 255))), Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    btnChangeUsername.Click += async (_, _) => await ShowAdminChangeUsernameDialog(targetUserId, targetUsername);
                    buttonPanel.Children.Add(btnChangeUsername);

                    if (user.Status == "Pending")
                    {
                        var btnApprove = new Button { Content = "Approve", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = GetThemeResource("AccentWebsite", new SolidColorBrush(Color.FromRgb(255, 165, 0))), Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnApprove.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User approved!", "Success"); };
                        buttonPanel.Children.Add(btnApprove);
                    }
                    else if (user.Status == "Active")
                    {
                        var btnDisable = new Button { Content = "Disable", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = GetThemeResource("AccentSql", new SolidColorBrush(Color.FromRgb(255, 255, 0))), Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnDisable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Disabled"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User disabled.", "Warning"); };
                        buttonPanel.Children.Add(btnDisable);
                    }
                    else if (user.Status == "Disabled")
                    {
                        var btnEnable = new Button { Content = "Enable", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = GetThemeResource("AccentFtp", new SolidColorBrush(Color.FromRgb(0, 255, 0))), Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnEnable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User enabled.", "Info"); };
                        buttonPanel.Children.Add(btnEnable);
                    }

                    var btnDelete = new Button { Content = "Delete", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = new SolidColorBrush(Color.FromRgb(255, 0, 0)), Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    var deleteId = user.Id;
                    var deleteUsername = user.Username;
                    btnDelete.Click += async (_, _) =>
                    {
                        var confirmed = await NotificationService.ConfirmAsync($"Are you sure you want to delete user '{deleteUsername}'?", "Confirm Delete");
                        if (confirmed)
                        {
                            await AuthService.DeleteUserAsync(deleteId);
                            RefreshUserList();
                            NotificationService.ShowBackupToast("Users", "User deleted.", "Warning");
                        }
                    };
                    buttonPanel.Children.Add(btnDelete);
                }

                Grid.SetColumn(buttonPanel, 1);
                row.Children.Add(buttonPanel);
                userCard.Child = row;
                userListPanel.Children.Add(userCard);
            }
        }

        private async Task ShowUserDetailsDialog(PinayPalBackupManager.Models.AppUser user)
        {
            const string dialogKey = "user_details";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var statusColor = user.Status switch
                {
                    "Active"   => "AccentFtp",
                    "Pending"  => "AccentSql",
                    "Disabled" => "AppWarning",
                    _          => "AccentWebsite"
                };

                var content = new StackPanel { Spacing = 0, Background = GetThemeResource("AppCard", new SolidColorBrush(Color.FromRgb(128, 128, 128))) };

                // Header
                var header = new Border
                {
                    Background = GetThemeResource("AppSurface", new SolidColorBrush(Color.FromRgb(64, 64, 64))),
                    Padding = new Thickness(24, 16),
                    Child = new TextBlock
                    {
                        Text = "User Details",
                        Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))),
                        FontSize = 16,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };
                content.Children.Add(header);

                // Details body
                var body = new StackPanel { Spacing = 12, Margin = new Thickness(24, 20) };

                void AddRow(string label, string value, string valueColor = "AppText")
                {
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(130)));
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                    var labelBlock = new TextBlock
                    {
                        Text = label + ":",
                        Foreground = GetThemeResource("AppMuted", new SolidColorBrush(Color.FromRgb(128, 128, 128))),
                        FontSize = 12,
                        FontWeight = FontWeight.Medium,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(labelBlock, 0);
                    row.Children.Add(labelBlock);

                    var valueBlock = new TextBlock
                    {
                        Text = value,
                        Foreground = GetThemeResource(valueColor, new SolidColorBrush(Color.FromRgb(255, 255, 255))),
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    };
                    Grid.SetColumn(valueBlock, 1);
                    row.Children.Add(valueBlock);

                    body.Children.Add(row);

                    body.Children.Add(new Border { Height = 1, Background = GetThemeResource("AppBorder", new SolidColorBrush(Color.FromRgb(128, 128, 128))), Margin = new Thickness(0, 4) });
                }

                AddRow("User ID:",       $"#{user.Id}");
                AddRow("Username:",      user.Username);
                AddRow("Role:",          user.Role, "AccentWebsite");
                AddRow("Status:",        user.Status, statusColor);
                AddRow("Member Since:",  user.CreatedAt.ToString("MMM dd, yyyy  hh:mm tt") + " UTC");
                AddRow("Password:",      "••••••••  (secured — cannot be displayed)", "AppMuted");

                content.Children.Add(body);

                // Close button
                var btnClose = new Button
                {
                    Content = "Close",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20),
                    Padding = new Thickness(40, 10),
                    Background = GetThemeResource("AppBorder", new SolidColorBrush(Color.FromRgb(128, 128, 128))),
                    Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))),
                    CornerRadius = new CornerRadius(8),
                    FontWeight = FontWeight.SemiBold
                };

                content.Children.Add(btnClose);

                var window = new Window
                {
                    Content = content,
                    Width = 460,
                    Height = 440,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = GetThemeResource("AppCard", new SolidColorBrush(Color.FromRgb(128, 128, 128))),
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0,
                };

                btnClose.Click += (_, _) => window.Close();

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                if (parentWindow != null)
                    await window.ShowDialog(parentWindow);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async Task ShowAdminChangePasswordDialog(int userId, string username)
        {
            const string dialogKey = "admin_change_password";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                Window? window = null;

                var root = new StackPanel { Spacing = 0, Background = GetThemeResource("AppCard", new SolidColorBrush(Color.FromRgb(36, 41, 56))) };

                // Header
                root.Children.Add(new Border
                {
                    Background = GetThemeResource("AppSurface", new SolidColorBrush(Color.FromRgb(64, 64, 64))),
                    Padding = new Thickness(24, 18),
                    Child = new StackPanel { Spacing = 4, Children =
                    {
                        new TextBlock { Text = "Change Password", Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))), FontSize = 18, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = $"for user: {username}", Foreground = GetThemeResource("AccentWebsite", new SolidColorBrush(Color.FromRgb(255, 165, 0))), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center }
                    }}
                });

                // Body
                var body = new StackPanel { Spacing = 8, Margin = new Thickness(28, 20, 28, 24) };

                body.Children.Add(new TextBlock { Text = "New Password", Foreground = GetThemeResource("AppSubtext", new SolidColorBrush(Color.FromRgb(128, 128, 128))), FontSize = 12 });
                var txtNew = new TextBox { PasswordChar = '●', Background = GetThemeResource("AppBorder", new SolidColorBrush(Color.FromRgb(128, 128, 128))), Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10) };
                body.Children.Add(txtNew);

                body.Children.Add(new TextBlock { Text = "Confirm New Password", Foreground = GetThemeResource("AppSubtext", new SolidColorBrush(Color.FromRgb(128, 128, 128))), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
                var txtConfirm = new TextBox { PasswordChar = '●', Background = GetThemeResource("AppBorder", new SolidColorBrush(Color.FromRgb(128, 128, 128))), Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10) };
                body.Children.Add(txtConfirm);

                var txtError = new TextBlock { Foreground = GetThemeResource("AppWarning", new SolidColorBrush(Color.FromRgb(255, 140, 0))), FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
                body.Children.Add(txtError);

                // Buttons
                var btnCancel = new Button { Content = "Cancel", Background = GetThemeResource("AppBorder", new SolidColorBrush(Color.FromRgb(128, 128, 128))), Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))), Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(8), FontWeight = FontWeight.SemiBold };
                var btnChange = new Button { Content = "Change Password", Background = GetThemeResource("AccentWebsite", new SolidColorBrush(Color.FromRgb(255, 165, 0))), Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)), Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(8), FontWeight = FontWeight.Bold };
                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
                btnRow.Children.Add(btnCancel);
                btnRow.Children.Add(btnChange);
                body.Children.Add(btnRow);

                root.Children.Add(body);

                window = new Window
                {
                    Content = root,
                    Width = 420,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = GetThemeResource("AppCard", new SolidColorBrush(Color.FromRgb(36, 41, 56))),
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0,
                };

                btnCancel.Click += (_, _) => window.Close();
                btnChange.Click += (_, _) =>
                {
                    var newPass = txtNew.Text ?? "";
                    var confirm = txtConfirm.Text ?? "";
                    if (string.IsNullOrWhiteSpace(newPass)) { txtError.Text = "Please enter a new password."; txtError.IsVisible = true; return; }
                    if (newPass != confirm) { txtError.Text = "Passwords do not match."; txtError.IsVisible = true; return; }
                    if (newPass.Length < 4) { txtError.Text = "Password must be at least 4 characters."; txtError.IsVisible = true; return; }

                    var changed = AuthService.ChangePassword(userId, newPass);
                    if (changed)
                    {
                        window.Close();
                        NotificationService.ShowBackupToast("Users", $"Password changed for {username}", "Success");
                    }
                    else
                    {
                        txtError.Text = "Failed to change password.";
                        txtError.IsVisible = true;
                    }
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                if (parentWindow != null)
                    await window.ShowDialog(parentWindow);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }

        private async Task ShowAdminChangeUsernameDialog(int userId, string currentUsername)
        {
            const string dialogKey = "admin_change_username";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                Window? window = null;

                var root = new StackPanel { Spacing = 0, Background = GetThemeResource("AppCard", new SolidColorBrush(Color.FromRgb(128, 128, 128))) };

                // Header
                root.Children.Add(new Border
                {
                    Background = GetThemeResource("AppSurface", new SolidColorBrush(Color.FromRgb(64, 64, 64))),
                    Padding = new Thickness(24, 18),
                    Child = new StackPanel { Spacing = 4, Children =
                    {
                        new TextBlock { Text = "Change Username", Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))), FontSize = 18, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = $"Current: {currentUsername}", Foreground = GetThemeResource("AccentWebsite", new SolidColorBrush(Color.FromRgb(255, 165, 0))), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center }
                    }}
                });

                // Body
                var body = new StackPanel { Spacing = 8, Margin = new Thickness(28, 20, 28, 24) };

                body.Children.Add(new TextBlock { Text = "New Username", Foreground = GetThemeResource("AppSubtext", new SolidColorBrush(Color.FromRgb(128, 128, 128))), FontSize = 12 });
                var txtNew = new TextBox { Background = GetThemeResource("AppBorder", new SolidColorBrush(Color.FromRgb(128, 128, 128))), Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10) };
                body.Children.Add(txtNew);

                var txtError = new TextBlock { Foreground = GetThemeResource("AppWarning", new SolidColorBrush(Color.FromRgb(255, 140, 0))), FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
                body.Children.Add(txtError);

                // Buttons
                var btnCancel = new Button { Content = "Cancel", Background = GetThemeResource("AppBorder", new SolidColorBrush(Color.FromRgb(128, 128, 128))), Foreground = GetThemeResource("AppText", new SolidColorBrush(Color.FromRgb(255, 255, 255))), Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(8), FontWeight = FontWeight.SemiBold };
                var btnChange = new Button { Content = "Change Username", Background = GetThemeResource("AccentMailchimp", new SolidColorBrush(Color.FromRgb(0, 255, 255))), Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)), Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(8), FontWeight = FontWeight.Bold };
                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
                btnRow.Children.Add(btnCancel);
                btnRow.Children.Add(btnChange);
                body.Children.Add(btnRow);

                root.Children.Add(body);

                window = new Window
                {
                    Content = root,
                    Width = 420,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = GetThemeResource("AppCard", new SolidColorBrush(Color.FromRgb(36, 41, 56))),
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0,
                };

                btnCancel.Click += (_, _) => window.Close();
                btnChange.Click += (_, _) =>
                {
                    var newUsername = (txtNew.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(newUsername)) { txtError.Text = "Please enter a new username."; txtError.IsVisible = true; return; }
                    if (newUsername.Length < 3) { txtError.Text = "Username must be at least 3 characters."; txtError.IsVisible = true; return; }

                    var changed = AuthService.ChangeUsername(userId, newUsername);
                    if (changed)
                    {
                        window.Close();
                        RefreshUserList();
                        NotificationService.ShowBackupToast("Users", $"Username changed to {newUsername}", "Success");
                    }
                    else
                    {
                        txtError.Text = "Failed to change username.";
                        txtError.IsVisible = true;
                    }
                };

                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                if (parentWindow != null)
                    await window.ShowDialog(parentWindow);
            }
            finally
            {
                NotificationService.UnregisterDialog(dialogKey);
            }
        }
    }
}
