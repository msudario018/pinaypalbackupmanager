using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
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
            var btnExport = this.FindControl<Button>("BtnExport");
            var btnImport = this.FindControl<Button>("BtnImport");
            var txtStatus = this.FindControl<TextBlock>("TxtStatus");

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

            // Export/Import handlers
            btnExport!.Click += async (s, e) => await ExportCredentialsAsync(txtStatus);
            btnImport!.Click += async (s, e) => await ImportCredentialsAsync(txtStatus);
        }

        private async Task ExportCredentialsAsync(TextBlock? statusText)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Encrypted Credentials",
                    DefaultExtension = ".ppenc",
                    SuggestedFileName = "pinaypal_credentials",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Encrypted Credentials") { Patterns = new[] { "*.ppenc" } }
                    }
                });

                if (file == null) return;

                var settings = GetSettings();
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                var encrypted = EncryptString(json);
                
                await File.WriteAllTextAsync(file.Path.LocalPath, encrypted);
                
                if (statusText != null)
                {
                    statusText.Text = "Credentials exported successfully!";
                    statusText.Foreground = Avalonia.Media.Brush.Parse("#588157");
                }
                
                NotificationService.ShowBackupToast("Credentials", "Encrypted credentials exported successfully!", "Success");
            }
            catch (Exception ex)
            {
                if (statusText != null)
                {
                    statusText.Text = $"Export failed: {ex.Message}";
                    statusText.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                }
                NotificationService.ShowBackupToast("Credentials", $"Export failed: {ex.Message}", "Error");
            }
        }

        private async Task ImportCredentialsAsync(TextBlock? statusText)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Encrypted Credentials",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Encrypted Credentials") { Patterns = new[] { "*.ppenc" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count == 0) return;

                var file = files[0];
                var encrypted = await File.ReadAllTextAsync(file.Path.LocalPath);
                var json = DecryptString(encrypted);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                
                if (settings == null)
                {
                    throw new InvalidOperationException("Failed to deserialize credentials file.");
                }

                // Update UI with imported values
                this.FindControl<TextBox>("TxtSharedHost")!.Text = settings.Ftp.Host;
                this.FindControl<TextBox>("TxtSharedTls")!.Text = settings.Ftp.TlsFingerprint;
                this.FindControl<TextBox>("TxtFtpUser")!.Text = settings.Ftp.User;
                this.FindControl<TextBox>("TxtFtpPassword")!.Text = settings.Ftp.Password;
                this.FindControl<TextBox>("TxtFtpPort")!.Text = settings.Ftp.Port.ToString();
                this.FindControl<TextBox>("TxtSqlUser")!.Text = settings.Sql.User;
                this.FindControl<TextBox>("TxtSqlPassword")!.Text = settings.Sql.Password;
                this.FindControl<TextBox>("TxtMcApiKey")!.Text = settings.Mailchimp.ApiKey;
                this.FindControl<TextBox>("TxtMcAudienceId")!.Text = settings.Mailchimp.AudienceId;
                
                if (statusText != null)
                {
                    statusText.Text = "Credentials imported! Click Save to apply.";
                    statusText.Foreground = Avalonia.Media.Brush.Parse("#588157");
                }
                
                NotificationService.ShowBackupToast("Credentials", "Encrypted credentials imported! Click Save to apply.", "Success");
            }
            catch (Exception ex)
            {
                if (statusText != null)
                {
                    statusText.Text = $"Import failed: {ex.Message}";
                    statusText.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                }
                NotificationService.ShowBackupToast("Credentials", $"Import failed: {ex.Message}", "Error");
            }
        }

        private static readonly byte[] Key = Encoding.UTF8.GetBytes("PinayPalBackupManagerKey2024!");

        private static string EncryptString(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = Key[..32];
            aes.GenerateIV();
            
            using var encryptor = aes.CreateEncryptor();
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var encrypted = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
            
            // Combine IV + encrypted data, then convert to Base64
            var result = new byte[aes.IV.Length + encrypted.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
            
            return Convert.ToBase64String(result);
        }

        private static string DecryptString(string cipherText)
        {
            var fullCipher = Convert.FromBase64String(cipherText);
            
            using var aes = Aes.Create();
            aes.Key = Key[..32];
            
            // Extract IV (first 16 bytes)
            var iv = new byte[16];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
            aes.IV = iv;
            
            // Extract encrypted data
            var cipherBytes = new byte[fullCipher.Length - 16];
            Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);
            
            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            
            return Encoding.UTF8.GetString(decrypted);
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
