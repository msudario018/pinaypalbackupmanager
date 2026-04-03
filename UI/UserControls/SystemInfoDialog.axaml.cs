using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class SystemInfoDialog : UserControl
    {
        public event EventHandler? OnOk;

        public SystemInfoDialog() : this("", "")
        {
        }

        public SystemInfoDialog(string buildInfo, string changelog)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var txtBuildInfo = this.FindControl<TextBlock>("TxtBuildInfo");
            var txtChangelog = this.FindControl<TextBlock>("TxtChangelog");
            var btnOk = this.FindControl<Button>("BtnOk");

            if (txtBuildInfo != null) txtBuildInfo.Text = buildInfo;
            if (txtChangelog != null) txtChangelog.Text = changelog;
            if (btnOk != null) btnOk.Click += (s, e) => OnOk?.Invoke(this, EventArgs.Empty);
        }
    }
}
