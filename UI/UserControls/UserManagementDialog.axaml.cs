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
                    Background = Brush.Parse("#1E1E2E"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    BorderBrush = Brush.Parse("#45475A"),
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
                    Foreground = Brush.Parse("#CDD6F4"),
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold
                };
                userInfo.Children.Add(nameText);

                var detailsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                
                var roleText = new TextBlock
                {
                    Text = user.Role,
                    Foreground = Brush.Parse("#89B4FA"),
                    FontSize = 11,
                    FontWeight = FontWeight.Medium
                };
                detailsRow.Children.Add(roleText);

                var statusColor = user.Status == "Active" ? "#A6E3A1" : user.Status == "Disabled" ? "#F38BA8" : "#F9E2AF";
                var statusText = new TextBlock
                {
                    Text = "• " + user.Status,
                    Foreground = Brush.Parse(statusColor),
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
                    var btnView = new Button { Content = "View Details", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#313244"), Foreground = Brush.Parse("#CDD6F4"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    btnView.Click += async (_, _) => await ShowUserDetailsDialog(viewUser);
                    buttonPanel.Children.Add(btnView);
                }

                if (AuthService.IsAdmin && !isCurrentUser)
                {
                    // Change Password button
                    var btnChangePassword = new Button { Content = "Change Password", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#CBA6F7"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    var targetUserId = user.Id;
                    var targetUsername = user.Username;
                    btnChangePassword.Click += async (_, _) => await ShowAdminChangePasswordDialog(targetUserId, targetUsername);
                    buttonPanel.Children.Add(btnChangePassword);

                    // Change Username button
                    var btnChangeUsername = new Button { Content = "Change Username", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#89DCEB"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    btnChangeUsername.Click += async (_, _) => await ShowAdminChangeUsernameDialog(targetUserId, targetUsername);
                    buttonPanel.Children.Add(btnChangeUsername);

                    if (user.Status == "Pending")
                    {
                        var btnApprove = new Button { Content = "Approve", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#89B4FA"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnApprove.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User approved!", "Success"); };
                        buttonPanel.Children.Add(btnApprove);
                    }
                    else if (user.Status == "Active")
                    {
                        var btnDisable = new Button { Content = "Disable", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#F9E2AF"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnDisable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Disabled"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User disabled.", "Warning"); };
                        buttonPanel.Children.Add(btnDisable);
                    }
                    else if (user.Status == "Disabled")
                    {
                        var btnEnable = new Button { Content = "Enable", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#A6E3A1"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnEnable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User enabled.", "Info"); };
                        buttonPanel.Children.Add(btnEnable);
                    }

                    var btnDelete = new Button { Content = "Delete", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#F38BA8"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
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
                    "Active"   => "#A6E3A1",
                    "Pending"  => "#F9E2AF",
                    "Disabled" => "#F38BA8",
                    _          => "#A6ADC8"
                };

                var content = new StackPanel { Spacing = 0, Background = Brush.Parse("#1E1E2E") };

                // Header
                var header = new Border
                {
                    Background = Brush.Parse("#313244"),
                    Padding = new Thickness(24, 16),
                    Child = new TextBlock
                    {
                        Text = "User Details",
                        Foreground = Brush.Parse("#CDD6F4"),
                        FontSize = 16,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };
                content.Children.Add(header);

                // Details body
                var body = new StackPanel { Spacing = 12, Margin = new Thickness(24, 20) };

                void AddRow(string label, string value, string valueColor = "#CDD6F4")
                {
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(130)));
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                    var lbl = new TextBlock { Text = label, Foreground = Brush.Parse("#A6ADC8"), FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                    var val = new TextBlock { Text = value, Foreground = Brush.Parse(valueColor), FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

                    Grid.SetColumn(lbl, 0);
                    Grid.SetColumn(val, 1);
                    row.Children.Add(lbl);
                    row.Children.Add(val);
                    body.Children.Add(row);

                    body.Children.Add(new Border { Height = 1, Background = Brush.Parse("#313244"), Margin = new Thickness(0, 4) });
                }

                AddRow("User ID:",       $"#{user.Id}");
                AddRow("Username:",      user.Username);
                AddRow("Role:",          user.Role, user.Role == "Admin" ? "#CBA6F7" : "#89B4FA");
                AddRow("Status:",        user.Status, statusColor);
                AddRow("Member Since:",  user.CreatedAt.ToString("MMM dd, yyyy  hh:mm tt") + " UTC");
                AddRow("Password:",      "••••••••  (secured — cannot be displayed)", "#6C7086");

                content.Children.Add(body);

                // Close button
                var btnClose = new Button
                {
                    Content = "Close",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20),
                    Padding = new Thickness(40, 10),
                    Background = Brush.Parse("#45475A"),
                    Foreground = Brush.Parse("#CDD6F4"),
                    CornerRadius = new CornerRadius(8),
                    FontWeight = FontWeight.SemiBold
                };

                content.Children.Add(btnClose);

                var window = new Window
                {
                    Title = $"Details — {user.Username}",
                    Content = content,
                    Width = 440,
                    Height = 380,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Background = Brush.Parse("#1E1E2E")
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
                var dialog = new AdminChangePasswordDialog(username);
                var window = new Window
                {
                    Title = "Change User Password",
                    Content = dialog,
                    Width = 400,
                    Height = 320,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Background = Avalonia.Media.Brushes.Transparent
                };

                dialog.OnPasswordChanged += (sender, newPassword) =>
                {
                    var changed = AuthService.ChangePassword(userId, newPassword);
                    if (changed)
                    {
                        window.Close();
                        NotificationService.ShowBackupToast("Users", $"Password changed for {username}", "Success");
                    }
                    else
                    {
                        NotificationService.ShowBackupToast("Users", "Failed to change password", "Error");
                    }
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

        private async Task ShowAdminChangeUsernameDialog(int userId, string currentUsername)
        {
            const string dialogKey = "admin_change_username";
            if (NotificationService.IsDialogOpen(dialogKey)) return;
            
            NotificationService.RegisterDialog(dialogKey);
            try
            {
                var dialog = new AdminChangeUsernameDialog(currentUsername);
                var window = new Window
                {
                    Title = "Change Username",
                    Content = dialog,
                    Width = 400,
                    Height = 280,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Background = Avalonia.Media.Brushes.Transparent
                };

                dialog.OnUsernameChanged += (sender, newUsername) =>
                {
                    var changed = AuthService.ChangeUsername(userId, newUsername);
                    if (changed)
                    {
                        window.Close();
                        RefreshUserList();
                        NotificationService.ShowBackupToast("Users", $"Username changed to {newUsername}", "Success");
                    }
                    else
                    {
                        NotificationService.ShowBackupToast("Users", "Failed to change username", "Error");
                    }
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
