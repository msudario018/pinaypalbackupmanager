using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class UpdateAvailableDialog : UserControl
    {
        public event EventHandler? OnYes;
        public event EventHandler? OnNo;

        private Grid? _pnlActions;
        private StackPanel? _pnlProgress;
        private ProgressBar? _prgDownload;
        private TextBlock? _txtProgressStatus;
        private TextBlock? _txtProgressPercent;
        private Button? _btnYes;

        public UpdateAvailableDialog() : this("", "", true)
        {
        }

        public UpdateAvailableDialog(string version, string changelog, bool isInstalled = true)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var txtVersion = this.FindControl<TextBlock>("TxtVersion");
            var txtChangelog = this.FindControl<TextBlock>("TxtChangelog");
            _btnYes = this.FindControl<Button>("BtnYes");
            var btnNo = this.FindControl<Button>("BtnNo");

            _pnlActions = this.FindControl<Grid>("PnlActions");
            _pnlProgress = this.FindControl<StackPanel>("PnlProgress");
            _prgDownload = this.FindControl<ProgressBar>("PrgDownload");
            _txtProgressStatus = this.FindControl<TextBlock>("TxtProgressStatus");
            _txtProgressPercent = this.FindControl<TextBlock>("TxtProgressPercent");

            if (txtVersion != null) txtVersion.Text = string.IsNullOrWhiteSpace(version) ? "v3.7.2" : (version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : $"v{version}");
            if (txtChangelog != null) txtChangelog.Text = changelog;

            if (_btnYes != null)
            {
                if (!isInstalled)
                {
                    _btnYes.Content = "Open Release Page";
                }
                _btnYes.Click += (s, e) => OnYes?.Invoke(this, EventArgs.Empty);
            }

            if (btnNo != null) btnNo.Click += (s, e) => OnNo?.Invoke(this, EventArgs.Empty);
        }

        public void ShowProgressMode()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_pnlActions != null) _pnlActions.IsVisible = false;
                if (_pnlProgress != null) _pnlProgress.IsVisible = true;
            });
        }

        public void SetDownloadProgress(int percent, string? message = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_pnlActions != null && _pnlActions.IsVisible) _pnlActions.IsVisible = false;
                if (_pnlProgress != null && !_pnlProgress.IsVisible) _pnlProgress.IsVisible = true;
                if (_prgDownload != null) _prgDownload.Value = Math.Clamp(percent, 0, 100);
                if (_txtProgressPercent != null) _txtProgressPercent.Text = $"{percent}%";
                if (_txtProgressStatus != null) _txtProgressStatus.Text = message ?? $"Downloading update ({percent}%)... Please wait";
            });
        }

        public void SetInstallingMode()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_prgDownload != null)
                {
                    _prgDownload.IsIndeterminate = true;
                }
                if (_txtProgressPercent != null) _txtProgressPercent.Text = "Applying";
                if (_txtProgressStatus != null) _txtProgressStatus.Text = "Applying update & restarting PinayPal Backup Manager...";
            });
        }
    }
}
