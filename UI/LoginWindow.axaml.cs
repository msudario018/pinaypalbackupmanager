using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI
{
    public partial class LoginWindow : Window
    {
        public event Action? OnLoginSuccess;
        private CancellationTokenSource? _statusListenerCts;

        private IBrush GetBrush(string key)
        {
            if (Application.Current?.TryGetResource(key, out var value) == true && value is IBrush b)
                return b;
            // Fallbacks
            return Brushes.White;
        }

        public LoginWindow()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            this.Icon = AppIconHelper.GetAppWindowIcon();
            this.Opened += (s, e) => AppIconHelper.SetNativeWindowIcon(this);

            // Note: First-run setup is now handled by SetupWizardWindow
            // This login window only handles returning users

            // Wire buttons
            this.FindControl<Button>("BtnLogin")!.Click += OnLoginClick;
            this.FindControl<Button>("BtnVerify2FA")!.Click += OnVerify2FAClick;
            this.FindControl<Button>("BtnBackToLogin")!.Click += OnBackToLoginClick;
            this.FindControl<Button>("BtnRegister")!.Click += OnRegisterClick;
            this.FindControl<Button>("BtnShowRegister")!.Click += (_, _) => ShowRegisterPanel(isFirstUser: false);
            this.FindControl<Button>("BtnShowLogin")!.Click += (_, _) => ShowLoginPanel();

            // Wire Account Recovery buttons
            var btnForgotUser = this.FindControl<Button>("BtnForgotUser");
            if (btnForgotUser != null) btnForgotUser.Click += (_, _) => ShowRecoveryPanel(isForgotPass: false);

            var btnForgotPass = this.FindControl<Button>("BtnForgotPass");
            if (btnForgotPass != null) btnForgotPass.Click += (_, _) => ShowRecoveryPanel(isForgotPass: true);

            var btnTabForgotUser = this.FindControl<Button>("BtnTabForgotUser");
            if (btnTabForgotUser != null) btnTabForgotUser.Click += (_, _) => SwitchRecoveryTab(isForgotPass: false);

            var btnTabForgotPass = this.FindControl<Button>("BtnTabForgotPass");
            if (btnTabForgotPass != null) btnTabForgotPass.Click += (_, _) => SwitchRecoveryTab(isForgotPass: true);

            var btnSendUsername = this.FindControl<Button>("BtnSendUsername");
            if (btnSendUsername != null) btnSendUsername.Click += OnSendUsernameClick;

            var btnRequestOtp = this.FindControl<Button>("BtnRequestOtp");
            if (btnRequestOtp != null) btnRequestOtp.Click += OnRequestOtpClick;

            var btnVerifyOtp = this.FindControl<Button>("BtnVerifyOtp");
            if (btnVerifyOtp != null) btnVerifyOtp.Click += OnVerifyOtpClick;

            var btnResendOtp = this.FindControl<Button>("BtnResendOtp");
            if (btnResendOtp != null) btnResendOtp.Click += OnRequestOtpClick;

            var btnSaveNewPass = this.FindControl<Button>("BtnSaveNewPassword");
            if (btnSaveNewPass != null) btnSaveNewPass.Click += OnSaveNewPasswordClick;

            var btnBackRecovery = this.FindControl<Button>("BtnBackFromRecovery");
            if (btnBackRecovery != null) btnBackRecovery.Click += (_, _) => ShowLoginPanel();

            // Emergency admin button — dev-only, visible on login screen for recovery
            var btnEmergency = this.FindControl<Button>("BtnEmergencyAdmin");
            if (btnEmergency != null)
            {
                btnEmergency.IsVisible = AuthService.IsDevEnvironment();
                btnEmergency.Click += (_, _) =>
                {
                    var errorTxt = this.FindControl<TextBlock>("TxtLoginError")!;
                    if (AuthService.HasAnyUsers())
                    {
                        errorTxt.Foreground = GetBrush("AccentError");
                        errorTxt.Text = "Users already exist. Use 'Reset All Users' in Settings if you need to start fresh.";
                        return;
                    }
                    var (success, message) = AuthService.CreateEmergencyAdmin();
                    if (success)
                    {
                        errorTxt.Foreground = GetBrush("AccentSuccess");
                        errorTxt.Text = message + " Use username: admin, password: admin123";
                    }
                    else
                    {
                        errorTxt.Foreground = GetBrush("AccentError");
                        errorTxt.Text = message;
                    }
                };
            }

            // Start real-time status listener when username changes
            this.FindControl<TextBox>("TxtLoginUser")!.TextChanged += (s, e) =>
            {
                var username = this.FindControl<TextBox>("TxtLoginUser")!.Text ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(username))
                {
                    StartStatusListener(username.Trim());
                }
            };

            // Allow Enter key on password fields
            this.FindControl<TextBox>("TxtLoginPass")!.KeyDown += (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter) OnLoginClick(s, e);
            };
            this.FindControl<TextBox>("TxtRegPass")!.KeyDown += (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter) OnRegisterClick(s, e);
            };
            this.FindControl<TextBox>("Txt2FACode")!.KeyDown += (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter) OnVerify2FAClick(s, e);
            };
        }

        private void StartStatusListener(string username)
        {
            // Cancel any existing listener
            _statusListenerCts?.Cancel();
            _statusListenerCts = new CancellationTokenSource();
            
            // Start listening for status changes in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await FirebaseUserService.StartListeningForUserStatusAsync(username, (newStatus) =>
                    {
                        // Update UI on main thread only if window is still open
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            // Check if window is still open and not closing
                            if (this.IsVisible && _statusListenerCts != null && !_statusListenerCts.IsCancellationRequested)
                            {
                                var errorTxt = this.FindControl<TextBlock>("TxtLoginError");
                                if (errorTxt == null) return;
                                
                                // Only show status updates if there's already an error message showing
                                // This prevents the message from appearing when user is just typing
                                if (!string.IsNullOrWhiteSpace(errorTxt.Text))
                                {
                                    if (newStatus == "Active")
                                    {
                                        errorTxt.Foreground = GetBrush("AccentFtp");
                                        errorTxt.Text = "Your account has been approved! You can now log in.";
                                    }
                                    else if (newStatus == "Deleted")
                                    {
                                        errorTxt.Foreground = GetBrush("AccentError");
                                        errorTxt.Text = "Your account has been deleted. Contact admin if you believe this is an error.";
                                    }
                                    else if (newStatus == "Disabled")
                                    {
                                        errorTxt.Foreground = GetBrush("AccentError");
                                        errorTxt.Text = "Your account has been disabled. Contact admin.";
                                    }
                                }
                            }
                        });
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LoginWindow] Status listener error: {ex.Message}");
                }
            }, _statusListenerCts.Token);
        }

        protected override void OnClosing(Avalonia.Controls.WindowClosingEventArgs e)
        {
            // Stop the status listener when window closes
            _statusListenerCts?.Cancel();
            _statusListenerCts = null;
            base.OnClosing(e);
        }

        private void ShowLoginPanel()
        {
            this.FindControl<Border>("LoginPanel")!.IsVisible = true;
            this.FindControl<Border>("RegisterPanel")!.IsVisible = false;
            var recPanel = this.FindControl<Border>("RecoveryPanel");
            if (recPanel != null) recPanel.IsVisible = false;
            this.FindControl<TextBlock>("TxtSubtitle")!.Text = "Sign in to continue";
            ClearErrors();
        }

        private void ShowRegisterPanel(bool isFirstUser)
        {
            this.FindControl<Border>("LoginPanel")!.IsVisible = false;
            this.FindControl<Border>("RegisterPanel")!.IsVisible = true;

            var invitePanel = this.FindControl<StackPanel>("InviteCodePanel")!;
            var regTitle = this.FindControl<TextBlock>("TxtRegTitle")!;
            var subtitle = this.FindControl<TextBlock>("TxtSubtitle")!;

            if (isFirstUser)
            {
                regTitle.Text = "CREATE ADMIN ACCOUNT";
                subtitle.Text = "First time setup — create your admin account";
                invitePanel.IsVisible = false;
            }
            else
            {
                regTitle.Text = "CREATE ACCOUNT";
                subtitle.Text = "Register with an invite code";
                invitePanel.IsVisible = true;
            }

            ClearErrors();
        }

        private int _pending2FAUserId = 0;
        private string _pendingUsername = "";
        private string _pendingPassword = "";

        private async void OnLoginClick(object? sender, RoutedEventArgs e)
        {
            var username = this.FindControl<TextBox>("TxtLoginUser")!.Text ?? string.Empty;
            var password = this.FindControl<TextBox>("TxtLoginPass")!.Text ?? string.Empty;
            var errorTxt = this.FindControl<TextBlock>("TxtLoginError")!;
            var btnLogin = this.FindControl<Button>("BtnLogin");

            try
            {
                if (btnLogin != null) btnLogin.IsEnabled = false;
                errorTxt.Foreground = GetBrush("AppMuted");
                errorTxt.Text = "Checking credentials...";

                // Step 1: Verify username/password only (no 2FA yet)
                var (success, user, message) = await AuthService.VerifyCredentialsAsync(username, password);
                if (!success || user == null)
                {
                    errorTxt.Foreground = GetBrush("AccentError");
                    errorTxt.Text = message;
                    return;
                }

                // Step 2: Check if 2FA is enabled
                if (TwoFactorAuthService.IsEnabled(user.Id))
                {
                    // Check if this device is remembered
                    if (RememberedDeviceService.IsDeviceRemembered(user.Id))
                    {
                        // Device is remembered, skip 2FA and complete login
                        var (deviceLoginSuccess, deviceLoginMessage) = AuthService.Login(username, password);
                        if (!deviceLoginSuccess)
                        {
                            errorTxt.Foreground = GetBrush("AccentError");
                            errorTxt.Text = deviceLoginMessage;
                            return;
                        }
                        await CompleteLoginAsync(user);
                        return;
                    }

                    // Store pending login info and show 2FA panel
                    _pending2FAUserId = user.Id;
                    _pendingUsername = username;
                    _pendingPassword = password;
                    Show2FAPanel();
                    return;
                }

                // No 2FA - complete login via AuthService to set CurrentUser
                var (loginSuccess, loginMessage) = AuthService.Login(username, password);
                if (!loginSuccess)
                {
                    errorTxt.Foreground = GetBrush("AccentError");
                    errorTxt.Text = loginMessage;
                    return;
                }
                await CompleteLoginAsync(user);
            }
            catch (Exception ex)
            {
                errorTxt.Foreground = GetBrush("AccentError");
                errorTxt.Text = $"Login failed: {ex.Message}";
            }
            finally
            {
                if (btnLogin != null) btnLogin.IsEnabled = true;
            }
        }

        private void Show2FAPanel()
        {
            this.FindControl<Border>("LoginPanel")!.IsVisible = false;
            this.FindControl<Border>("TwoFactorPanel")!.IsVisible = true;
            this.FindControl<TextBlock>("TxtSubtitle")!.Text = "Two-Factor Authentication";
            this.FindControl<TextBox>("Txt2FACode")!.Text = "";
            this.FindControl<TextBlock>("Txt2FAError")!.Text = "";
            this.FindControl<CheckBox>("ChkRememberDevice")!.IsChecked = false;
            this.FindControl<TextBox>("Txt2FACode")!.Focus();
        }

        private void Hide2FAPanel()
        {
            this.FindControl<Border>("LoginPanel")!.IsVisible = true;
            this.FindControl<Border>("TwoFactorPanel")!.IsVisible = false;
            this.FindControl<TextBlock>("TxtSubtitle")!.Text = "Sign in to continue";
            this.FindControl<TextBox>("Txt2FACode")!.Text = "";
            this.FindControl<TextBlock>("Txt2FAError")!.Text = "";
        }

        private void OnBackToLoginClick(object? sender, RoutedEventArgs e)
        {
            Hide2FAPanel();
        }

        private async void OnVerify2FAClick(object? sender, RoutedEventArgs e)
        {
            var code = this.FindControl<TextBox>("Txt2FACode")!.Text?.Trim() ?? "";
            var errorTxt = this.FindControl<TextBlock>("Txt2FAError")!;
            var rememberDevice = this.FindControl<CheckBox>("ChkRememberDevice")!.IsChecked == true;

            try
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    errorTxt.Text = "Please enter your authenticator code or a recovery code";
                    return;
                }

                // Verify the 2FA code (handles both TOTP codes and recovery codes)
                if (!TwoFactorAuthService.VerifyCode(_pending2FAUserId, code))
                {
                    errorTxt.Text = "Invalid code. Please try again.";
                    return;
                }

                // Code is valid - complete the login
                var user = AuthService.GetUserById(_pending2FAUserId);
                if (user != null)
                {
                    // Set current user since VerifyCredentials didn't do full login
                    AuthService.SetCurrentUserFor2FA(user);

                    // Remember device if checkbox is checked
                    if (rememberDevice)
                    {
                        await RememberedDeviceService.RememberDeviceAsync(user.Id, user.Username);
                    }

                    await CompleteLoginAsync(user);
                }
            }
            catch (Exception ex)
            {
                errorTxt.Text = $"2FA verification failed: {ex.Message}";
            }
        }

        private async Task CompleteLoginAsync(AppUser user)
        {
            await Task.Yield();
            _statusListenerCts?.Cancel();
            var rememberMe = this.FindControl<CheckBox>("ChkRememberMe")?.IsChecked == true;
            if (rememberMe)
                SessionService.SaveSession(user.Id);
            else
                SessionService.ClearSession();
            OnLoginSuccess?.Invoke();
        }

        private async void OnRegisterClick(object? sender, RoutedEventArgs e)
        {
            var username = this.FindControl<TextBox>("TxtRegUser")!.Text ?? string.Empty;
            var password = this.FindControl<TextBox>("TxtRegPass")!.Text ?? string.Empty;
            var inviteCode = this.FindControl<TextBox>("TxtInviteCode")!.Text ?? string.Empty;
            var email = this.FindControl<TextBox>("TxtRegEmail")?.Text?.Trim();
            var birthDate = this.FindControl<TextBox>("TxtRegBirthDate")?.Text?.Trim();
            var errorTxt = this.FindControl<TextBlock>("TxtRegError")!;

            if (!string.IsNullOrWhiteSpace(email) && (!email.Contains("@") || !email.Contains(".")))
            {
                errorTxt.Text = "Please enter a valid email address.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(birthDate) && !DateTime.TryParse(birthDate, out _))
            {
                errorTxt.Text = "Birthday format should be YYYY-MM-DD.";
                return;
            }

            try
            {
                var (success, message) = await AuthService.RegisterAsync(username, password, inviteCode, email, birthDate);
                if (success)
                {
                    // Auto-login after registration
                    var (loginOk, _) = AuthService.Login(username, password);
                    if (loginOk)
                    {
                        OnLoginSuccess?.Invoke();
                        return;
                    }

                    // Fallback: show login panel with success message
                    ShowLoginPanel();
                    this.FindControl<TextBlock>("TxtLoginError")!.Text = message + " Please sign in.";
                    this.FindControl<TextBlock>("TxtLoginError")!.Foreground = Avalonia.Media.Brush.Parse("#588157");
                }
                else
                {
                    errorTxt.Text = message;
                }
            }
            catch (Exception ex)
            {
                errorTxt.Text = $"Registration failed: {ex.Message}";
            }
        }

        private void ClearErrors()
        {
            this.FindControl<TextBlock>("TxtLoginError")!.Text = string.Empty;
            this.FindControl<TextBlock>("TxtRegError")!.Text = string.Empty;
            var recStatus = this.FindControl<TextBlock>("TxtRecoveryStatus");
            if (recStatus != null) recStatus.Text = string.Empty;
            var loginErr = this.FindControl<TextBlock>("TxtLoginError")!;
            loginErr.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
        }

        // ── Recovery Handlers ──
        private int _recoveryUserId = 0;
        private string _recoveryOtpCode = "";
        private string _recoveryIdentifier = "";

        private void ShowRecoveryPanel(bool isForgotPass)
        {
            this.FindControl<Border>("LoginPanel")!.IsVisible = false;
            this.FindControl<Border>("RegisterPanel")!.IsVisible = false;
            var twoFa = this.FindControl<Border>("TwoFactorPanel");
            if (twoFa != null) twoFa.IsVisible = false;

            var recPanel = this.FindControl<Border>("RecoveryPanel");
            if (recPanel != null) recPanel.IsVisible = true;

            this.FindControl<TextBlock>("TxtSubtitle")!.Text = isForgotPass ? "Reset your account password" : "Recover your account username";
            SwitchRecoveryTab(isForgotPass);
            ClearErrors();
        }

        private void SwitchRecoveryTab(bool isForgotPass)
        {
            var btnTabUser = this.FindControl<Button>("BtnTabForgotUser");
            var btnTabPass = this.FindControl<Button>("BtnTabForgotPass");
            var pnlUser = this.FindControl<StackPanel>("SubPanelForgotUser");
            var pnlPass1 = this.FindControl<StackPanel>("SubPanelPassStep1");
            var pnlPass2 = this.FindControl<StackPanel>("SubPanelPassStep2");
            var pnlPass3 = this.FindControl<StackPanel>("SubPanelPassStep3");
            var recStatus = this.FindControl<TextBlock>("TxtRecoveryStatus");

            if (recStatus != null) recStatus.Text = string.Empty;

            if (isForgotPass)
            {
                if (btnTabUser != null) { btnTabUser.Background = Brushes.Transparent; btnTabUser.Foreground = GetBrush("AppMuted"); }
                if (btnTabPass != null) { btnTabPass.Background = GetBrush("AccentWebsite"); btnTabPass.Foreground = Brushes.White; }
                if (pnlUser != null) pnlUser.IsVisible = false;
                if (pnlPass1 != null) pnlPass1.IsVisible = true;
                if (pnlPass2 != null) pnlPass2.IsVisible = false;
                if (pnlPass3 != null) pnlPass3.IsVisible = false;
            }
            else
            {
                if (btnTabUser != null) { btnTabUser.Background = GetBrush("AccentWebsite"); btnTabUser.Foreground = Brushes.White; }
                if (btnTabPass != null) { btnTabPass.Background = Brushes.Transparent; btnTabPass.Foreground = GetBrush("AppMuted"); }
                if (pnlUser != null) pnlUser.IsVisible = true;
                if (pnlPass1 != null) pnlPass1.IsVisible = false;
                if (pnlPass2 != null) pnlPass2.IsVisible = false;
                if (pnlPass3 != null) pnlPass3.IsVisible = false;
            }
        }

        private async void OnSendUsernameClick(object? sender, RoutedEventArgs e)
        {
            var emailTxt = this.FindControl<TextBox>("TxtForgotEmail")?.Text ?? string.Empty;
            var birthDateTxt = this.FindControl<TextBox>("TxtForgotBirthDate")?.Text ?? string.Empty;
            var statusTxt = this.FindControl<TextBlock>("TxtRecoveryStatus")!;

            if (string.IsNullOrWhiteSpace(emailTxt))
            {
                statusTxt.Foreground = GetBrush("AccentError");
                statusTxt.Text = "Please enter your email address.";
                return;
            }

            if (string.IsNullOrWhiteSpace(birthDateTxt))
            {
                statusTxt.Foreground = GetBrush("AccentError");
                statusTxt.Text = "Please enter your birthday (e.g. YYYY-MM-DD) for verification.";
                return;
            }

            statusTxt.Foreground = GetBrush("AppMuted");
            statusTxt.Text = "Verifying account details...";

            var (success, message, isPreview, username) = await EmailOtpService.SendUsernameRecoveryAsync(emailTxt, birthDateTxt);
            if (success)
            {
                statusTxt.Foreground = GetBrush("AccentSuccess");
                statusTxt.Text = message;
            }
            else
            {
                statusTxt.Foreground = GetBrush("AccentError");
                statusTxt.Text = message;
            }
        }

        private async void OnRequestOtpClick(object? sender, RoutedEventArgs e)
        {
            var identifier = this.FindControl<TextBox>("TxtPassResetIdentifier")?.Text ?? string.Empty;
            var overrideEmail = this.FindControl<TextBox>("TxtOverrideEmail")?.Text;
            var statusTxt = this.FindControl<TextBlock>("TxtRecoveryStatus")!;
            var pnlOverride = this.FindControl<StackPanel>("PnlOverrideEmail");

            if (string.IsNullOrWhiteSpace(identifier))
            {
                statusTxt.Foreground = GetBrush("AccentError");
                statusTxt.Text = "Please enter your username or email.";
                return;
            }

            _recoveryIdentifier = identifier.Trim();
            statusTxt.Foreground = GetBrush("AppMuted");
            statusTxt.Text = "Generating 6-digit OTP code...";

            var (success, message, userId, email, isPreview, previewOtp) = await EmailOtpService.SendPasswordResetOtpAsync(identifier, overrideEmail);

            if (!success && message == "NO_EMAIL_CONFIGURED")
            {
                if (pnlOverride != null) pnlOverride.IsVisible = true;
                statusTxt.Foreground = GetBrush("AccentWebsite");
                statusTxt.Text = "This account does not have a recovery email yet. Please enter your email above to link it and receive your OTP.";
                return;
            }

            if (success && userId.HasValue)
            {
                _recoveryUserId = userId.Value;
                statusTxt.Foreground = GetBrush("AccentSuccess");
                statusTxt.Text = message;

                // Move to Step 2
                var pnlPass1 = this.FindControl<StackPanel>("SubPanelPassStep1");
                var pnlPass2 = this.FindControl<StackPanel>("SubPanelPassStep2");
                if (pnlPass1 != null) pnlPass1.IsVisible = false;
                if (pnlPass2 != null) pnlPass2.IsVisible = true;

                var txtSentNotice = this.FindControl<TextBlock>("TxtOtpSentNotice");
                if (txtSentNotice != null && !string.IsNullOrEmpty(email))
                {
                    txtSentNotice.Text = $"Enter the 6-digit verification code sent to {email}.";
                }
            }
            else
            {
                statusTxt.Foreground = GetBrush("AccentError");
                statusTxt.Text = message;
            }
        }

        private async void OnVerifyOtpClick(object? sender, RoutedEventArgs e)
        {
            var otpInput = this.FindControl<TextBox>("TxtOtpInput")?.Text ?? string.Empty;
            var statusTxt = this.FindControl<TextBlock>("TxtRecoveryStatus")!;

            if (string.IsNullOrWhiteSpace(otpInput))
            {
                statusTxt.Foreground = GetBrush("AccentError");
                statusTxt.Text = "Please enter the 6-digit verification code.";
                return;
            }

            statusTxt.Foreground = GetBrush("AppMuted");
            statusTxt.Text = "Verifying code...";

            var (valid, message) = await EmailOtpService.VerifyOtpAsync(_recoveryUserId, otpInput);
            if (valid)
            {
                _recoveryOtpCode = otpInput.Trim();
                statusTxt.Foreground = GetBrush("AccentSuccess");
                statusTxt.Text = "Code verified! Now create your new password.";

                // Move to Step 3
                var pnlPass2 = this.FindControl<StackPanel>("SubPanelPassStep2");
                var pnlPass3 = this.FindControl<StackPanel>("SubPanelPassStep3");
                if (pnlPass2 != null) pnlPass2.IsVisible = false;
                if (pnlPass3 != null) pnlPass3.IsVisible = true;
            }
            else
            {
                statusTxt.Foreground = GetBrush("AccentError");
                statusTxt.Text = message;
            }
        }

        private async void OnSaveNewPasswordClick(object? sender, RoutedEventArgs e)
        {
            var newPass = this.FindControl<TextBox>("TxtNewPassword")?.Text ?? string.Empty;
            var confirmPass = this.FindControl<TextBox>("TxtConfirmNewPassword")?.Text ?? string.Empty;
            var statusTxt = this.FindControl<TextBlock>("TxtRecoveryStatus")!;

            statusTxt.Foreground = GetBrush("AppMuted");
            statusTxt.Text = "Updating password...";

            var (success, message) = await EmailOtpService.ResetPasswordWithOtpAsync(_recoveryUserId, _recoveryOtpCode, newPass, confirmPass);
            if (success)
            {
                // Return to login with username prefilled
                ShowLoginPanel();
                var loginUserTxt = this.FindControl<TextBox>("TxtLoginUser");
                if (loginUserTxt != null && !string.IsNullOrWhiteSpace(_recoveryIdentifier))
                {
                    loginUserTxt.Text = _recoveryIdentifier;
                }
                var loginErr = this.FindControl<TextBlock>("TxtLoginError")!;
                loginErr.Foreground = GetBrush("AccentSuccess");
                loginErr.Text = "Password reset successfully! Please sign in with your new password.";
            }
            else
            {
                statusTxt.Foreground = GetBrush("AccentError");
                statusTxt.Text = message;
            }
        }
    }
}
