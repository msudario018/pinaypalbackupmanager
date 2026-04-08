using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Animation;
using Avalonia.Threading;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ToastNotification : UserControl
    {
        private Point _startPoint;
        private bool _isSwiping = false;
        private double _swipeThreshold = 100; // Minimum swipe distance to dismiss
        private DispatcherTimer? _tapTimer;
        private bool _isTap = false;
        
        public ToastNotification()
        {
            InitializeComponent();
        }
        
        private void ToastNotification_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _startPoint = e.GetPosition(this);
            _isSwiping = true;
            _isTap = true;
            
            // Start timer to detect tap vs swipe
            _tapTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _tapTimer.Tick += (s, args) => 
            {
                _tapTimer.Stop();
                _tapTimer = null;
                
                if (_isTap && _isSwiping)
                {
                    // No significant movement detected, treat as tap
                    HandleTap();
                }
            };
            _tapTimer.Start();
        }
        
        private void ToastNotification_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isSwiping) return;
            
            var currentPoint = e.GetPosition(this);
            var deltaX = currentPoint.X - _startPoint.X;
            var deltaY = Math.Abs(currentPoint.Y - _startPoint.Y);
            
            // If we move significantly, it's not a tap anymore
            if (Math.Abs(deltaX) > 20 || deltaY > 20)
            {
                _isTap = false;
                if (_tapTimer != null)
                {
                    _tapTimer.Stop();
                    _tapTimer = null;
                }
            }
            
            // Only swipe horizontally, not vertically
            if (deltaY < 50 && Math.Abs(deltaX) > 10)
            {
                // Apply swipe transformation
                this.RenderTransform = new TranslateTransform(deltaX, 0);
                this.Opacity = Math.Max(0.3, 1 - (Math.Abs(deltaX) / 200));
            }
        }
        
        private void ToastNotification_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isSwiping) return;
            
            // Stop tap timer
            if (_tapTimer != null)
            {
                _tapTimer.Stop();
                _tapTimer = null;
            }
            
            var endPoint = e.GetPosition(this);
            var deltaX = endPoint.X - _startPoint.X;
            
            // Check if swipe distance exceeds threshold
            if (Math.Abs(deltaX) > _swipeThreshold)
            {
                // Swipe to dismiss
                DismissToast(deltaX > 0 ? 1 : -1); // 1 for right, -1 for left
            }
            else if (_isTap)
            {
                // It's a tap
                HandleTap();
            }
            else
            {
                // Animate back to original position
                AnimateBackToPosition();
            }
            
            _isSwiping = false;
            _isTap = false;
        }
        
        private void DismissToast(int direction)
        {
            // Simple swipe out animation
            var currentTransform = this.RenderTransform as TranslateTransform ?? new TranslateTransform();
            this.RenderTransform = currentTransform;
            
            // Animate swipe out using Transitions
            var transition = new TransformOperationsTransition
            {
                Duration = TimeSpan.FromMilliseconds(200),
                Property = RenderTransformProperty
            };
            
            this.Transitions = new Transitions
            {
                transition,
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) }
            };
            
            this.RenderTransform = new TranslateTransform(400 * direction, 0);
            this.Opacity = 0;
            
            // Remove after animation
            Task.Delay(200).ContinueWith(_ => {
                Dispatcher.UIThread.Post(() => {
                    if (this.Parent is Border container && container.Parent is Grid grid)
                    {
                        grid.Children.Remove(container);
                    }
                });
            });
        }
        
        private void AnimateBackToPosition()
        {
            // Animate back to original position using Transitions
            var transition = new TransformOperationsTransition
            {
                Duration = TimeSpan.FromMilliseconds(200),
                Property = RenderTransformProperty
            };
            
            this.Transitions = new Transitions
            {
                transition,
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) }
            };
            
            this.RenderTransform = new TranslateTransform(0, 0);
            this.Opacity = 1;
        }
        
        private void HandleTap()
        {
            // Find the main window and toggle notification center
            var mainWindow = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
                
            if (mainWindow is PinayPalBackupManager.UI.MainWindow mainWin)
            {
                mainWin.ToggleNotificationCenter();
            }
        }

        public void SetContent(string title, string message, string type = "Info")
        {
            TitleText.Text = title;
            MessageText.Text = message;

            // Set contextual icon based on notification content
            var contextualIcon = GetContextualIcon(title, message, type);
            
            // Set icon and colors based on type using tea-green palette
            switch (type.ToLower())
            {
                case "success":
                    IconCircle.Fill = GetThemeResource("AccentFtp", new SolidColorBrush(Color.FromRgb(204, 213, 174)));
                    IconPath.Data = PathGeometry.Parse(contextualIcon);
                    break;
                case "warning":
                    IconCircle.Fill = GetThemeResource("AppWarning", new SolidColorBrush(Color.FromRgb(250, 214, 67)));
                    IconPath.Data = PathGeometry.Parse(contextualIcon);
                    break;
                case "error":
                    IconCircle.Fill = GetThemeResource("AppWarning", new SolidColorBrush(Color.FromRgb(243, 138, 168)));
                    IconPath.Data = PathGeometry.Parse(contextualIcon);
                    break;
                default: // info
                    IconCircle.Fill = GetThemeResource("AccentWebsite", new SolidColorBrush(Color.FromRgb(149, 213, 178)));
                    IconPath.Data = PathGeometry.Parse(contextualIcon);
                    break;
            }
        }
        
        private string GetContextualIcon(string title, string message, string type)
        {
            var titleLower = title.ToLower();
            var messageLower = message.ToLower();
            
            // Service-specific icons
            if (titleLower.Contains("ftp") || messageLower.Contains("ftp"))
                return "M12,2A3,3 0 0,1 15,5V11A3,3 0 0,1 12,14A3,3 0 0,1 9,11V5A3,3 0 0,1 12,2M12,4A1,1 0 0,0 11,5V11A1,1 0 0,0 12,12A1,1 0 0,0 13,11V5A1,1 0 0,0 12,4M18,18H15V16A3,3 0 0,0 12,13A3,3 0 0,0 9,16V18H6V16A6,6 0 0,1 12,10A6,6 0 0,1 18,16V18Z"; // FTP/Server icon
            
            if (titleLower.Contains("mailchimp") || messageLower.Contains("mailchimp"))
                return "M20,8L12,13L4,8V6A2,2 0 0,1 6,4H18A2,2 0 0,1 20,6V8M20,10V18A2,2 0 0,1 18,20H6A2,2 0 0,1 4,18V10L12,15L20,10Z"; // Mail/Envelope icon
            
            if (titleLower.Contains("sql") || messageLower.Contains("sql"))
                return "M12,3C7.58,3 4,4.79 4,7C4,9.21 7.58,11 12,11C16.42,11 20,9.21 20,7C20,4.79 16.42,3 12,3M4,9V12C4,14.21 7.58,16 12,16C16.42,16 20,14.21 20,12V9C20,11.21 16.42,13 12,13C7.58,13 4,11.21 4,9M4,14V17C4,19.21 7.58,21 12,21C16.42,21 20,19.21 20,17V14C20,16.21 16.42,18 12,18C7.58,18 4,16.21 4,14Z"; // Database icon
            
            if (titleLower.Contains("user") || messageLower.Contains("user"))
                return "M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z"; // User icon
            
            if (titleLower.Contains("backup") || messageLower.Contains("backup"))
                return "M19,12V19H5V12H3V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V12H19M13,12.67L15.59,10.09L17,11.5L12,16.5L7,11.5L8.41,10.09L11,12.67V3H13V12.67Z"; // Backup/Download icon
            
            if (titleLower.Contains("config") || messageLower.Contains("config"))
                return "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.13 21.78,8.79 21.66,8.5L19.66,4.5C19.54,4.21 19.27,4 18.97,4H15.97C15.67,4 15.4,4.21 15.28,4.5L13.28,8.5C13.16,8.79 13.21,9.13 13.4,9.37L15.5,11C15.5,11.34 15.5,11.67 15.5,12C15.5,12.33 15.5,12.65 15.54,12.97L13.43,14.63C13.25,14.87 13.2,15.21 13.32,15.5L15.32,19.5C15.44,19.79 15.71,20 16.01,20H19.01C19.31,20 19.58,19.79 19.7,19.5L21.7,15.5C21.82,15.21 21.77,14.87 21.58,14.63L19.43,12.97Z"; // Settings/Config icon
            
            if (titleLower.Contains("tab") || messageLower.Contains("switched"))
                return "M9,5V9H21V5M9,19H21V15H9M9,14H21V10H9M4,9H8V5H4M4,19H8V15H4M4,14H8V10H4V14Z"; // Tab/View icon
            
            if (titleLower.Contains("health") || messageLower.Contains("scan"))
                return "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M11,7H13V13H11V7M11,15H13V17H11V15Z"; // Health/Check icon
            
            if (titleLower.Contains("startup") || messageLower.Contains("initial"))
                return "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M12,6L6,12L10.5,12L10.5,18L13.5,18L13.5,12L18,12L12,6Z"; // Startup/Power icon
            
            // Default icons based on type
            switch (type.ToLower())
            {
                case "success":
                    return "M9,20.42L2.79,14.21L5.62,11.38L9,14.77L18.88,4.88L21.71,7.71L9,20.42Z"; // Checkmark
                case "warning":
                    return "M13,14H11V10H13M13,18H11V16H13M1,21H23L12,2L1,21Z"; // Warning
                case "error":
                    return "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z"; // X/Close
                default:
                    return "M13,9H11V7H13M13,17H11V11H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z"; // Info
            }
        }

        private T GetThemeResource<T>(string key, T fallback) where T : class
        {
            if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is T resource)
                return resource;
            return fallback;
        }
    }
}
