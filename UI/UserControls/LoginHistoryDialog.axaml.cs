using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PinayPalBackupManager.Services;
using System;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class LoginHistoryDialog : UserControl
    {
        public event EventHandler? OnClose;
        private string _username;

        public LoginHistoryDialog(string username)
        {
            _username = username;
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            var btnClose = this.FindControl<Button>("BtnClose");
            var btnClear = this.FindControl<Button>("BtnClear");
            var historyList = this.FindControl<StackPanel>("HistoryList");
            var txtEmpty = this.FindControl<TextBlock>("TxtEmpty");

            LoadHistory(historyList!, txtEmpty!);

            btnClose!.Click += (s, e) => OnClose?.Invoke(this, EventArgs.Empty);
            
            btnClear!.Click += async (s, e) =>
            {
                var confirm = await ConfirmDialog.ShowAsync("Clear History", "Are you sure you want to clear your login history?");
                if (confirm)
                {
                    await LoginHistoryService.ClearHistoryAsync(_username);
                    historyList!.Children.Clear();
                    txtEmpty!.IsVisible = true;
                    NotificationService.ShowBackupToast("Security", "Login history cleared.", "Info");
                }
            };
        }

        private void LoadHistory(StackPanel historyList, TextBlock txtEmpty)
        {
            var entries = LoginHistoryService.GetLoginHistory(_username, 30);

            if (entries.Count == 0)
            {
                txtEmpty.IsVisible = true;
                return;
            }

            txtEmpty.IsVisible = false;

            foreach (var entry in entries)
            {
                var border = new Border
                {
                    Background = entry.Success ? 
                        new SolidColorBrush(Color.Parse("#2D342D")) : 
                        new SolidColorBrush(Color.Parse("#3D2D2D")),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8),
                    BorderThickness = new Thickness(1),
                    BorderBrush = entry.Success ? 
                        new SolidColorBrush(Color.Parse("#4A524A")) : 
                        new SolidColorBrush(Color.Parse("#5A4242"))
                };

                var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };

                var leftStack = new StackPanel { Spacing = 2 };
                var statusText = new TextBlock
                {
                    Text = entry.Success ? "✓ Success" : "✗ Failed",
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = entry.Success ? 
                        new SolidColorBrush(Color.Parse("#A6E3A1")) : 
                        new SolidColorBrush(Color.Parse("#F38BA8"))
                };
                leftStack.Children.Add(statusText);

                var deviceText = new TextBlock
                {
                    Text = entry.DeviceInfo,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.Parse("#808080"))
                };
                leftStack.Children.Add(deviceText);

                if (!entry.Success && !string.IsNullOrEmpty(entry.FailureReason))
                {
                    var reasonText = new TextBlock
                    {
                        Text = entry.FailureReason,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.Parse("#F38BA8"))
                    };
                    leftStack.Children.Add(reasonText);
                }

                Grid.SetColumn(leftStack, 0);
                grid.Children.Add(leftStack);

                var rightStack = new StackPanel { Spacing = 2 };
                var dateText = new TextBlock
                {
                    Text = entry.Timestamp.ToString("MMM dd, yyyy"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.Parse("#808080")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
                };
                rightStack.Children.Add(dateText);

                var timeText = new TextBlock
                {
                    Text = entry.Timestamp.ToString("h:mm tt"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.Parse("#808080")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
                };
                rightStack.Children.Add(timeText);

                Grid.SetColumn(rightStack, 1);
                grid.Children.Add(rightStack);

                border.Child = grid;
                historyList.Children.Add(border);
            }
        }
    }
}
