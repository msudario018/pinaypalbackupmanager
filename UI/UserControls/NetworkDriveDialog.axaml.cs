using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class NetworkDriveDialog : UserControl
    {
        public event EventHandler? OnSave;
        public event EventHandler? OnCancel;

        public NetworkDriveDialog() : this(ConfigService.Current) { }

        public NetworkDriveDialog(AppSettings settings)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var chkEnabled = this.FindControl<CheckBox>("ChkNetworkDriveEnabled")!;
            var txtPath = this.FindControl<TextBox>("TxtNetworkDrivePath")!;
            var txtUsername = this.FindControl<TextBox>("TxtNetworkDriveUsername")!;
            var txtPassword = this.FindControl<TextBox>("TxtNetworkDrivePassword")!;

            chkEnabled.IsChecked = settings.NetworkDrive.Enabled;
            txtPath.Text = settings.NetworkDrive.Path;
            txtUsername.Text = settings.NetworkDrive.Username;
            txtPassword.Text = settings.NetworkDrive.Password;

            this.FindControl<Button>("BtnBrowseNetworkDrive")!.Click += async (s, e) =>
                await BrowseFolderAsync(txtPath, "Select Network Drive Folder");

            this.FindControl<Button>("BtnCancel")!.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            this.FindControl<Button>("BtnSave")!.Click += (s, e) => OnSave?.Invoke(this, EventArgs.Empty);
        }

        private async Task BrowseFolderAsync(TextBox target, string title)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var options = new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            if (!string.IsNullOrWhiteSpace(target.Text))
            {
                try
                {
                    var existing = await topLevel.StorageProvider.TryGetFolderFromPathAsync(target.Text);
                    if (existing != null) options.SuggestedStartLocation = existing;
                }
                catch { }
            }

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
            if (folders.Count > 0)
            {
                var path = folders[0].TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path))
                    target.Text = path;
            }
        }

        public AppSettings GetSettings()
        {
            var current = ConfigService.Current;

            string GetOrPreserve(TextBox? tb, string currentVal) =>
                string.IsNullOrWhiteSpace(tb?.Text) ? currentVal : tb.Text;

            return new AppSettings
            {
                Paths = new PathsSettings
                {
                    FtpLocalFolder = current.Paths.FtpLocalFolder,
                    MailchimpFolder = current.Paths.MailchimpFolder,
                    SqlLocalFolder = current.Paths.SqlLocalFolder,
                    NetworkDriveFolder = GetOrPreserve(this.FindControl<TextBox>("TxtNetworkDrivePath"), current.Paths.NetworkDriveFolder),
                },
                Ftp = current.Ftp,
                Sql = current.Sql,
                Mailchimp = current.Mailchimp,
                NetworkDrive = new NetworkDriveSettings
                {
                    Enabled = this.FindControl<CheckBox>("ChkNetworkDriveEnabled")?.IsChecked ?? false,
                    Path = GetOrPreserve(this.FindControl<TextBox>("TxtNetworkDrivePath"), current.NetworkDrive.Path),
                    Username = GetOrPreserve(this.FindControl<TextBox>("TxtNetworkDriveUsername"), current.NetworkDrive.Username),
                    Password = GetOrPreserve(this.FindControl<TextBox>("TxtNetworkDrivePassword"), current.NetworkDrive.Password),
                },
                Schedule = current.Schedule,
                Operation = current.Operation,
                HttpServer = current.HttpServer
            };
        }
    }
}
