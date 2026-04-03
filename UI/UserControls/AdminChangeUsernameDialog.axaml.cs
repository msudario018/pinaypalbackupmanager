using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class AdminChangeUsernameDialog : UserControl
    {
        public event EventHandler<string>? OnUsernameChanged;
        public event EventHandler? OnCancel;

        public AdminChangeUsernameDialog() : this("") { }

        public AdminChangeUsernameDialog(string currentUsername)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var txtCurrent = this.FindControl<TextBlock>("TxtCurrent");
            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnChange = this.FindControl<Button>("BtnChange");
            var txtNew = this.FindControl<TextBox>("TxtNewUsername");
            var txtError = this.FindControl<TextBlock>("TxtError");

            txtCurrent!.Text = $"Current: {currentUsername}";

            btnCancel!.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            
            btnChange!.Click += (s, e) =>
            {
                var newUsername = txtNew!.Text ?? "";

                if (string.IsNullOrWhiteSpace(newUsername))
                {
                    txtError!.Text = "Please enter a new username.";
                    txtError.IsVisible = true;
                    return;
                }

                if (newUsername.Length < 3)
                {
                    txtError!.Text = "Username must be at least 3 characters.";
                    txtError.IsVisible = true;
                    return;
                }

                OnUsernameChanged?.Invoke(this, newUsername.Trim());
            };
        }
    }
}
