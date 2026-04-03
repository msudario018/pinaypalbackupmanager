using Avalonia.Controls;
using Avalonia.Interactivity;
using PinayPalBackupManager.Services;
using System;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ChangeUsernameDialog : UserControl
    {
        public event EventHandler? OnUsernameChanged;
        public event EventHandler? OnCancel;

        public ChangeUsernameDialog()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnChange = this.FindControl<Button>("BtnChange");
            var txtCurrent = this.FindControl<TextBlock>("TxtCurrentUsername");
            var txtNew = this.FindControl<TextBox>("TxtNewUsername");
            var txtError = this.FindControl<TextBlock>("TxtError");

            // Display current username
            var user = AuthService.CurrentUser;
            if (user != null)
            {
                txtCurrent!.Text = user.Username;
            }

            btnCancel!.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            
            btnChange!.Click += (s, e) =>
            {
                var newUsername = txtNew!.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(newUsername))
                {
                    txtError!.Text = "Please enter a new username.";
                    return;
                }

                if (newUsername.Length < 3)
                {
                    txtError!.Text = "Username must be at least 3 characters.";
                    return;
                }

                if (user == null)
                {
                    txtError!.Text = "Not logged in.";
                    return;
                }

                // Check if username already exists
                if (AuthService.GetUserByUsername(newUsername) != null)
                {
                    txtError!.Text = "Username already taken.";
                    return;
                }

                // Change username
                var changed = AuthService.ChangeUsername(user.Id, newUsername);
                if (changed)
                {
                    OnUsernameChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    txtError!.Text = "Failed to change username.";
                }
            };
        }
    }
}
