using Avalonia.Controls;
using Avalonia.Interactivity;
using PinayPalBackupManager.Services;
using System;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ChangePasswordDialog : UserControl
    {
        public event EventHandler? OnPasswordChanged;
        public event EventHandler? OnCancel;

        public ChangePasswordDialog()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnChange = this.FindControl<Button>("BtnChange");
            var txtCurrent = this.FindControl<TextBox>("TxtCurrentPassword");
            var txtNew = this.FindControl<TextBox>("TxtNewPassword");
            var txtConfirm = this.FindControl<TextBox>("TxtConfirmPassword");
            var txtError = this.FindControl<TextBlock>("TxtError");

            btnCancel!.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            
            btnChange!.Click += (s, e) =>
            {
                var current = txtCurrent!.Text ?? "";
                var newPass = txtNew!.Text ?? "";
                var confirm = txtConfirm!.Text ?? "";

                if (string.IsNullOrWhiteSpace(current))
                {
                    txtError!.Text = "Please enter your current password.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(newPass))
                {
                    txtError!.Text = "Please enter a new password.";
                    return;
                }

                if (newPass != confirm)
                {
                    txtError!.Text = "New passwords do not match.";
                    return;
                }

                if (newPass.Length < 6)
                {
                    txtError!.Text = "Password must be at least 6 characters.";
                    return;
                }

                // Verify current password and change
                var user = AuthService.CurrentUser;
                if (user == null)
                {
                    txtError!.Text = "Not logged in.";
                    return;
                }

                // Try to login with current password to verify
                var (success, _) = AuthService.Login(user.Username, current);
                if (!success)
                {
                    txtError!.Text = "Current password is incorrect.";
                    return;
                }

                // Change password
                var changed = AuthService.ChangePassword(user.Id, newPass);
                if (changed)
                {
                    OnPasswordChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    txtError!.Text = "Failed to change password.";
                }
            };
        }
    }
}
