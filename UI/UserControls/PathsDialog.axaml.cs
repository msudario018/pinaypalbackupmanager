using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
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

            var txtFtpLocalFolder = this.FindControl<TextBox>("TxtFtpLocalFolder");
            var txtMailchimpFolder = this.FindControl<TextBox>("TxtMailchimpFolder");
            var txtSqlLocalFolder = this.FindControl<TextBox>("TxtSqlLocalFolder");
            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnSave = this.FindControl<Button>("BtnSave");

            // Load values
            txtFtpLocalFolder!.Text = settings.Paths.FtpLocalFolder;
            txtMailchimpFolder!.Text = settings.Paths.MailchimpFolder;
            txtSqlLocalFolder!.Text = settings.Paths.SqlLocalFolder;

            btnCancel!.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            
            btnSave!.Click += (s, e) =>
            {
                OnSave?.Invoke(this, EventArgs.Empty);
            };
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
