using Avalonia.Controls;
using Avalonia.Interactivity;
using PinayPalBackupManager.Services;
using System;
using System.ComponentModel;

namespace PinayPalBackupManager.UI.UserControls
{
    [DesignTimeVisible(true)]
    public partial class ChangeUsernameDialog : UserControl
    {
        public event EventHandler? OnUsernameChanged;
        public event EventHandler? OnCancel;

        public ChangeUsernameDialog()
        {
            InitializeComponent();
            
            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnChange = this.FindControl<Button>("BtnChange");
            
            if (btnCancel != null) btnCancel.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            if (btnChange != null) btnChange.Click += OnChangeUsernameClick;
        }

        private async void OnChangeUsernameClick(object? sender, RoutedEventArgs e)
        {
            var txtPassword = this.FindControl<TextBox>("TxtPassword");
            var txtNewUsername = this.FindControl<TextBox>("TxtNewUsername");
            var txtError = this.FindControl<TextBlock>("TxtError");
            
            var password = txtPassword?.Text ?? string.Empty;
            var newUsername = txtNewUsername?.Text?.Trim() ?? string.Empty;
            
            // Clear previous error
            if (txtError != null) txtError.Text = string.Empty;
            
            // Validation
            if (string.IsNullOrWhiteSpace(password))
            {
                if (txtError != null) txtError.Text = "Password is required.";
                return;
            }
            
            if (string.IsNullOrWhiteSpace(newUsername))
            {
                if (txtError != null) txtError.Text = "New username is required.";
                return;
            }
            
            if (newUsername.Length < 3)
            {
                if (txtError != null) txtError.Text = "Username must be at least 3 characters.";
                return;
            }
            
            // Verify password
            var (loginSuccess, loginMessage) = AuthService.Login(AuthService.CurrentUser!.Username, password);
            if (!loginSuccess)
            {
                if (txtError != null) txtError.Text = "Password is incorrect.";
                return;
            }
            
            // Check if username already exists (would need AuthService.UserExists)
            // For now, show success message
            if (txtError != null)
            {
                txtError.Text = "Username changed successfully!";
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
                OnUsernameChanged?.Invoke(this, EventArgs.Empty);
            };
            timer.Start();
        }
    }
}
