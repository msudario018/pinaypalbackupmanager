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
                bool isDark = Services.ThemeService.IsDark;
                string cardBg    = isDark ? "#1E1E2E" : "#E6E9EF";
                string cardBdr   = isDark ? "#6C7086" : "#BCC0CC";
                string textMain  = isDark ? "#CDD6F4" : "#4C4F69";
                string textLabel = isDark ? "#6C7086" : "#5C5F77";
                string btnBg     = isDark ? "#6C7086" : "#BCC0CC";

                var userCard = new Border
                {
                    Background = Brush.Parse(cardBg),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    BorderBrush = Brush.Parse(cardBdr),
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
                    Foreground = Brush.Parse(textMain),
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold
                };
                userInfo.Children.Add(nameText);

                var detailsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                
                var roleText = new TextBlock
                {
                    Text = user.Role,
                    Foreground = Brush.Parse("#00b4d8"),
                    FontSize = 11,
                    FontWeight = FontWeight.Medium
                };
                detailsRow.Children.Add(roleText);

                var statusColor = user.Status == "Active" ? "#588157" : user.Status == "Disabled" ? "#F38BA8" : "#e6c55c";
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
                    var btnView = new Button { Content = "View Details", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse(btnBg), Foreground = Brush.Parse(textMain), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    btnView.Click += async (_, _) => await ShowUserDetailsDialog(viewUser);
                    buttonPanel.Children.Add(btnView);
                }

                if (AuthService.IsAdmin && !isCurrentUser)
                {
                    // Change Password button
                    var btnChangePassword = new Button { Content = "Change Password", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#00b4d8"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    var targetUserId = user.Id;
                    var targetUsername = user.Username;
                    btnChangePassword.Click += async (_, _) => await ShowAdminChangePasswordDialog(targetUserId, targetUsername);
                    buttonPanel.Children.Add(btnChangePassword);

                    // Change Username button
                    var btnChangeUsername = new Button { Content = "Change Username", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#48a9c9"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    btnChangeUsername.Click += async (_, _) => await ShowAdminChangeUsernameDialog(targetUserId, targetUsername);
                    buttonPanel.Children.Add(btnChangeUsername);

                    if (user.Status == "Pending")
                    {
                        var btnApprove = new Button { Content = "Approve", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#588157"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnApprove.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User approved!", "Success"); };
                        buttonPanel.Children.Add(btnApprove);
                    }
                    else if (user.Status == "Active")
                    {
                        var btnDisable = new Button { Content = "Disable", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#F38BA8"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnDisable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Disabled"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User disabled.", "Warning"); };
                        buttonPanel.Children.Add(btnDisable);
                    }
                    else if (user.Status == "Disabled")
                    {
                        var btnEnable = new Button { Content = "Enable", FontSize = 11, Padding = new Thickness(12, 6), Margin = new Thickness(4), Background = Brush.Parse("#588157"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnEnable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User enabled.", "Info"); };
                        buttonPanel.Children.Add(btnEnable);
                    }

                    // Do not allow deleting Admin accounts
                    if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                    {
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
                    "Active"   => "#588157",
                    "Pending"  => "#e6c55c",
                    "Disabled" => "#F38BA8",
                    _          => "#6C7086"
                };

                bool detailDark  = Services.ThemeService.IsDark;
                string detailBg  = detailDark ? "#1E1E2E" : "#E6E9EF";
                string detailHdr = detailDark ? "#6C7086" : "#BCC0CC";
                string detailTxt = detailDark ? "#CDD6F4" : "#4C4F69";
                string detailLbl = detailDark ? "#6C7086" : "#5C5F77";
                string detailSep = detailDark ? "#6C7086" : "#BCC0CC";
                string detailMut = detailDark ? "#6C7086" : "#7C7F93";
                string detailBtn = detailDark ? "#6C7086" : "#BCC0CC";

                var content = new StackPanel { Spacing = 0, Background = Brush.Parse(detailBg) };

                // Header
                var header = new Border
                {
                    Background = Brush.Parse(detailHdr),
                    Padding = new Thickness(24, 16),
                    Child = new TextBlock
                    {
                        Text = "User Details",
                        Foreground = Brush.Parse(detailTxt),
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

                    var lbl = new TextBlock { Text = label, Foreground = Brush.Parse(detailLbl), FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                    var val = new TextBlock { Text = value, Foreground = Brush.Parse(valueColor == "#CDD6F4" ? detailTxt : valueColor == "#6C7086" ? detailMut : valueColor), FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

                    Grid.SetColumn(lbl, 0);
                    Grid.SetColumn(val, 1);
                    row.Children.Add(lbl);
                    row.Children.Add(val);
                    body.Children.Add(row);

                    body.Children.Add(new Border { Height = 1, Background = Brush.Parse(detailSep), Margin = new Thickness(0, 4) });
                }

                AddRow("User ID:",       $"#{user.Id}");
                AddRow("Username:",      user.Username);
                AddRow("Role:",          user.Role, user.Role == "Admin" ? "#00b4d8" : "#00b4d8");
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
                    Background = Brush.Parse(detailBtn),
                    Foreground = Brush.Parse(detailTxt),
                    CornerRadius = new CornerRadius(8),
                    FontWeight = FontWeight.SemiBold
                };

                content.Children.Add(btnClose);

                var window = new Window
                {
                    Title = $"Details — {user.Username}",
                    Content = content,
                    Width = 460,
                    Height = 440,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Brush.Parse(detailBg),
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0
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

                var root = new StackPanel { Spacing = 0, Background = Brush.Parse("#1E1E2E") };

                // Header
                root.Children.Add(new Border
                {
                    Background = Brush.Parse("#4C4F69"),
                    Padding = new Thickness(24, 18),
                    Child = new StackPanel { Spacing = 4, Children =
                    {
                        new TextBlock { Text = "Change Password", Foreground = Brush.Parse("#CDD6F4"), FontSize = 18, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = $"for user: {username}", Foreground = Brush.Parse("#6C7086"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center }
                    }}
                });

                // Body
                var body = new StackPanel { Spacing = 8, Margin = new Thickness(28, 20, 28, 24) };

                body.Children.Add(new TextBlock { Text = "New Password", Foreground = Brush.Parse("#6C7086"), FontSize = 12 });
                var txtNew = new TextBox { PasswordChar = '●', Background = Brush.Parse("#6C7086"), Foreground = Brush.Parse("#CDD6F4"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10) };
                body.Children.Add(txtNew);

                body.Children.Add(new TextBlock { Text = "Confirm New Password", Foreground = Brush.Parse("#6C7086"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
                var txtConfirm = new TextBox { PasswordChar = '●', Background = Brush.Parse("#6C7086"), Foreground = Brush.Parse("#CDD6F4"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10) };
                body.Children.Add(txtConfirm);

                var txtError = new TextBlock { Foreground = Brush.Parse("#F38BA8"), FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
                body.Children.Add(txtError);

                // Buttons
                var btnCancel = new Button { Content = "Cancel", Background = Brush.Parse("#6C7086"), Foreground = Brush.Parse("#CDD6F4"), Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(8), FontWeight = FontWeight.SemiBold };
                var btnChange = new Button { Content = "Change Password", Background = Brush.Parse("#00b4d8"), Foreground = Brush.Parse("#1E1E2E"), Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(8), FontWeight = FontWeight.Bold };
                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
                btnRow.Children.Add(btnCancel);
                btnRow.Children.Add(btnChange);
                body.Children.Add(btnRow);

                root.Children.Add(body);

                window = new Window
                {
                    Title = "Change User Password",
                    Content = root,
                    Width = 420,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Brush.Parse("#1E1E2E"),
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0
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

                var root = new StackPanel { Spacing = 0, Background = Brush.Parse("#1E1E2E") };

                // Header
                root.Children.Add(new Border
                {
                    Background = Brush.Parse("#6C7086"),
                    Padding = new Thickness(24, 18),
                    Child = new StackPanel { Spacing = 4, Children =
                    {
                        new TextBlock { Text = "Change Username", Foreground = Brush.Parse("#CDD6F4"), FontSize = 18, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = $"Current: {currentUsername}", Foreground = Brush.Parse("#6C7086"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center }
                    }}
                });

                // Body
                var body = new StackPanel { Spacing = 8, Margin = new Thickness(28, 20, 28, 24) };

                body.Children.Add(new TextBlock { Text = "New Username", Foreground = Brush.Parse("#6C7086"), FontSize = 12 });
                var txtNew = new TextBox { Background = Brush.Parse("#6C7086"), Foreground = Brush.Parse("#CDD6F4"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10) };
                body.Children.Add(txtNew);

                var txtError = new TextBlock { Foreground = Brush.Parse("#F38BA8"), FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
                body.Children.Add(txtError);

                // Buttons
                var btnCancel = new Button { Content = "Cancel", Background = Brush.Parse("#6C7086"), Foreground = Brush.Parse("#CDD6F4"), Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(8), FontWeight = FontWeight.SemiBold };
                var btnChange = new Button { Content = "Change Username", Background = Brush.Parse("#48a9c9"), Foreground = Brush.Parse("#1E1E2E"), Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(8), FontWeight = FontWeight.Bold };
                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
                btnRow.Children.Add(btnCancel);
                btnRow.Children.Add(btnChange);
                body.Children.Add(btnRow);

                root.Children.Add(body);

                window = new Window
                {
                    Title = "Change Username",
                    Content = root,
                    Width = 420,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Brush.Parse("#1E1E2E"),
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaTitleBarHeightHint = 0
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
