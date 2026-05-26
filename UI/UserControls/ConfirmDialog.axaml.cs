using Avalonia.Controls;
using System;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ConfirmDialog : UserControl
    {
        public event EventHandler<bool>? OnResult;
        
        public ConfirmDialog()
        {
            InitializeComponent();
        }
        
        public ConfirmDialog(string title, string message) : this()
        {
            TitleText.Text = title;
            MessageText.Text = message;
            
            BtnNo.Click += (_, _) => OnResult?.Invoke(this, false);
            BtnYes.Click += (_, _) => OnResult?.Invoke(this, true);
        }
        
        public static async Task<bool> ShowAsync(string title, string message, Window? parentWindow = null)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            var dialog = new ConfirmDialog(title, message);
            
            // Use provided parent window or find the main window as owner
            var ownerWindow = parentWindow ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);
            
            var window = new Window
            {
                Content = dialog,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaTitleBarHeightHint = 0,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                SystemDecorations = SystemDecorations.None,
                // No Topmost - ShowDialog makes it modal to parent only
                Background = Avalonia.Media.Brushes.Transparent
                // Note: Owner is set automatically by ShowDialog()
            };
            
            dialog.OnResult += (sender, result) => 
            {
                window.Close();
                tcs.SetResult(result);
            };
                
            if (ownerWindow != null)
            {
                // Ensure parent window is active so dialog appears on top
                ownerWindow.Activate();
                await window.ShowDialog(ownerWindow);
            }
            else
            {
                // Fallback: show without owner if no window found
                window.Show();
                window.Activate();
                await tcs.Task;
            }
            
            return await tcs.Task;
        }
    }
}
