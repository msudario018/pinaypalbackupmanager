using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class AdminChangePasswordDialog : UserControl
    {
        public event EventHandler<string>? OnPasswordChanged;
        public event EventHandler? OnCancel;

        public AdminChangePasswordDialog() : this("") { }

        public AdminChangePasswordDialog(string username)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var txtTitle = this.FindControl<TextBlock>("TxtTitle");
            var txtForUser = this.FindControl<TextBlock>("TxtForUser");
            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnChange = this.FindControl<Button>("BtnChange");
            var txtNew = this.FindControl<TextBox>("TxtNewPassword");
            var txtConfirm = this.FindControl<TextBox>("TxtConfirmPassword");
            var txtError = this.FindControl<TextBlock>("TxtError");

            txtForUser!.Text = $"for user: {username}";

            btnCancel!.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            
            btnChange!.Click += (s, e) =>
            {
                var newPass = txtNew!.Text ?? "";
                var confirm = txtConfirm!.Text ?? "";

                if (string.IsNullOrWhiteSpace(newPass))
                {
                    txtError!.Text = "Please enter a new password.";
                    txtError.IsVisible = true;
                    return;
                }

                if (newPass != confirm)
                {
                    txtError!.Text = "Passwords do not match.";
                    txtError.IsVisible = true;
                    return;
                }

                if (newPass.Length < 4)
                {
                    txtError!.Text = "Password must be at least 4 characters.";
                    txtError.IsVisible = true;
                    return;
                }

                OnPasswordChanged?.Invoke(this, newPass);
            };
        }
    }
}
