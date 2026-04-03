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

            // Set values for hidden fields (pre-configured)
            txtSharedHost!.Text = "148.72.60.215";
            txtSharedTls!.Text = "SHA256:72:87:45:50:b8:1e:bd:75:10:f8:87:a0:03:2f:4d:f5:3b:41:7a:8e";
            txtFtpUser!.Text = "backupuser@pinaypal.net";
            txtFtpPort!.Text = "21";
            txtSqlUser!.Text = "z2fe7z1ysonk";
            txtMcAudienceId!.Text = "714bb33d4c";
            
            // Clear visible fields for user to fill
            txtFtpPassword!.Text = "";
            txtSqlPassword!.Text = "";
            txtMcApiKey!.Text = "";

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
