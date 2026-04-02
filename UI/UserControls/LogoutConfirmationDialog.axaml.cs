using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class LogoutConfirmationDialog : UserControl
    {
        public event EventHandler? OnLogoutConfirmed;
        public event EventHandler? OnCancel;

        public LogoutConfirmationDialog()
        {
            InitializeComponent();
            
            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnLogout = this.FindControl<Button>("BtnLogout");
            
            if (btnCancel != null)
            {
                btnCancel.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            }
            
            if (btnLogout != null)
            {
                btnLogout.Click += (s, e) => OnLogoutConfirmed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
