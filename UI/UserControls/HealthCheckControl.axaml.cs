using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PinayPalBackupManager.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class HealthCheckControl : UserControl
    {
        public HealthCheckControl()
        {
            InitializeComponent();
            
            var btnRunCheck = this.FindControl<Button>("BtnRunCheck");
            if (btnRunCheck != null)
            {
                btnRunCheck.Click += async (s, e) => await RunHealthCheckAsync();
            }

            var btnExport = this.FindControl<Button>("BtnExport");
            if (btnExport != null)
            {
                btnExport.Click += (s, e) => ExportReport();
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
        }

        private async Task RunHealthCheckAsync()
        {
            var btnRunCheck = this.FindControl<Button>("BtnRunCheck");
            if (btnRunCheck != null) btnRunCheck.IsEnabled = false;

            try
            {
                var result = await HealthCheckService.RunHealthCheckAsync();
                UpdateUI(result);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Health check error: {ex.Message}", "Error", "HEALTHCHECK");
            }
            finally
            {
                if (btnRunCheck != null) btnRunCheck.IsEnabled = true;
            }
        }

        private void UpdateUI(HealthCheckService.HealthCheckResult result)
        {
            // Update last run time
            var txtLastRun = this.FindControl<TextBlock>("TxtLastRun");
            if (txtLastRun != null)
            {
                txtLastRun.Text = $"Last check: {result.Timestamp:yyyy-MM-dd HH:mm:ss}";
            }

            // Update overall status
            var txtOverallStatus = this.FindControl<TextBlock>("TxtOverallStatus");
            var borderStatusIndicator = this.FindControl<Border>("BorderStatusIndicator");
            var txtStatusDetails = this.FindControl<TextBlock>("TxtStatusDetails");

            if (txtOverallStatus != null)
            {
                txtOverallStatus.Text = result.Status;
                txtOverallStatus.Foreground = result.IsHealthy 
                    ? Brush.Parse("#3FB950") 
                    : Brush.Parse("#F85149");
            }

            if (borderStatusIndicator != null)
            {
                borderStatusIndicator.Background = result.IsHealthy 
                    ? Brush.Parse("#3FB950") 
                    : Brush.Parse("#F85149");
            }

            if (txtStatusDetails != null)
            {
                var healthyCount = result.Components.Values.Count(c => c.IsHealthy);
                var totalCount = result.Components.Count;
                txtStatusDetails.Text = result.IsHealthy 
                    ? "All systems operational"
                    : $"{totalCount - healthyCount} component(s) unhealthy";
            }

            // Update component list
            var componentList = this.FindControl<StackPanel>("ComponentList");
            if (componentList != null)
            {
                componentList.Children.Clear();
                foreach (var kvp in result.Components)
                {
                    var component = kvp.Value;
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    grid.Margin = new Avalonia.Thickness(0, 5, 5, 5);
                    
                    var statusDot = new Border
                    {
                        Width = 8,
                        Height = 8,
                        CornerRadius = new Avalonia.CornerRadius(4),
                        Background = component.IsHealthy ? Brush.Parse("#3FB950") : Brush.Parse("#F85149"),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 0, 10, 0)
                    };

                    var nameText = new TextBlock
                    {
                        Text = component.Name,
                        FontSize = 12,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };

                    var statusText = new TextBlock
                    {
                        Text = component.Status,
                        FontSize = 11,
                        Foreground = component.IsHealthy ? Brush.Parse("#3FB950") : Brush.Parse("#F85149"),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                        Margin = new Avalonia.Thickness(10, 0, 0, 0)
                    };

                    Grid.SetColumn(statusDot, 0);
                    Grid.SetColumn(nameText, 1);
                    Grid.SetColumn(statusText, 2);
                    grid.Children.Add(statusDot);
                    grid.Children.Add(nameText);
                    grid.Children.Add(statusText);
                    
                    componentList.Children.Add(grid);

                    // Add details if available
                    if (!string.IsNullOrEmpty(component.Details))
                    {
                        var detailsText = new TextBlock
                        {
                            Text = component.Details,
                            FontSize = 10,
                            Foreground = Brush.Parse("#8B949E"),
                            Margin = new Avalonia.Thickness(18, 0, 5, 8),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        };
                        componentList.Children.Add(detailsText);
                    }
                }
            }

            // Update system resources
            var resourceList = this.FindControl<StackPanel>("ResourceList");
            if (resourceList != null)
            {
                resourceList.Children.Clear();
                
                // CPU
                AddResourceRow(resourceList, "CPU", result.Resources.CpuUsagePercent, "#58A6FF");
                // Memory
                AddResourceRow(resourceList, "Memory", result.Resources.MemoryUsagePercent, "#A371F7");
                // Disk
                AddResourceRow(resourceList, "Disk", result.Resources.DiskUsagePercent, "#3FB950");
                
                // Backup Size
                var sizeGrid = new Grid();
                sizeGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                sizeGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                sizeGrid.Margin = new Avalonia.Thickness(0, 5, 5, 5);
                
                var sizeLabel = new TextBlock
                {
                    Text = "Backup Size:",
                    FontSize = 11,
                    Foreground = Brush.Parse("#8B949E"),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                
                var sizeValue = new TextBlock
                {
                    Text = FormatBytes(result.Resources.BackupPathSizeMB * 1024 * 1024),
                    FontSize = 11,
                    Foreground = Brush.Parse("#E6EDF3"),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                };
                
                Grid.SetColumn(sizeLabel, 0);
                Grid.SetColumn(sizeValue, 1);
                sizeGrid.Children.Add(sizeLabel);
                sizeGrid.Children.Add(sizeValue);
                resourceList.Children.Add(sizeGrid);
            }

            // Update warnings
            var borderWarnings = this.FindControl<Border>("BorderWarnings");
            var warningList = this.FindControl<StackPanel>("WarningList");
            if (borderWarnings != null && warningList != null)
            {
                warningList.Children.Clear();
                if (result.Warnings.Count > 0)
                {
                    borderWarnings.IsVisible = true;
                    foreach (var warning in result.Warnings)
                    {
                        var text = new TextBlock
                        {
                            Text = $"⚠ {warning}",
                            FontSize = 11,
                            Foreground = Brush.Parse("#D29922"),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Avalonia.Thickness(0, 2)
                        };
                        warningList.Children.Add(text);
                    }
                }
                else
                {
                    borderWarnings.IsVisible = false;
                }
            }

            // Update errors
            var borderErrors = this.FindControl<Border>("BorderErrors");
            var errorList = this.FindControl<StackPanel>("ErrorList");
            if (borderErrors != null && errorList != null)
            {
                errorList.Children.Clear();
                if (result.Errors.Count > 0)
                {
                    borderErrors.IsVisible = true;
                    foreach (var error in result.Errors)
                    {
                        var text = new TextBlock
                        {
                            Text = $"✗ {error}",
                            FontSize = 11,
                            Foreground = Brush.Parse("#F85149"),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Avalonia.Thickness(0, 2)
                        };
                        errorList.Children.Add(text);
                    }
                }
                else
                {
                    borderErrors.IsVisible = false;
                }
            }
        }

        private async void ExportReport()
        {
            try
            {
                var summary = HealthCheckService.GetHealthSummary();
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null)
                {
                    return;
                }

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Health Check Report",
                    DefaultExtension = "txt",
                    FileTypeChoices = new List<FilePickerFileType>
                    {
                        new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (file == null)
                {
                    return;
                }

                await File.WriteAllTextAsync(file.Path.LocalPath, summary);
                LogService.WriteSystemLog("Health check export requested", "Information", "HEALTHCHECK");
                NotificationService.ShowBackupToast("Export", "Report exported successfully", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Export failed: {ex.Message}", "Error", "HEALTHCHECK");
            }
        }

        private void AddResourceRow(StackPanel panel, string label, double value, string color)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
            grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
            grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
            grid.Margin = new Avalonia.Thickness(0, 5, 5, 5);
            
            var labelText = new TextBlock
            {
                Text = label,
                Width = 60,
                FontSize = 11,
                Foreground = Brush.Parse("#8B949E"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            
            var progressBar = new ProgressBar
            {
                Value = value,
                Maximum = 100,
                Height = 8,
                Margin = new Avalonia.Thickness(10, 0),
                Foreground = Brush.Parse(color),
                CornerRadius = new Avalonia.CornerRadius(4)
            };
            
            var valueText = new TextBlock
            {
                Text = $"{value:F0}%",
                Width = 40,
                FontSize = 11,
                Foreground = Brush.Parse(color),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
            };
            
            Grid.SetColumn(labelText, 0);
            Grid.SetColumn(progressBar, 1);
            Grid.SetColumn(valueText, 2);
            grid.Children.Add(labelText);
            grid.Children.Add(progressBar);
            grid.Children.Add(valueText);
            
            panel.Children.Add(grid);
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
