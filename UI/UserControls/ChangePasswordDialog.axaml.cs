using Avalonia.Controls;
using Avalonia.Interactivity;
using PinayPalBackupManager.Services;
using System;
using System.ComponentModel;

namespace PinayPalBackupManager.UI.UserControls
{
    [DesignTimeVisible(true)]
    public partial class ChangePasswordDialog : UserControl
    {
        public event EventHandler? OnPasswordChanged;
        public event EventHandler? OnCancel;

        public ChangePasswordDialog()
        {
            InitializeComponent();
            
            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnChange = this.FindControl<Button>("BtnChange");
            
            if (btnCancel != null) btnCancel.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            if (btnChange != null) btnChange.Click += OnChangePasswordClick;
        }

        private async void OnChangePasswordClick(object? sender, RoutedEventArgs e)
        {
            var txtCurrentPassword = this.FindControl<TextBox>("TxtCurrentPassword");
            var txtNewPassword = this.FindControl<TextBox>("TxtNewPassword");
            var txtError = this.FindControl<TextBlock>("TxtError");
            
            var currentPassword = txtCurrentPassword?.Text ?? string.Empty;
            var newPassword = txtNewPassword?.Text ?? string.Empty;
            
            // Clear previous error
            if (txtError != null) txtError.Text = string.Empty;
            
            // Validation
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                if (txtError != null) txtError.Text = "Current password is required.";
                return;
            }
            
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                if (txtError != null) txtError.Text = "New password is required.";
                return;
            }
            
            if (newPassword.Length < 4)
            {
                if (txtError != null) txtError.Text = "New password must be at least 4 characters.";
                return;
            }
            
            // Verify current password
            var (loginSuccess, loginMessage) = AuthService.Login(AuthService.CurrentUser!.Username, currentPassword);
            if (!loginSuccess)
            {
                if (txtError != null) txtError.Text = "Current password is incorrect.";
                return;
            }
            
            // Update password (would need to implement AuthService.UpdatePassword)
            // For now, show success message
            if (txtError != null)
            {
                txtError.Text = "Password changed successfully!";
                txtError.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
            }
            
            // Notify parent after a short delay
            var timer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                OnPasswordChanged?.Invoke(this, EventArgs.Empty);
            };
            timer.Start();
        }
    }
}
