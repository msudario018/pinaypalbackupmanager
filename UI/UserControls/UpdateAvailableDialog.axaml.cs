using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class UpdateAvailableDialog : UserControl
    {
        public event EventHandler? OnYes;
        public event EventHandler? OnNo;

        public UpdateAvailableDialog(string version, string changelog)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var txtVersion = this.FindControl<TextBlock>("TxtVersion");
            var txtChangelog = this.FindControl<TextBlock>("TxtChangelog");
            var btnYes = this.FindControl<Button>("BtnYes");
            var btnNo = this.FindControl<Button>("BtnNo");

            if (txtVersion != null) txtVersion.Text = $"Update available: {version}";
            if (txtChangelog != null) txtChangelog.Text = changelog;
            if (btnYes != null) btnYes.Click += (s, e) => OnYes?.Invoke(this, EventArgs.Empty);
            if (btnNo != null) btnNo.Click += (s, e) => OnNo?.Invoke(this, EventArgs.Empty);
        }
    }
}
