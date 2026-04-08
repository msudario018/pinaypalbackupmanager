using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class DashboardCustomizationDialog : UserControl
    {
        public event Action<DashboardCustomization>? OnApply;
        
        private DashboardCustomization _settings = new DashboardCustomization();

        public DashboardCustomizationDialog()
        {
            InitializeComponent();
            LoadCurrentSettings();
        }

        private void InitializeComponent()
        {
            var applyButton = new Button
            {
                Content = "Apply",
                Background = Brush.Parse("#CBA6F7"),
                Foreground = Brush.Parse("#1E1E2E"),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(20, 10),
                FontSize = 12,
                FontWeight = FontWeight.Bold
            };
            applyButton.Click += (_, _) => 
            {
                // Read checkbox value before applying
                var compactCheckBox = this.FindControl<CheckBox>("CompactModeCheckBox");
                if (compactCheckBox != null)
                {
                    _settings.CompactMode = compactCheckBox.IsChecked == true;
                }
                
                // Save settings
                DashboardCustomization.Save(_settings);
                
                OnApply?.Invoke(_settings);
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Background = Brush.Parse("#6C7086"),
                Foreground = Brush.Parse("#CDD6F4"),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(20, 10),
                FontSize = 12
            };
            cancelButton.Click += (_, _) => 
            {
                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                parentWindow?.Close();
            };

            var compactCheckBox = new CheckBox
            {
                Name = "CompactModeCheckBox",
                Content = "Compact Mode",
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#6C7086"),
                IsChecked = _settings.CompactMode
            };

            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Dashboard Customization",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brush.Parse("#CBA6F7"),
                        Margin = new Avalonia.Thickness(0, 0, 0, 10)
                    },
                    new TextBlock
                    {
                        Text = "Choose which sections to show and your preferred view mode.",
                        FontSize = 12,
                        Foreground = Brush.Parse("#6C7086"),
                        Margin = new Avalonia.Thickness(0, 0, 0, 20)
                    },
                    CreateSectionToggle("System Status Overview", nameof(_settings.ShowSystemStatus)),
                    CreateSectionToggle("Quick Stats Cards", nameof(_settings.ShowQuickStats)),
                    CreateSectionToggle("Time Since Last Backup", nameof(_settings.ShowTimeSinceBackup)),
                    CreateSectionToggle("Recent Errors Panel", nameof(_settings.ShowRecentErrors)),
                    CreateSectionToggle("Service Cards", nameof(_settings.ShowServiceCards)),
                    CreateSectionToggle("Backup Health Dashboard", nameof(_settings.ShowHealthDashboard)),
                    CreateSectionToggle("Operations", nameof(_settings.ShowOperations)),
                    CreateSectionToggle("Connectivity", nameof(_settings.ShowConnectivity)),
                    CreateSectionToggle("Stats & Reporting", nameof(_settings.ShowStatsReporting)),
                    CreateSectionToggle("Schedule Adjustment", nameof(_settings.ShowScheduleAdjustment)),
                    CreateSectionToggle("Storage Usage", nameof(_settings.ShowStorageUsage)),
                    CreateSectionToggle("Daily Schedule", nameof(_settings.ShowDailySchedule)),
                    CreateSectionToggle("Recent Activity", nameof(_settings.ShowRecentActivity)),
                    CreateSectionToggle("System Logs", nameof(_settings.ShowSystemLogs)),
                    new Border
                    {
                        Background = Brush.Parse("#6C7086"),
                        CornerRadius = new Avalonia.CornerRadius(8),
                        Padding = new Avalonia.Thickness(12),
                        Margin = new Avalonia.Thickness(0, 10, 0, 0),
                        Child = compactCheckBox
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Margin = new Avalonia.Thickness(0, 20, 0, 0),
                        Children =
                        {
                            cancelButton,
                            applyButton
                        }
                    }
                }
            };
        }

        private Border CreateSectionToggle(string label, string propertyName)
        {
            return new Border
            {
                Background = Brush.Parse("#6C7086"),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(12),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new CheckBox
                        {
                            IsChecked = true,
                            FontSize = 13
                        },
                        new TextBlock
                        {
                            Text = label,
                            FontSize = 13,
                            Foreground = Brush.Parse("#6C7086"),
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };
        }

        private void LoadCurrentSettings()
        {
            // Load saved settings from config
            _settings = DashboardCustomization.Load();
            
            // Update UI to reflect loaded settings
            var compactCheckBox = this.FindControl<CheckBox>("CompactModeCheckBox");
            if (compactCheckBox != null)
            {
                compactCheckBox.IsChecked = _settings.CompactMode;
            }
        }
    }
}
