using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
                                        errorTxt.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
                                        errorTxt.Text = "Your account has been approved! You can now log in.";
                                    }
                                    else if (newStatus == "Deleted")
                                    {
                                        errorTxt.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                                        errorTxt.Text = "Your account has been deleted. Contact admin if you believe this is an error.";
                                    }
                                    else if (newStatus == "Disabled")
                                    {
                                        errorTxt.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
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

        private void OnLoginClick(object? sender, RoutedEventArgs e)
        {
            var username = this.FindControl<TextBox>("TxtLoginUser")!.Text ?? string.Empty;
            var password = this.FindControl<TextBox>("TxtLoginPass")!.Text ?? string.Empty;
            var errorTxt = this.FindControl<TextBlock>("TxtLoginError")!;

            var (success, message) = AuthService.Login(username, password);
            if (success)
            {
                _statusListenerCts?.Cancel();
                var rememberMe = this.FindControl<CheckBox>("ChkRememberMe")?.IsChecked == true;
                if (rememberMe && AuthService.CurrentUser != null)
                    SessionService.SaveSession(AuthService.CurrentUser.Id);
                else
                    SessionService.ClearSession();
                OnLoginSuccess?.Invoke();
            }
            else
            {
                errorTxt.Text = message;
            }
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
                this.FindControl<TextBlock>("TxtLoginError")!.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
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
