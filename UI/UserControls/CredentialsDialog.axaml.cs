using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class CredentialsDialog : UserControl
    {
        public event EventHandler? OnSave;
        public event EventHandler? OnCancel;

        public CredentialsDialog() : this(ConfigService.Current) { }

        public CredentialsDialog(AppSettings settings)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            var txtSharedHost = this.FindControl<TextBox>("TxtSharedHost");
            var txtSharedTls = this.FindControl<TextBox>("TxtSharedTls");
            var txtFtpUser = this.FindControl<TextBox>("TxtFtpUser");
            var txtFtpPassword = this.FindControl<TextBox>("TxtFtpPassword");
            var txtFtpPort = this.FindControl<TextBox>("TxtFtpPort");
            var txtSqlUser = this.FindControl<TextBox>("TxtSqlUser");
            var txtSqlPassword = this.FindControl<TextBox>("TxtSqlPassword");
            var txtMcApiKey = this.FindControl<TextBox>("TxtMcApiKey");
            var txtMcAudienceId = this.FindControl<TextBox>("TxtMcAudienceId");
            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnSave = this.FindControl<Button>("BtnSave");

            // Load values
            txtSharedHost!.Text = settings.Ftp.Host;
            txtSharedTls!.Text = settings.Ftp.TlsFingerprint;
            txtFtpUser!.Text = settings.Ftp.User;
            txtFtpPassword!.Text = settings.Ftp.Password;
            txtFtpPort!.Text = settings.Ftp.Port.ToString();
            txtSqlUser!.Text = settings.Sql.User;
            txtSqlPassword!.Text = settings.Sql.Password;
            txtMcApiKey!.Text = settings.Mailchimp.ApiKey;
            txtMcAudienceId!.Text = settings.Mailchimp.AudienceId;

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

            int GetPortOrDefault(TextBox? tb, int defaultPort)
            {
                if (int.TryParse(tb?.Text, out var port) && port > 0)
                    return port;
                return defaultPort;
            }

            return new AppSettings
            {
                Paths = current.Paths,
                Ftp = new FtpSettings
                {
                    Host = GetOrPreserve(this.FindControl<TextBox>("TxtSharedHost"), current.Ftp.Host),
                    User = GetOrPreserve(this.FindControl<TextBox>("TxtFtpUser"), current.Ftp.User),
                    Password = GetOrPreserve(this.FindControl<TextBox>("TxtFtpPassword"), current.Ftp.Password),
                    TlsFingerprint = GetOrPreserve(this.FindControl<TextBox>("TxtSharedTls"), current.Ftp.TlsFingerprint),
                    Port = GetPortOrDefault(this.FindControl<TextBox>("TxtFtpPort"), current.Ftp.Port)
                },
                Sql = new SqlSettings
                {
                    Host = GetOrPreserve(this.FindControl<TextBox>("TxtSharedHost"), current.Sql.Host),
                    User = GetOrPreserve(this.FindControl<TextBox>("TxtSqlUser"), current.Sql.User),
                    Password = GetOrPreserve(this.FindControl<TextBox>("TxtSqlPassword"), current.Sql.Password),
                    RemotePath = "/public_html/mysql_staged",
                    TlsFingerprint = GetOrPreserve(this.FindControl<TextBox>("TxtSharedTls"), current.Sql.TlsFingerprint),
                },
                Mailchimp = new MailchimpSettings
                {
                    ApiKey = GetOrPreserve(this.FindControl<TextBox>("TxtMcApiKey"), current.Mailchimp.ApiKey),
                    AudienceId = GetOrPreserve(this.FindControl<TextBox>("TxtMcAudienceId"), current.Mailchimp.AudienceId),
                },
                Schedule = current.Schedule
            };
        }
    }
}
