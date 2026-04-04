using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class TabOrderDialog : Window
    {
        private static readonly string OrderFile = Path.Combine(AppDataPaths.CurrentDirectory, "taborder.json");

        private static readonly List<(string Tag, string Label, string Icon)> DefaultTabs =
        [
            ("Home",      "Dashboard",    "🏠"),
            ("FTP",       "FTP Sync",     "☁"),
            ("Mailchimp", "Mailchimp",    "✉"),
            ("SQL",       "SQL Backup",   "🗄"),
            ("Settings",  "Settings",     "⚙"),
        ];

        private List<(string Tag, string Label, string Icon)> _tabs;
        public bool Saved { get; private set; }

        public TabOrderDialog()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            _tabs = LoadOrder();
            BuildList();

            this.FindControl<Button>("BtnResetOrder")!.Click  += (_, _) => { _tabs = new List<(string, string, string)>(DefaultTabs); BuildList(); };
            this.FindControl<Button>("BtnCancelOrder")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnSaveOrder")!.Click   += (_, _) => { SaveOrder(); Saved = true; Close(true); };
        }

        private void BuildList()
        {
            var list = this.FindControl<StackPanel>("TabList");
            if (list == null) return;
            list.Children.Clear();

            for (int i = 0; i < _tabs.Count; i++)
            {
                int idx = i;
                var (tag, label, icon) = _tabs[i];

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto, 8, Auto") };

                var iconText = new TextBlock
                {
                    Text = icon,
                    FontSize = 16,
                    Width = 28,
                    VerticalAlignment = VerticalAlignment.Center
                };

                bool isDark = Services.ThemeService.IsDark;
                string btnBg       = isDark ? "#313244" : "#BCC0CC";

                var labelText = new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    Foreground = Brush.Parse(isDark ? "#CDD6F4" : "#4C4F69"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                string btnFgActive = isDark ? "#CDD6F4" : "#4C4F69";
                string btnFgMuted  = isDark ? "#45475A"  : "#9CA0B0";

                var btnUp = new Button
                {
                    Content = "▲",
                    FontSize = 12,
                    Padding = new Avalonia.Thickness(8, 4),
                    Background = Brush.Parse(btnBg),
                    Foreground = idx == 0 ? Brush.Parse(btnFgMuted) : Brush.Parse(btnFgActive),
                    IsEnabled = idx > 0,
                    BorderThickness = new Avalonia.Thickness(0),
                    CornerRadius = new Avalonia.CornerRadius(6),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };
                btnUp.Click += (_, _) => { MoveTab(idx, -1); };

                var btnDown = new Button
                {
                    Content = "▼",
                    FontSize = 12,
                    Padding = new Avalonia.Thickness(8, 4),
                    Background = Brush.Parse(btnBg),
                    Foreground = idx == _tabs.Count - 1 ? Brush.Parse(btnFgMuted) : Brush.Parse(btnFgActive),
                    IsEnabled = idx < _tabs.Count - 1,
                    BorderThickness = new Avalonia.Thickness(0),
                    CornerRadius = new Avalonia.CornerRadius(6),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };
                btnDown.Click += (_, _) => { MoveTab(idx, +1); };

                Grid.SetColumn(iconText, 0);
                Grid.SetColumn(labelText, 1);
                Grid.SetColumn(btnUp, 2);
                Grid.SetColumn(btnDown, 4);

                row.Children.Add(iconText);
                row.Children.Add(labelText);
                row.Children.Add(btnUp);
                row.Children.Add(btnDown);

                list.Children.Add(row);

                if (i < _tabs.Count - 1)
                    list.Children.Add(new Border { Height = 1, Background = Brush.Parse(isDark ? "#313244" : "#BCC0CC"), Margin = new Avalonia.Thickness(0, 2) });
            }
        }

        private void MoveTab(int index, int direction)
        {
            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= _tabs.Count) return;
            var temp = _tabs[index];
            _tabs[index] = _tabs[newIndex];
            _tabs[newIndex] = temp;
            BuildList();
        }

        private void SaveOrder()
        {
            try
            {
                var order = _tabs.Select(t => t.Tag).ToList();
                Directory.CreateDirectory(AppDataPaths.CurrentDirectory);
                File.WriteAllText(OrderFile, JsonSerializer.Serialize(order));
            }
            catch { }
        }

        public static List<string> LoadSavedTagOrder()
        {
            try
            {
                if (File.Exists(OrderFile))
                {
                    var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(OrderFile));
                    if (saved != null && saved.Count == DefaultTabs.Count)
                        return saved;
                }
            }
            catch { }
            return DefaultTabs.Select(t => t.Tag).ToList();
        }

        private static List<(string Tag, string Label, string Icon)> LoadOrder()
        {
            var order = LoadSavedTagOrder();
            return order
                .Select(tag => DefaultTabs.FirstOrDefault(d => d.Tag == tag))
                .Where(t => t != default)
                .ToList();
        }
    }
}
