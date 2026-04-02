using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI
{
    public partial class LoginWindow : Window
    {
        public event Action? OnLoginSuccess;

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

            // If no users exist, show register panel for first-time admin setup
            if (!hasUsers)
            {
                ShowRegisterPanel(isFirstUser: true);
            }

            // Wire buttons
            this.FindControl<Button>("BtnLogin")!.Click += OnLoginClick;
            this.FindControl<Button>("BtnRegister")!.Click += OnRegisterClick;
            this.FindControl<Button>("BtnShowRegister")!.Click += (_, _) => ShowRegisterPanel(isFirstUser: false);
            this.FindControl<Button>("BtnShowLogin")!.Click += (_, _) => ShowLoginPanel();

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
