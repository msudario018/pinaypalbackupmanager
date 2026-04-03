using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class PathsDialog : UserControl
    {
        public event EventHandler? OnSave;
        public event EventHandler? OnCancel;

        public PathsDialog() : this(ConfigService.Current) { }

        public PathsDialog(AppSettings settings)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var txtFtpLocalFolder = this.FindControl<TextBox>("TxtFtpLocalFolder")!;
            var txtMailchimpFolder = this.FindControl<TextBox>("TxtMailchimpFolder")!;
            var txtSqlLocalFolder = this.FindControl<TextBox>("TxtSqlLocalFolder")!;

            // Load current values
            txtFtpLocalFolder.Text = settings.Paths.FtpLocalFolder;
            txtMailchimpFolder.Text = settings.Paths.MailchimpFolder;
            txtSqlLocalFolder.Text = settings.Paths.SqlLocalFolder;

            // Browse buttons
            this.FindControl<Button>("BtnBrowseFtp")!.Click += async (s, e) =>
                await BrowseFolderAsync(txtFtpLocalFolder, "Select FTP Backup Folder");

            this.FindControl<Button>("BtnBrowseMailchimp")!.Click += async (s, e) =>
                await BrowseFolderAsync(txtMailchimpFolder, "Select Mailchimp Backup Folder");

            this.FindControl<Button>("BtnBrowseSql")!.Click += async (s, e) =>
                await BrowseFolderAsync(txtSqlLocalFolder, "Select SQL Backup Folder");

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

            // Pre-open to current value if it exists
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
                    FtpLocalFolder = GetOrPreserve(this.FindControl<TextBox>("TxtFtpLocalFolder"), current.Paths.FtpLocalFolder),
                    MailchimpFolder = GetOrPreserve(this.FindControl<TextBox>("TxtMailchimpFolder"), current.Paths.MailchimpFolder),
                    SqlLocalFolder = GetOrPreserve(this.FindControl<TextBox>("TxtSqlLocalFolder"), current.Paths.SqlLocalFolder),
                },
                Ftp = current.Ftp,
                Sql = current.Sql,
                Mailchimp = current.Mailchimp,
                Schedule = current.Schedule
            };
        }
    }
}
