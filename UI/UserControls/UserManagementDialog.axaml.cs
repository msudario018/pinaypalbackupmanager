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
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

                var statusColor = user.Status == "Active" ? "#A6E3A1" : user.Status == "Disabled" ? "#F38BA8" : "#F9E2AF";
                var youMarker = isCurrentUser ? " (You)" : "";
                row.Children.Add(new TextBlock
                {
                    Text = $"{user.Username}{youMarker} — {user.Role} — {user.Status}",
                    Foreground = Brush.Parse(statusColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    Width = 240
                });

                if (AuthService.IsAdmin && !isCurrentUser)
                {
                    if (user.Status == "Pending")
                    {
                        var btnApprove = new Button { Content = "Approve", FontSize = 10, Padding = new Thickness(8, 4), Background = Brush.Parse("#89B4FA"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6) };
                        var uid = user.Id;
                        btnApprove.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User approved!", "Success"); };
                        row.Children.Add(btnApprove);
                    }
                    else if (user.Status == "Active")
                    {
                        var btnDisable = new Button { Content = "Disable", FontSize = 10, Padding = new Thickness(8, 4), Background = Brush.Parse("#F9E2AF"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6) };
                        var uid = user.Id;
                        btnDisable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Disabled"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User disabled.", "Warning"); };
                        row.Children.Add(btnDisable);
                    }
                    else if (user.Status == "Disabled")
                    {
                        var btnEnable = new Button { Content = "Enable", FontSize = 10, Padding = new Thickness(8, 4), Background = Brush.Parse("#A6E3A1"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6) };
                        var uid = user.Id;
                        btnEnable.Click += async (_, _) => { await AuthService.SetUserStatusAsync(uid, "Active"); RefreshUserList(); NotificationService.ShowBackupToast("Users", "User enabled.", "Info"); };
                        row.Children.Add(btnEnable);
                    }

                    var btnDelete = new Button { Content = "Delete", FontSize = 10, Padding = new Thickness(8, 4), Background = Brush.Parse("#F38BA8"), Foreground = Brush.Parse("#0B0F17"), CornerRadius = new CornerRadius(6) };
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
                    row.Children.Add(btnDelete);
                }

                userListPanel.Children.Add(row);
            }
        }
    }
}
