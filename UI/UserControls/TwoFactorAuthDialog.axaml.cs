using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PinayPalBackupManager.Services;
using QRCoder;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class TwoFactorAuthDialog : UserControl
    {
        public event EventHandler? OnClose;
        private int _userId;
        private string _secretKey = "";
        private DispatcherTimer? _totpTimer;
        private TextBlock? _txtLiveCode;
        private TextBlock? _txtCountdown;
        private Border? _barCountdown;
        private Border? _barParent;

        public TwoFactorAuthDialog(int userId)
        {
            _userId = userId;
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            var btnEnable = this.FindControl<Button>("BtnEnable");
            var btnDisable = this.FindControl<Button>("BtnDisable");
            var btnRegenerate = this.FindControl<Button>("BtnRegenerate");
            var btnClose = this.FindControl<Button>("BtnClose");
            var panelSetup = this.FindControl<StackPanel>("PanelSetup");
            var panelEnabled = this.FindControl<StackPanel>("PanelEnabled");
            var imgQr = this.FindControl<Image>("ImgQr");
            var txtSecretKey = this.FindControl<TextBlock>("TxtSecretKey");
            var txtCode = this.FindControl<TextBox>("TxtCode");
            var txtError = this.FindControl<TextBlock>("TxtError");
            var txtBackupCodes = this.FindControl<TextBlock>("TxtBackupCodes");
            _txtLiveCode = this.FindControl<TextBlock>("TxtLiveCode");
            _txtCountdown = this.FindControl<TextBlock>("TxtCountdown");
            _barCountdown = this.FindControl<Border>("BarCountdown");
            _barParent = _barCountdown?.Parent as Border;

            // Show local state immediately, then sync from Firebase in background
            RefreshUiState(panelSetup!, panelEnabled!, imgQr!, txtSecretKey!, txtBackupCodes!);

            // Pull latest 2FA settings from Firebase and refresh UI
            _ = Task.Run(async () =>
            {
                try
                {
                    await TwoFactorAuthService.SyncFromFirebaseAsync(_userId);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        RefreshUiState(panelSetup!, panelEnabled!, imgQr!, txtSecretKey!, txtBackupCodes!));
                    Console.WriteLine("[2FA] Firebase sync completed, UI refreshed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[2FA] Firebase sync on open failed: {ex.Message}");
                }
            });

            btnEnable!.Click += async (s, e) =>
            {
                var code = txtCode!.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(code))
                {
                    txtError!.Text = "Please enter the verification code.";
                    return;
                }

                // Verify the code
                var expectedCode = GenerateTotpCode(_secretKey);
                if (code != expectedCode)
                {
                    txtError!.Text = "Invalid verification code. Please try again.";
                    return;
                }

                // Enable 2FA using the same secret that was shown
                await TwoFactorAuthService.EnableTfaAsync(_userId, _secretKey);
                
                // Show enabled panel
                panelSetup.IsVisible = false;
                panelEnabled!.IsVisible = true;
                UpdateBackupCodes(txtBackupCodes!);
                
                NotificationService.ShowBackupToast("Security", "Two-factor authentication enabled!", "Success");
            };

            btnDisable!.Click += async (s, e) =>
            {
                var result = await ConfirmDialog.ShowAsync("Disable 2FA", "Are you sure you want to disable two-factor authentication? This makes your account less secure.");
                if (result)
                {
                    await TwoFactorAuthService.DisableTfaAsync(_userId);
                    panelSetup!.IsVisible = true;
                    panelEnabled.IsVisible = false;
                    
                    // Ensure we have a persistent secret for the next setup
                    _secretKey = TwoFactorAuthService.EnsureSecret(_userId);
                    txtSecretKey!.Text = _secretKey;
                    UpdateQrImage(imgQr!, _secretKey);
                    txtCode!.Text = "";
                    txtError!.Text = "";
                    
                    NotificationService.ShowBackupToast("Security", "Two-factor authentication disabled.", "Warning");
                }
            };

            btnRegenerate!.Click += async (s, e) =>
            {
                await TwoFactorAuthService.RegenerateBackupCodesAsync(_userId);
                UpdateBackupCodes(txtBackupCodes!);
                NotificationService.ShowBackupToast("Security", "New backup codes generated!", "Success");
            };

            btnClose!.Click += (s, e) =>
            {
                StopTotpTimer();
                OnClose?.Invoke(this, EventArgs.Empty);
            };

            // Start the live TOTP timer
            StartTotpTimer();
        }

        private void StartTotpTimer()
        {
            _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _totpTimer.Tick += (s, e) => UpdateLiveCode();
            _totpTimer.Start();
            UpdateLiveCode();
        }

        private void StopTotpTimer()
        {
            _totpTimer?.Stop();
            _totpTimer = null;
        }

        private void UpdateLiveCode()
        {
            if (_txtLiveCode == null || _txtCountdown == null || _barCountdown == null) return;

            var secret = TwoFactorAuthService.GetSecretKey(_userId);
            if (string.IsNullOrEmpty(secret))
            {
                _txtLiveCode.Text = "------";
                _txtCountdown.Text = "";
                return;
            }

            var code = GenerateTotpCode(secret);
            _txtLiveCode.Text = code.Insert(3, " ");

            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var secondsRemaining = 30 - (int)(unixTime % 30);
            _txtCountdown.Text = $"{secondsRemaining}s";

            // Update progress bar width
            if (_barParent != null && _barParent.Bounds.Width > 0)
            {
                var fraction = secondsRemaining / 30.0;
                _barCountdown.Width = _barParent.Bounds.Width * fraction;
            }
        }

        private void UpdateBackupCodes(TextBlock txtBackupCodes)
        {
            var codes = TwoFactorAuthService.GetBackupCodes(_userId);
            txtBackupCodes.Text = string.Join("  ", codes);
        }

        private void RefreshUiState(StackPanel panelSetup, StackPanel panelEnabled, Image imgQr, TextBlock txtSecretKey, TextBlock txtBackupCodes)
        {
            if (TwoFactorAuthService.IsEnabled(_userId))
            {
                panelSetup.IsVisible = false;
                panelEnabled.IsVisible = true;
                UpdateBackupCodes(txtBackupCodes);
            }
            else
            {
                _secretKey = TwoFactorAuthService.EnsureSecret(_userId);
                txtSecretKey.Text = _secretKey;
                UpdateQrImage(imgQr, _secretKey);
                panelSetup.IsVisible = true;
                panelEnabled.IsVisible = false;
            }
        }

        private string GenerateRandomSecret()
        {
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var random = new Random();
            var result = new StringBuilder(16);
            for (int i = 0; i < 16; i++)
                result.Append(chars[random.Next(chars.Length)]);
            return result.ToString();
        }

        private void UpdateQrImage(Image target, string secret)
        {
            try
            {
                var user = AuthService.GetUserById(_userId);
                var account = Uri.EscapeDataString(user?.Username ?? $"user-{_userId}");
                var issuer = "PinayPalBackupManager";
                // Simplified URI — SHA1/6/30 are defaults, omitting them avoids parser issues
                var uri = $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}";

                Console.WriteLine($"[2FA] QR URI: {uri}");

                var generator = new QRCodeGenerator();
                // Use ECCLevel.Q for better error correction = more reliable scanning
                using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
                var pngQr = new PngByteQRCode(data);
                // Large pixel-per-module (20) so the source image is crisp even when scaled
                var bytes = pngQr.GetGraphic(20, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, true);
                using var ms = new MemoryStream(bytes);
                target.Source = new Bitmap(ms);

                Console.WriteLine($"[2FA] QR code generated, {bytes.Length} bytes, modules={data.ModuleMatrix.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[2FA] QR generation failed: {ex.Message}");
            }
        }

        private string GenerateTotpCode(string secret)
        {
            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timeStep = unixTime / 30;
            var keyBytes = Base32Decode(secret);
            var timeBytes = BitConverter.GetBytes(timeStep);
            if (BitConverter.IsLittleEndian)
                System.Array.Reverse(timeBytes);

            using var hmac = new System.Security.Cryptography.HMACSHA1(keyBytes);
            var hash = hmac.ComputeHash(timeBytes);
            var offset = hash[hash.Length - 1] & 0x0F;
            var binaryCode = ((hash[offset] & 0x7F) << 24) |
                             ((hash[offset + 1] & 0xFF) << 16) |
                             ((hash[offset + 2] & 0xFF) << 8) |
                             (hash[offset + 3] & 0xFF);
            var code = binaryCode % 1_000_000;
            return code.ToString("D6");
        }

        private static readonly char[] Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();
        private static byte[] Base32Decode(string base32)
        {
            if (string.IsNullOrWhiteSpace(base32)) return Array.Empty<byte>();
            base32 = base32.Trim().Replace(" ", string.Empty).ToUpperInvariant();
            int byteCount = base32.Length * 5 / 8;
            var result = new System.Collections.Generic.List<byte>(byteCount);
            int bitBuffer = 0;
            int bitCount = 0;

            foreach (char c in base32)
            {
                int val = System.Array.IndexOf(Base32Alphabet, c);
                if (val < 0) continue;
                bitBuffer = (bitBuffer << 5) | val;
                bitCount += 5;
                if (bitCount >= 8)
                {
                    int b = (bitBuffer >> (bitCount - 8)) & 0xFF;
                    result.Add((byte)b);
                    bitCount -= 8;
                }
            }
            return result.ToArray();
        }
    }
}
