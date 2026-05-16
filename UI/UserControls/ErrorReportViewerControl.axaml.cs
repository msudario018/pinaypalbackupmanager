using Avalonia.Controls;
using Avalonia.Media;
using PinayPalBackupManager.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ErrorReportViewerControl : UserControl
    {
        public ErrorReportViewerControl()
        {
            InitializeComponent();
            
            var btnClose = this.FindControl<Button>("BtnClose");
            if (btnClose != null)
            {
                btnClose.Click += (s, e) => CloseWindow();
            }
            
            var btnRefresh = this.FindControl<Button>("BtnRefresh");
            if (btnRefresh != null)
            {
                btnRefresh.Click += async (s, e) => await LoadErrorReportsAsync();
            }

            var btnExport = this.FindControl<Button>("BtnExport");
            if (btnExport != null)
            {
                btnExport.Click += (s, e) => ExportReports();
            }

            var btnClearAll = this.FindControl<Button>("BtnClearAll");
            if (btnClearAll != null)
            {
                btnClearAll.Click += (s, e) => ClearAllErrors();
            }

            var btnApplyFilter = this.FindControl<Button>("BtnApplyFilter");
            if (btnApplyFilter != null)
            {
                btnApplyFilter.Click += async (s, e) => await LoadErrorReportsAsync();
            }

            var cmbFilterType = this.FindControl<ComboBox>("CmbFilterType");
            if (cmbFilterType != null)
            {
                cmbFilterType.SelectionChanged += async (s, e) => 
                {
                    var txtFilterSource = this.FindControl<TextBox>("TxtFilterSource");
                    if (txtFilterSource != null)
                    {
                        txtFilterSource.IsVisible = cmbFilterType.SelectedIndex == 2; // Show for "By Source"
                    }
                };
            }

            var btnCloseDetails = this.FindControl<Button>("BtnCloseDetails");
            if (btnCloseDetails != null)
            {
                btnCloseDetails.Click += (s, e) =>
                {
                    var borderDetails = this.FindControl<Border>("BorderErrorDetails");
                    if (borderDetails != null) borderDetails.IsVisible = false;
                };
            }

            // Load initial data
            _ = LoadErrorReportsAsync();
        }

        private void CloseWindow()
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            window?.Close();
        }

        private async Task LoadErrorReportsAsync()
        {
            try
            {
                var cmbFilterType = this.FindControl<ComboBox>("CmbFilterType");
                var txtFilterSource = this.FindControl<TextBox>("TxtFilterSource");
                
                List<ErrorReportingService.ErrorReport> reports;
                
                if (cmbFilterType != null && cmbFilterType.SelectedIndex == 1)
                {
                    // Critical only
                    reports = ErrorReportingService.GetCriticalErrors(50);
                }
                else if (cmbFilterType != null && cmbFilterType.SelectedIndex == 2 && txtFilterSource != null)
                {
                    // By source
                    reports = ErrorReportingService.GetErrorsBySource(txtFilterSource.Text ?? "", 50);
                }
                else
                {
                    // All errors
                    reports = ErrorReportingService.GetErrorReports(50);
                }

                UpdateUI(reports);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to load error reports: {ex.Message}", "Error", "ERRORREPORTS");
            }
        }

        private void UpdateUI(List<ErrorReportingService.ErrorReport> reports)
        {
            var txtSummary = this.FindControl<TextBlock>("TxtSummary");
            var errorList = this.FindControl<StackPanel>("ErrorList");

            if (txtSummary != null)
            {
                var summary = ErrorReportingService.GetErrorSummary();
                txtSummary.Text = $"Total: {summary.TotalErrors} | Critical: {summary.CriticalErrors} | Reported: {summary.ReportedErrors}";
            }

            if (errorList != null)
            {
                errorList.Children.Clear();
                
                if (reports.Count == 0)
                {
                    errorList.Children.Add(new TextBlock 
                    { 
                        Text = "No errors found.", 
                        Foreground = Brush.Parse("#8B949E"), 
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 20)
                    });
                    return;
                }

                foreach (var report in reports)
                {
                    var border = new Border
                    {
                        Background = Brush.Parse("#0A000000"),
                        BorderBrush = report.IsCritical ? Brush.Parse("#F85149") : Brush.Parse("#30363D"),
                        BorderThickness = new Avalonia.Thickness(1),
                        CornerRadius = new Avalonia.CornerRadius(8),
                        Padding = new Avalonia.Thickness(12),
                        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                    };

                    border.PointerPressed += (s, e) => ShowErrorDetails(report);

                    var panel = new StackPanel { Spacing = 8 };

                    // Header
                    var headerGrid = new Grid();
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    headerGrid.Margin = new Avalonia.Thickness(0, 0, 5, 0);
                    
                    var icon = new TextBlock
                    {
                        Text = report.IsCritical ? "🔴" : "⚠️",
                        FontSize = 12,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };

                    var titleText = new TextBlock
                    {
                        Text = report.ErrorType,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 12,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(8, 0, 0, 0)
                    };

                    var timeText = new TextBlock
                    {
                        Text = report.Timestamp.ToString("MM/dd HH:mm"),
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E"),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                    };

                    Grid.SetColumn(icon, 0);
                    Grid.SetColumn(titleText, 1);
                    Grid.SetColumn(timeText, 2);
                    
                    headerGrid.Children.Add(icon);
                    headerGrid.Children.Add(titleText);
                    headerGrid.Children.Add(timeText);

                    // Source
                    var sourceText = new TextBlock
                    {
                        Text = $"Source: {report.Source}",
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E"),
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                        Margin = new Avalonia.Thickness(0, 0, 5, 0)
                    };

                    // Message (truncated)
                    var messageText = new TextBlock
                    {
                        Text = report.Message.Length > 100 ? report.Message.Substring(0, 100) + "..." : report.Message,
                        FontSize = 11,
                        Foreground = Brush.Parse("#E6EDF3"),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };

                    panel.Children.Add(headerGrid);
                    panel.Children.Add(sourceText);
                    panel.Children.Add(messageText);

                    border.Child = panel;
                    errorList.Children.Add(border);
                }
            }
        }

        private void ShowErrorDetails(ErrorReportingService.ErrorReport report)
        {
            var borderDetails = this.FindControl<Border>("BorderErrorDetails");
            var detailsPanel = this.FindControl<StackPanel>("ErrorDetailsPanel");

            if (borderDetails != null && detailsPanel != null)
            {
                detailsPanel.Children.Clear();

                // ID
                AddDetailRow(detailsPanel, "ID:", report.Id);
                AddDetailRow(detailsPanel, "Timestamp:", report.Timestamp.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                AddDetailRow(detailsPanel, "Type:", report.ErrorType);
                AddDetailRow(detailsPanel, "Source:", report.Source);
                AddDetailRow(detailsPanel, "Critical:", report.IsCritical ? "Yes" : "No");
                AddDetailRow(detailsPanel, "Reported:", report.IsReported ? "Yes" : "No");
                AddDetailRow(detailsPanel, "Version:", report.ApplicationVersion);
                AddDetailRow(detailsPanel, "OS:", report.OperatingSystem);

                // Message
                var messageLabel = new TextBlock
                {
                    Text = "Message:",
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Foreground = Brush.Parse("#E6EDF3"),
                    Margin = new Avalonia.Thickness(0, 10, 0, 5)
                };
                detailsPanel.Children.Add(messageLabel);

                var messageText = new TextBlock
                {
                    Text = report.Message,
                    Foreground = Brush.Parse("#E6EDF3"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 0, 0, 10)
                };
                detailsPanel.Children.Add(messageText);

                // Stack Trace
                if (!string.IsNullOrEmpty(report.StackTrace))
                {
                    var stackLabel = new TextBlock
                    {
                        Text = "Stack Trace:",
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Foreground = Brush.Parse("#E6EDF3"),
                        Margin = new Avalonia.Thickness(0, 0, 0, 5)
                    };
                    detailsPanel.Children.Add(stackLabel);

                    var stackText = new TextBlock
                    {
                        Text = report.StackTrace,
                        Foreground = Brush.Parse("#8B949E"),
                        FontFamily = new Avalonia.Media.FontFamily("Consolas, monospace"),
                        FontSize = 10,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 0, 0, 10)
                    };
                    detailsPanel.Children.Add(stackText);
                }

                // Context
                if (report.Context.Count > 0)
                {
                    var contextLabel = new TextBlock
                    {
                        Text = "Context:",
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Foreground = Brush.Parse("#E6EDF3"),
                        Margin = new Avalonia.Thickness(0, 0, 0, 5)
                    };
                    detailsPanel.Children.Add(contextLabel);

                    foreach (var kvp in report.Context)
                    {
                        AddDetailRow(detailsPanel, $"{kvp.Key}:", kvp.Value);
                    }
                }

                borderDetails.IsVisible = true;
            }
        }

        private void AddDetailRow(StackPanel panel, string label, string value)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
            grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
            grid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
            grid.Margin = new Avalonia.Thickness(0, 2);
            
            var labelText = new TextBlock
            {
                Text = label,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = Brush.Parse("#8B949E"),
                Width = 100
            };

            var valueText = new TextBlock
            {
                Text = value,
                Foreground = Brush.Parse("#E6EDF3"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };

            grid.Children.Add(labelText);
            grid.Children.Add(valueText);
            panel.Children.Add(grid);
        }

        private void ExportReports()
        {
            try
            {
                var report = ErrorReportingService.ExportErrorReports();
                LogService.WriteSystemLog("Error reports exported", "Information", "ERRORREPORTS");
                NotificationService.ShowBackupToast("Export", "Error reports exported successfully", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Export failed: {ex.Message}", "Error", "ERRORREPORTS");
                NotificationService.ShowBackupToast("Export", "Failed to export reports", "Error");
            }
        }

        private void ClearAllErrors()
        {
            try
            {
                ErrorReportingService.ClearErrorReports();
                _ = LoadErrorReportsAsync();
                NotificationService.ShowBackupToast("Clear", "All error reports cleared", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Clear failed: {ex.Message}", "Error", "ERRORREPORTS");
            }
        }
    }
}
