using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PinayPalBackupManager.Services;
using System;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class InviteCodesDialog : UserControl
    {
        public event EventHandler? OnClose;
        private DateTime _nextRotateTime;
        private DispatcherTimer? _inviteTimer;

        private async Task LoadInviteCodeAsync(TextBox? txtInviteCode)
        {
            try
            {
                var code = await AuthService.GetInviteCodeAsync();
                if (txtInviteCode != null)
                {
                    txtInviteCode.Text = string.IsNullOrEmpty(code) ? "(none)" : code;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InviteCodesDialog] Failed to load invite code: {ex.Message}");
                if (txtInviteCode != null)
                {
                    txtInviteCode.Text = "(error)";
                }
            }
        }

        public InviteCodesDialog()
        {
            Console.WriteLine("[InviteCodesDialog] Constructor started");
            
            try
            {
                Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
                Console.WriteLine("[InviteCodesDialog] XAML loaded successfully");

                var txtInviteCode = this.FindControl<TextBox>("TxtInviteCode");
                var btnCopy = this.FindControl<Button>("BtnCopy");
                var btnRegenerate = this.FindControl<Button>("BtnRegenerate");
                var btnClose = this.FindControl<Button>("BtnClose");
                
                Console.WriteLine($"[InviteCodesDialog] Controls found - TextBox: {txtInviteCode != null}, Copy: {btnCopy != null}, Regenerate: {btnRegenerate != null}, Close: {btnClose != null}");

                // Setup copy button
                if (btnCopy != null)
                {
                    btnCopy.Click += async (_, _) =>
                    {
                        if (txtInviteCode != null && !string.IsNullOrEmpty(txtInviteCode.Text) && txtInviteCode.Text != "(none)")
                        {
                            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                            if (clipboard != null)
                                await clipboard.SetTextAsync(txtInviteCode.Text);
                            NotificationService.ShowBackupToast("Users", "Invite code copied to clipboard.", "Info");
                        }
                    };
                }

                // Setup regenerate button
                if (btnRegenerate != null)
                {
                    btnRegenerate.Click += (_, _) =>
                    {
                        var newCode = AuthService.RotateInviteCode();
                        if (txtInviteCode != null) txtInviteCode.Text = newCode;
                        _nextRotateTime = DateTime.UtcNow.AddMinutes(5);
                        NotificationService.ShowBackupToast("Users", "Invite code regenerated!", "Success");
                    };
                }

                // Setup close button
                if (btnClose != null) btnClose.Click += (_, _) =>
                {
                    _inviteTimer?.Stop();
                    OnClose?.Invoke(this, EventArgs.Empty);
                };

                // Start auto-rotation timer
                _nextRotateTime = DateTime.UtcNow.AddMinutes(5);
                _inviteTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _inviteTimer.Tick += (_, _) =>
                {
                    var remaining = _nextRotateTime - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        var newCode = AuthService.RotateInviteCode();
                        if (txtInviteCode != null) txtInviteCode.Text = newCode;
                        _nextRotateTime = DateTime.UtcNow.AddMinutes(5);
                        NotificationService.ShowBackupToast("Users", "Invite code auto-rotated.", "Info");
                    }
                    UpdateTimerArc(remaining.TotalSeconds / 300.0); // 300 seconds = 5 minutes
                };
                _inviteTimer.Start();
                UpdateTimerArc(1.0);

                // Load current invite code asynchronously to avoid blocking
                _ = LoadInviteCodeAsync(txtInviteCode);
                Console.WriteLine("[InviteCodesDialog] LoadInviteCodeAsync started");
                Console.WriteLine("[InviteCodesDialog] Constructor completed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InviteCodesDialog] Constructor error: {ex.Message}");
                Console.WriteLine($"[InviteCodesDialog] Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateTimerArc(double progress)
        {
            var timerArc = this.FindControl<Avalonia.Controls.Shapes.Path>("TimerArc");
            if (timerArc == null) return;

            // Clamp progress between 0 and 1
            progress = Math.Clamp(progress, 0.0, 1.0);

            // Create an arc from top (-90°) clockwise
            const double radius = 7;
            const double centerX = 8;
            const double centerY = 8;
            const double startAngle = -90;
            double endAngle = startAngle + (360 * progress);

            // Convert angles to radians
            double startRad = startAngle * Math.PI / 180.0;
            double endRad = endAngle * Math.PI / 180.0;

            // Calculate start and end points
            double startX = centerX + radius * Math.Cos(startRad);
            double startY = centerY + radius * Math.Sin(startRad);
            double endX = centerX + radius * Math.Cos(endRad);
            double endY = centerY + radius * Math.Sin(endRad);

            // Determine if the arc is large (>180°)
            bool isLargeArc = progress > 0.5;

            // Create the path data
            string pathData = $"M {startX},{startY} A {radius},{radius} 0 {(isLargeArc ? 1 : 0)},1 {endX},{endY}";
            timerArc.Data = Geometry.Parse(pathData);
        }
    }
}
