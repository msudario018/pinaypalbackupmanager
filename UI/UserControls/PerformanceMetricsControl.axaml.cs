using Avalonia.Controls;
using Avalonia.Media;
using PinayPalBackupManager.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class PerformanceMetricsControl : UserControl
    {
        public PerformanceMetricsControl()
        {
            InitializeComponent();
            
            var btnRefresh = this.FindControl<Button>("BtnRefresh");
            if (btnRefresh != null)
            {
                btnRefresh.Click += async (s, e) => await RefreshMetricsAsync();
            }

            var btnExport = this.FindControl<Button>("BtnExport");
            if (btnExport != null)
            {
                btnExport.Click += (s, e) => ExportReport();
            }

            var btnClear = this.FindControl<Button>("BtnClear");
            if (btnClear != null)
            {
                btnClear.Click += (s, e) => ClearOldMetrics();
            }

            var btnClose = this.FindControl<Button>("BtnClose");
            if (btnClose != null)
            {
                btnClose.Click += (s, e) =>
                {
                    var parentWindow = TopLevel.GetTopLevel(this) as Window;
                    parentWindow?.Close();
                };
            }

            // Load initial data
            _ = RefreshMetricsAsync();
        }

        private async Task RefreshMetricsAsync()
        {
            try
            {
                var report = PerformanceMetricsService.GeneratePerformanceReport();
                UpdateUI(report);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to refresh metrics: {ex.Message}", "Error", "PERFORMANCE");
            }
        }

        private void UpdateUI(PerformanceMetricsService.PerformanceReport report)
        {
            // Update summary
            var txtSummary = this.FindControl<TextBlock>("TxtSummary");
            if (txtSummary != null)
            {
                txtSummary.Text = $"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} | Metrics: {report.Summaries.Count}";
            }

            // Update success rates
            var successRatePanel = this.FindControl<StackPanel>("SuccessRatePanel");
            if (successRatePanel != null)
            {
                successRatePanel.Children.Clear();
                
                foreach (var kvp in report.BackupSuccessRates)
                {
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    grid.Margin = new Avalonia.Thickness(0, 5, 5, 5);
                    
                    var serviceText = new TextBlock
                    {
                        Text = kvp.Key.ToUpper(),
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 12,
                        Width = 60
                    };

                    var progressBar = new ProgressBar
                    {
                        Value = kvp.Value,
                        Maximum = 100,
                        Height = 8,
                        Margin = new Avalonia.Thickness(10, 0),
                        CornerRadius = new Avalonia.CornerRadius(4)
                    };

                    var valueText = new TextBlock
                    {
                        Text = $"{kvp.Value:F1}%",
                        Width = 50,
                        TextAlignment = Avalonia.Media.TextAlignment.Right,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                    };

                    Grid.SetColumn(serviceText, 0);
                    Grid.SetColumn(progressBar, 1);
                    Grid.SetColumn(valueText, 2);
                    
                    grid.Children.Add(serviceText);
                    grid.Children.Add(progressBar);
                    grid.Children.Add(valueText);
                    
                    successRatePanel.Children.Add(grid);
                }
            }

            // Update backup times
            var backupTimePanel = this.FindControl<StackPanel>("BackupTimePanel");
            if (backupTimePanel != null)
            {
                backupTimePanel.Children.Clear();
                
                foreach (var kvp in report.AverageBackupTimes)
                {
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    grid.Margin = new Avalonia.Thickness(0, 5, 5, 5);
                    
                    var serviceText = new TextBlock
                    {
                        Text = kvp.Key.ToUpper(),
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 12,
                        Width = 60
                    };

                    var progressBar = new ProgressBar
                    {
                        Value = Math.Min(kvp.Value, 60), // Cap at 60 seconds for display
                        Maximum = 60,
                        Height = 8,
                        Margin = new Avalonia.Thickness(10, 0),
                        CornerRadius = new Avalonia.CornerRadius(4)
                    };

                    var valueText = new TextBlock
                    {
                        Text = $"{kvp.Value:F2}s",
                        Width = 50,
                        TextAlignment = Avalonia.Media.TextAlignment.Right,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                    };

                    Grid.SetColumn(serviceText, 0);
                    Grid.SetColumn(progressBar, 1);
                    Grid.SetColumn(valueText, 2);
                    
                    grid.Children.Add(serviceText);
                    grid.Children.Add(progressBar);
                    grid.Children.Add(valueText);
                    
                    backupTimePanel.Children.Add(grid);
                }
            }

            // Update metric summaries
            var metricSummaryPanel = this.FindControl<StackPanel>("MetricSummaryPanel");
            if (metricSummaryPanel != null)
            {
                metricSummaryPanel.Children.Clear();
                
                foreach (var kvp in report.Summaries.OrderByDescending(s => s.Value.SampleCount))
                {
                    var summary = kvp.Value;
                    
                    var border = new Border
                    {
                        Background = Brush.Parse("#0A000000"),
                        BorderBrush = Brush.Parse("#30363D"),
                        BorderThickness = new Avalonia.Thickness(1),
                        CornerRadius = new Avalonia.CornerRadius(8),
                        Padding = new Avalonia.Thickness(12)
                    };

                    var panel = new StackPanel { Spacing = 8 };

                    // Header
                    var headerGrid = new Grid();
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    headerGrid.Margin = new Avalonia.Thickness(0, 0, 5, 0);
                    
                    var nameText = new TextBlock
                    {
                        Text = summary.Name,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 12,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                    };

                    var countText = new TextBlock
                    {
                        Text = $"{summary.SampleCount} samples",
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E"),
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                    };

                    Grid.SetColumn(nameText, 0);
                    Grid.SetColumn(countText, 1);
                    headerGrid.Children.Add(nameText);
                    headerGrid.Children.Add(countText);

                    // Stats
                    var statsGrid = new Grid();
                    statsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    statsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    statsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    statsGrid.Margin = new Avalonia.Thickness(0, 5, 5, 0);
                    
                    var avgText = new TextBlock { Text = $"Avg: {summary.Average:F2} {summary.Unit}", FontSize = 10, Foreground = Brush.Parse("#8B949E"), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                    var minText = new TextBlock { Text = $"Min: {summary.Min:F2}", FontSize = 10, Foreground = Brush.Parse("#8B949E"), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                    var maxText = new TextBlock { Text = $"Max: {summary.Max:F2}", FontSize = 10, Foreground = Brush.Parse("#8B949E"), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };

                    Grid.SetColumn(avgText, 0);
                    Grid.SetColumn(minText, 1);
                    Grid.SetColumn(maxText, 2);
                    statsGrid.Children.Add(avgText);
                    statsGrid.Children.Add(minText);
                    statsGrid.Children.Add(maxText);

                    panel.Children.Add(headerGrid);
                    panel.Children.Add(statsGrid);

                    border.Child = panel;
                    metricSummaryPanel.Children.Add(border);
                }
            }
        }

        private void ExportReport()
        {
            try
            {
                var report = PerformanceMetricsService.ExportMetricsReport();
                LogService.WriteSystemLog("Performance metrics exported", "Information", "PERFORMANCE");
                NotificationService.ShowBackupToast("Export", "Performance metrics exported successfully", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Export failed: {ex.Message}", "Error", "PERFORMANCE");
                NotificationService.ShowBackupToast("Export", "Failed to export metrics", "Error");
            }
        }

        private void ClearOldMetrics()
        {
            try
            {
                PerformanceMetricsService.ClearMetrics(null); // Clear all
                _ = RefreshMetricsAsync();
                NotificationService.ShowBackupToast("Clear", "Performance metrics cleared", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Clear failed: {ex.Message}", "Error", "PERFORMANCE");
            }
        }
    }
}
