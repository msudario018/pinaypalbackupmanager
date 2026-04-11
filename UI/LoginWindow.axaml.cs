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

        private static bool IsDevelopmentMachine()
        {
            // Check if running from a development directory
            var currentDir = Directory.GetCurrentDirectory();
            var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            
            // Development indicators
            return currentDir.Contains("pinaypalbackupmanager") && 
                   (currentDir.Contains("Debug") || currentDir.Contains("bin") || 
                    File.Exists(Path.Combine(baseDir, "..", "..", "PinayPalBackupManager.csproj")) ||
                    File.Exists(Path.Combine(currentDir, "PinayPalBackupManager.csproj")));
        }

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

            bool hasUsers = false;
            try
            {
                hasUsers = AuthService.HasAnyUsers();
            }
            catch (Exception ex)
            {
                // If database check fails, assume no users (fresh install)
                Console.WriteLine($"[LoginWindow] Error checking users: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[LoginWindow] Error checking users: {ex}");
            }

            // If no users exist, check if this is development machine
            if (!hasUsers)
            {
                if (IsDevelopmentMachine())
                {
                    // Development machine - show admin setup
                    ShowRegisterPanel(isFirstUser: true);
                }
                else
                {
                    // Production machine - create default admin automatically
                    try
                    {
                        var (success, message) = AuthService.Register("admin", "admin123");
                        Console.WriteLine($"[LoginWindow] Admin creation result: {success}, {message}");
                        if (!success)
                        {
                            Console.WriteLine($"[LoginWindow] Failed to create admin: {message}");
                            throw new Exception($"Failed to create admin: {message}");
                        }
                        
                        // Show login panel since admin is created
                        ShowLoginPanel();
                        
                        // Update subtitle to show default credentials
                        var subtitle = this.FindControl<TextBlock>("TxtSubtitle")!;
                        subtitle.Text = "Default admin created (admin/admin123)";
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LoginWindow] Failed to create default admin: {ex.Message}");
                        // Fallback to admin setup
                        ShowRegisterPanel(isFirstUser: true);
                    }
                }
            }

            // Wire buttons
            this.FindControl<Button>("BtnLogin")!.Click += OnLoginClick;
            this.FindControl<Button>("BtnVerify2FA")!.Click += OnVerify2FAClick;
            this.FindControl<Button>("BtnBackToLogin")!.Click += OnBackToLoginClick;
            this.FindControl<Button>("BtnRegister")!.Click += OnRegisterClick;
            this.FindControl<Button>("BtnShowRegister")!.Click += (_, _) => ShowRegisterPanel(isFirstUser: false);
            this.FindControl<Button>("BtnShowLogin")!.Click += (_, _) => ShowLoginPanel();

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

            Console.WriteLine($"[LoginWindow] OnLoginClick called for username: {username}");

            try
            {
                if (btnLogin != null) btnLogin.IsEnabled = false;
                errorTxt.Foreground = GetBrush("AppMuted");
                errorTxt.Text = "Checking credentials...";

                // Step 1: Verify username/password only (no 2FA yet)
                Console.WriteLine($"[LoginWindow] Calling VerifyCredentialsAsync for username: {username}");
                var (success, user, message) = await AuthService.VerifyCredentialsAsync(username, password);
                Console.WriteLine($"[LoginWindow] VerifyCredentialsAsync result: success={success}, user={user?.Username}, role={user?.Role}");
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
                        Console.WriteLine($"[LoginWindow] Device remembered for user {user.Username}, skipping 2FA");
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
                    Console.WriteLine($"[LoginWindow] Device remembered for user {user.Username}");
                }

                await CompleteLoginAsync(user);
            }
        }

        private async Task CompleteLoginAsync(AppUser user)
        {
            Console.WriteLine($"[LoginWindow] CompleteLoginAsync called for user: {user.Username}, Role={user.Role}");
            _statusListenerCts?.Cancel();
            var rememberMe = this.FindControl<CheckBox>("ChkRememberMe")?.IsChecked == true;
            Console.WriteLine($"[LoginWindow] Remember Me: {rememberMe}");
            if (rememberMe)
                SessionService.SaveSession(user.Id);
            else
                SessionService.ClearSession();
            Console.WriteLine($"[LoginWindow] Invoking OnLoginSuccess");
            OnLoginSuccess?.Invoke();
        }

        private void OnRegisterClick(object? sender, RoutedEventArgs e)
        {
            var username = this.FindControl<TextBox>("TxtRegUser")!.Text ?? string.Empty;
            var password = this.FindControl<TextBox>("TxtRegPass")!.Text ?? string.Empty;
            var inviteCode = this.FindControl<TextBox>("TxtInviteCode")!.Text ?? string.Empty;
            var errorTxt = this.FindControl<TextBlock>("TxtRegError")!;

            var (success, message) = AuthService.Register(username, password, inviteCode);
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

        private void ClearErrors()
        {
            this.FindControl<TextBlock>("TxtLoginError")!.Text = string.Empty;
            this.FindControl<TextBlock>("TxtRegError")!.Text = string.Empty;
            var loginErr = this.FindControl<TextBlock>("TxtLoginError")!;
            loginErr.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
        }
    }
}
