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

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

                // User info section
                var userInfo = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                
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
                row.Children.Add(userInfo);

                // Spacer
                row.Children.Add(new Border { Width = 1, MinWidth = 50 });

                // Action buttons
                var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

                if (AuthService.IsAdmin && !isCurrentUser)
                {
                    if (user.Status == "Pending")
                    {
                        var btnApprove = new Button { Content = "Approve", FontSize = 11, Padding = new Thickness(12, 6), Background = Brush.Parse("#89B4FA"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnApprove.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User approved!", "Success"); };
                        buttonPanel.Children.Add(btnApprove);
                    }
                    else if (user.Status == "Active")
                    {
                        var btnDisable = new Button { Content = "Disable", FontSize = 11, Padding = new Thickness(12, 6), Background = Brush.Parse("#F9E2AF"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnDisable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Disabled"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User disabled.", "Warning"); };
                        buttonPanel.Children.Add(btnDisable);
                    }
                    else if (user.Status == "Disabled")
                    {
                        var btnEnable = new Button { Content = "Enable", FontSize = 11, Padding = new Thickness(12, 6), Background = Brush.Parse("#A6E3A1"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                        var uid = user.Id;
                        btnEnable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User enabled.", "Info"); };
                        buttonPanel.Children.Add(btnEnable);
                    }

                    var btnDelete = new Button { Content = "Delete", FontSize = 11, Padding = new Thickness(12, 6), Background = Brush.Parse("#F38BA8"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6), FontWeight = FontWeight.SemiBold };
                    var deleteId = user.Id;
                    var deleteUsername = user.Username;
                    btnDelete.Click += async (_, _) =>
                    {
                        var confirmed = await NotificationService.ConfirmAsync($"Are you sure you want to delete user '{deleteUsername}'?", "Confirm Delete");
                        if (confirmed)
                        {
                            AuthService.DeleteUser(deleteId);
                            RefreshUserList();
                            NotificationService.ShowBackupToast("Users", "User deleted.", "Warning");
                        }
                    };
                    buttonPanel.Children.Add(btnDelete);
                }

                row.Children.Add(buttonPanel);
                userCard.Child = row;
                userListPanel.Children.Add(userCard);
            }
        }
    }
}
