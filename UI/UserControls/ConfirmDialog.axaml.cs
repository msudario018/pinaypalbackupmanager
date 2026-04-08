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
        
        public static async Task<bool> ShowAsync(string title, string message)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            var dialog = new ConfirmDialog(title, message);
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
                Topmost = true,
                Background = Avalonia.Media.Brushes.Transparent
            };
            
            dialog.OnResult += (sender, result) => 
            {
                window.Close();
                tcs.SetResult(result);
            };
            
            // Find the main window as owner
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
                
            if (mainWindow != null)
            {
                await window.ShowDialog(mainWindow);
            }
            
            return await tcs.Task;
        }
    }
}
