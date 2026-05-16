using Avalonia.Controls;
using Avalonia.Media;
using PinayPalBackupManager.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class BackupHistoryControl : UserControl
    {
        public BackupHistoryControl()
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
                btnRefresh.Click += async (s, e) => await LoadBackupHistoryAsync();
            }

            var btnApplyFilter = this.FindControl<Button>("BtnApplyFilter");
            if (btnApplyFilter != null)
            {
                btnApplyFilter.Click += async (s, e) => await LoadBackupHistoryAsync();
            }

            var btnClearHistory = this.FindControl<Button>("BtnClearHistory");
            if (btnClearHistory != null)
            {
                btnClearHistory.Click += (s, e) => ClearOldHistory();
            }

            var btnExport = this.FindControl<Button>("BtnExport");
            if (btnExport != null)
            {
                btnExport.Click += (s, e) => ExportReport();
            }

            var btnViewDetails = this.FindControl<Button>("BtnViewDetails");
            if (btnViewDetails != null)
            {
                btnViewDetails.Click += (s, e) => ViewSummary();
            }

            // Load initial data
            _ = LoadBackupHistoryAsync();
        }

        private void CloseWindow()
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            window?.Close();
        }

        private async Task LoadBackupHistoryAsync()
        {
            try
            {
                var cmbService = this.FindControl<ComboBox>("CmbService");
                var cmbStatus = this.FindControl<ComboBox>("CmbStatus");
                
                var filter = new BackupHistoryService.BackupHistoryFilter();
                
                if (cmbService != null && cmbService.SelectedIndex > 0)
                {
                    filter.Service = cmbService.SelectedItem?.ToString()?.ToLower();
                }
                
                if (cmbStatus != null && cmbStatus.SelectedIndex > 0)
                {
                    filter.Status = cmbStatus.SelectedItem?.ToString();
                }

                var history = BackupHistoryService.GetHistory(filter, 100);
                UpdateUI(history);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to load backup history: {ex.Message}", "Error", "BACKUPHISTORY");
            }
        }

        private void UpdateUI(System.Collections.Generic.List<BackupHistoryService.BackupHistoryEntry> history)
        {
            var txtSummary = this.FindControl<TextBlock>("TxtSummary");
            var backupList = this.FindControl<StackPanel>("BackupList");

            // Update summary
            var summary = BackupHistoryService.GetSummary();
            if (txtSummary != null)
            {
                txtSummary.Text = $"Total: {summary.TotalBackups} | Success: {summary.SuccessfulBackups} | Failed: {summary.FailedBackups} | Success Rate: {summary.SuccessRate:F1}%";
            }

            if (backupList != null)
            {
                backupList.Children.Clear();
                
                if (history.Count == 0)
                {
                    backupList.Children.Add(new TextBlock 
                    { 
                        Text = "No backup history found.", 
                        Foreground = Brush.Parse("#8B949E"), 
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 20)
                    });
                    return;
                }

                foreach (var entry in history)
                {
                    var border = new Border
                    {
                        Background = Brush.Parse("#0A000000"),
                        BorderBrush = GetStatusBrush(entry.Status),
                        BorderThickness = new Avalonia.Thickness(1),
                        CornerRadius = new Avalonia.CornerRadius(8),
                        Padding = new Avalonia.Thickness(12)
                    };

                    var panel = new StackPanel { Spacing = 8 };

                    // Header
                    var headerGrid = new Grid();
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    headerGrid.Margin = new Avalonia.Thickness(0, 0, 5, 0);
                    
                    var icon = new TextBlock
                    {
                        Text = GetStatusIcon(entry.Status),
                        FontSize = 12,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };

                    var serviceText = new TextBlock
                    {
                        Text = $"{entry.Service} - {entry.Type}",
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 12,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(8, 0, 0, 0)
                    };

                    var timeText = new TextBlock
                    {
                        Text = entry.Timestamp.ToString("MM/dd HH:mm"),
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E"),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                    };

                    Grid.SetColumn(icon, 0);
                    Grid.SetColumn(serviceText, 1);
                    Grid.SetColumn(timeText, 2);
                    
                    headerGrid.Children.Add(icon);
                    headerGrid.Children.Add(serviceText);
                    headerGrid.Children.Add(timeText);

                    // Details
                    var detailsGrid = new Grid();
                    detailsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    detailsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    detailsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    detailsGrid.Margin = new Avalonia.Thickness(0, 0, 5, 0);
                    
                    var durationText = new TextBlock
                    {
                        Text = $"Duration: {entry.Duration.TotalSeconds:F1}s",
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E")
                    };

                    var sizeText = new TextBlock
                    {
                        Text = entry.SizeBytes > 0 ? $"Size: {FormatBytes(entry.SizeBytes)}" : "",
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E"),
                        Margin = new Avalonia.Thickness(10, 0, 0, 0)
                    };

                    var filesText = new TextBlock
                    {
                        Text = entry.FilesCount > 0 ? $"Files: {entry.FilesCount}" : "",
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E"),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                    };

                    Grid.SetColumn(durationText, 0);
                    Grid.SetColumn(sizeText, 1);
                    Grid.SetColumn(filesText, 2);
                    
                    detailsGrid.Children.Add(durationText);
                    detailsGrid.Children.Add(sizeText);
                    detailsGrid.Children.Add(filesText);

                    // Error message if failed
                    if (entry.Status == "Failed" && !string.IsNullOrEmpty(entry.ErrorMessage))
                    {
                        var errorText = new TextBlock
                        {
                            Text = $"Error: {entry.ErrorMessage}",
                            FontSize = 10,
                            Foreground = Brush.Parse("#F85149"),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Avalonia.Thickness(0, 5, 5, 0)
                        };
                        panel.Children.Add(errorText);
                    }

                    panel.Children.Add(headerGrid);
                    panel.Children.Add(detailsGrid);

                    border.Child = panel;
                    backupList.Children.Add(border);
                }
            }
        }

        private void ClearOldHistory()
        {
            try
            {
                BackupHistoryService.ClearOldHistory(System.TimeSpan.FromDays(90));
                _ = LoadBackupHistoryAsync();
                NotificationService.ShowBackupToast("Clear", "Old backup history cleared", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Clear failed: {ex.Message}", "Error", "BACKUPHISTORY");
            }
        }

        private void ExportReport()
        {
            try
            {
                var report = BackupHistoryService.ExportHistoryReport();
                LogService.WriteSystemLog("Backup history exported", "Information", "BACKUPHISTORY");
                NotificationService.ShowBackupToast("Export", "Backup history exported successfully", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Export failed: {ex.Message}", "Error", "BACKUPHISTORY");
                NotificationService.ShowBackupToast("Export", "Failed to export history", "Error");
            }
        }

        private void ViewSummary()
        {
            try
            {
                var summary = BackupHistoryService.GetSummary();
                var message = $"Total Backups: {summary.TotalBackups}\n" +
                              $"Successful: {summary.SuccessfulBackups}\n" +
                              $"Failed: {summary.FailedBackups}\n" +
                              $"Success Rate: {summary.SuccessRate:F1}%\n" +
                              $"Last Backup: {summary.LastBackupTime:yyyy-MM-dd HH:mm:ss}\n" +
                              $"Last Successful: {summary.LastSuccessfulBackupTime:yyyy-MM-dd HH:mm:ss}\n" +
                              $"Total Size: {FormatBytes(summary.TotalSizeBytes)}\n" +
                              $"Average Duration: {summary.AverageDuration.TotalSeconds:F2}s";
                
                NotificationService.ShowBackupToast("Summary", message, "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Summary failed: {ex.Message}", "Error", "BACKUPHISTORY");
            }
        }

        private IBrush GetStatusBrush(string status)
        {
            return status switch
            {
                "Success" => Brush.Parse("#3FB950"),
                "Failed" => Brush.Parse("#F85149"),
                "Cancelled" => Brush.Parse("#D29922"),
                "InProgress" => Brush.Parse("#58A6FF"),
                _ => Brush.Parse("#8B949E")
            };
        }

        private string GetStatusIcon(string status)
        {
            return status switch
            {
                "Success" => "✅",
                "Failed" => "❌",
                "Cancelled" => "⚠️",
                "InProgress" => "⏳",
                _ => "📦"
            };
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
