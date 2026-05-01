using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class VerificationControl : UserControl
    {
        private readonly ObservableCollection<VerificationItem> _verificationItems;
        private bool _isVerifying = false;

        public VerificationControl()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            _verificationItems = new ObservableCollection<VerificationItem>();
            
            SetupEventHandlers();
            InitializeControl();
            LoadInitialData();
        }

        private void InitializeControl()
        {
            try
            {
                // Initialize ListBox
                var listBox = this.FindControl<ListBox>("VerificationDataGrid");
                if (listBox != null)
                {
                    listBox.ItemsSource = _verificationItems;
                    LogService.WriteLiveLog("[VERIFICATION] ListBox initialized successfully", "", "Information", "SYSTEM");
                    
                    // Add a test item to verify the ListBox is working
                    _verificationItems.Add(new VerificationItem
                    {
                        FileName = "test_file.zip",
                        FilePath = "C:\\test\\test_file.zip",
                        Service = "Test",
                        Status = "Valid",
                        FileSize = 1024 * 1024,
                        Created = DateTime.Now,
                        Hash = "abc123def456",
                        IsValid = true
                    });
                    
                    LogService.WriteLiveLog("[VERIFICATION] Added test item to ListBox", "", "Information", "SYSTEM");
                }
                else
                {
                    LogService.WriteLiveLog("[VERIFICATION] ERROR: VerificationDataGrid ListBox not found during initialization", "", "Error", "SYSTEM");
                }
                
                // Initialize filter checkboxes
                var chkShowValid = this.FindControl<CheckBox>("ChkShowValid");
                var chkShowCorrupted = this.FindControl<CheckBox>("ChkShowCorrupted");
                var chkShowMissing = this.FindControl<CheckBox>("ChkShowMissing");
                
                if (chkShowValid != null) chkShowValid.IsChecked = true;
                if (chkShowCorrupted != null) chkShowCorrupted.IsChecked = true;
                if (chkShowMissing != null) chkShowMissing.IsChecked = true;
                
                // Initialize filter count
                var filterCount = this.FindControl<TextBlock>("TxtFilterCount");
                if (filterCount != null) filterCount.Text = "0 files displayed";
                
                LogService.WriteLiveLog("[VERIFICATION] Control initialized successfully", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error initializing control: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private void SetupEventHandlers()
        {
            // Button handlers
            this.FindControl<Button>("BtnVerifyAll")!.Click += async (_, _) => await VerifyAllFilesAsync();
            this.FindControl<Button>("BtnGenerateChecksums")!.Click += async (_, _) => await GenerateAllChecksumsAsync();
            this.FindControl<Button>("BtnExportReport")!.Click += async (_, _) => await ExportVerificationReportAsync();
            this.FindControl<Button>("BtnRefreshResults")!.Click += async (_, _) => await RefreshResultsAsync();
            
            // Checkbox handlers
            this.FindControl<CheckBox>("ChkShowValid")!.IsCheckedChanged += (_, _) => FilterResults();
            this.FindControl<CheckBox>("ChkShowCorrupted")!.IsCheckedChanged += (_, _) => FilterResults();
            this.FindControl<CheckBox>("ChkShowMissing")!.IsCheckedChanged += (_, _) => FilterResults();
        }

        private async Task LoadInitialData()
        {
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Loading initial verification data...", "", "Information", "SYSTEM");
                await RefreshResultsAsync();
                UpdateStatusOverview();
                UpdateServiceStatus();
                LogService.WriteLiveLog("[VERIFICATION] Initial data loaded successfully", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error loading initial data: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private async Task VerifyAllFilesAsync()
        {
            if (_isVerifying) return;
            
            _isVerifying = true;
            var btnVerifyAll = this.FindControl<Button>("BtnVerifyAll");
            if (btnVerifyAll == null) return;
            
            btnVerifyAll.IsEnabled = false;
            btnVerifyAll.Content = "Verifying...";
            
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Starting comprehensive file verification...", "", "Information", "SYSTEM");
                
                // Verify each service with timeout protection
                var ftpTask = VerifyServiceAsync("FTP", "FtpProgressBar", "TxtFtpVerified");
                var mcTask = VerifyServiceAsync("Mailchimp", "McProgressBar", "TxtMcVerified");
                var sqlTask = VerifyServiceAsync("SQL", "SqlProgressBar", "TxtSqlVerified");
                
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5)); // 5 minute timeout
                var verificationTask = Task.WhenAll(ftpTask, mcTask, sqlTask);
                
                var completedTask = await Task.WhenAny(verificationTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    LogService.WriteLiveLog("[VERIFICATION] Verification timed out after 5 minutes", "", "Warning", "SYSTEM");
                    NotificationService.ShowBackupToast("Verification", "Verification timed out", "Warning");
                    return;
                }
                
                // Update overall status
                await RefreshResultsAsync();
                UpdateStatusOverview();
                UpdateServiceStatus();
                
                LogService.WriteLiveLog("[VERIFICATION] Comprehensive verification completed", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", "File verification completed", "Success");
            }
            catch (OperationCanceledException)
            {
                LogService.WriteLiveLog("[VERIFICATION] Verification was cancelled", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", "Verification cancelled", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error during verification: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", $"Error: {ex.Message}", "Error");
            }
            finally
            {
                _isVerifying = false;
                btnVerifyAll.IsEnabled = true;
                btnVerifyAll.Content = "Verify All";
            }
        }

        private async Task VerifyServiceAsync(string service, string progressBarName, string verifiedTextName)
        {
            var progressBar = this.FindControl<ProgressBar>(progressBarName);
            var verifiedText = this.FindControl<TextBlock>(verifiedTextName);
            var issuesListName = service.ToLower()[0] + "IssuesList";
            var issuesList = this.FindControl<StackPanel>(issuesListName);
            
            try
            {
                // Reset UI elements
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (progressBar != null) progressBar.Value = 0;
                    if (verifiedText != null) verifiedText.Text = "0/0";
                    if (issuesList != null) issuesList.Children.Clear();
                });
                
                var results = await ChecksumService.VerifyServiceChecksumsAsync(service);
                if (results == null || results.Count == 0)
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (verifiedText != null) verifiedText.Text = "No files";
                    });
                    return;
                }
                
                var total = results.Count;
                var processed = 0;
                var corruptedCount = 0;
                
                foreach (var result in results)
                {
                    processed++;
                    var progress = total > 0 ? (double)processed / total * 100 : 0;
                    
                    // Batch UI updates for better performance
                    if (processed % 10 == 0 || processed == total)
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (progressBar != null) progressBar.Value = progress;
                            if (verifiedText != null) verifiedText.Text = $"{processed}/{total}";
                        });
                    }
                    
                    if (!result.IsValid)
                    {
                        corruptedCount++;
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (issuesList != null)
                            {
                                var alert = CreateIssueAlert(result);
                                issuesList.Children.Add(alert);
                            }
                        });
                        
                        // Log corruption alert (batch to avoid spam)
                        if (corruptedCount <= 5)
                        {
                            LogService.WriteLiveLog($"[VERIFICATION] CORRUPTION DETECTED: {Path.GetFileName(result.FilePath)} - {result.Status}", "", "Error", "SYSTEM");
                        }
                    }
                    
                    // Reduce delay for better performance
                    if (processed % 50 == 0)
                        await Task.Delay(1);
                }
                
                // Log summary if many corrupted files
                if (corruptedCount > 5)
                {
                    LogService.WriteLiveLog($"[VERIFICATION] {corruptedCount} corrupted files detected in {service} service", "", "Error", "SYSTEM");
                }
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error verifying {service}: {ex.Message}", "", "Error", "SYSTEM");
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (verifiedText != null) verifiedText.Text = "Error";
                });
            }
        }

        private async Task GenerateAllChecksumsAsync()
        {
            var btnGenerate = this.FindControl<Button>("BtnGenerateChecksums");
            if (btnGenerate == null) return;
            
            btnGenerate.IsEnabled = false;
            btnGenerate.Content = "Generating...";
            
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Generating checksums for all backup files...", "", "Information", "SYSTEM");
                
                // Validate backup folders exist
                var ftpFolderExists = Directory.Exists(BackupConfig.FtpLocalFolder);
                var mcFolderExists = Directory.Exists(BackupConfig.MailchimpFolder);
                var sqlFolderExists = Directory.Exists(BackupConfig.SqlLocalFolder);
                
                if (!ftpFolderExists && !mcFolderExists && !sqlFolderExists)
                {
                    LogService.WriteLiveLog("[VERIFICATION] No backup folders found", "", "Warning", "SYSTEM");
                    NotificationService.ShowBackupToast("Verification", "No backup folders found", "Warning");
                    return;
                }
                
                // Generate checksums for each service folder with timeout
                var tasks = new List<Task>();
                
                if (ftpFolderExists)
                    tasks.Add(ChecksumService.SaveChecksumsForFolderAsync(BackupConfig.FtpLocalFolder, "FTP"));
                
                if (mcFolderExists)
                    tasks.Add(ChecksumService.SaveChecksumsForFolderAsync(BackupConfig.MailchimpFolder, "Mailchimp"));
                
                if (sqlFolderExists)
                    tasks.Add(ChecksumService.SaveChecksumsForFolderAsync(BackupConfig.SqlLocalFolder, "SQL"));
                
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(10)); // 10 minute timeout
                var checksumTask = Task.WhenAll(tasks);
                
                var completedTask = await Task.WhenAny(checksumTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    LogService.WriteLiveLog("[VERIFICATION] Checksum generation timed out", "", "Warning", "SYSTEM");
                    NotificationService.ShowBackupToast("Verification", "Checksum generation timed out", "Warning");
                    return;
                }
                
                await RefreshResultsAsync();
                UpdateStatusOverview();
                
                LogService.WriteLiveLog("[VERIFICATION] Checksum generation completed", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", "Checksum generation completed", "Success");
            }
            catch (UnauthorizedAccessException ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Access denied generating checksums: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", "Access denied - check folder permissions", "Error");
            }
            catch (DirectoryNotFoundException ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Backup folder not found: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", "Backup folder not found", "Error");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error generating checksums: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", $"Error: {ex.Message}", "Error");
            }
            finally
            {
                btnGenerate.IsEnabled = true;
                btnGenerate.Content = "Generate Checksums";
            }
        }

        private async Task RefreshResultsAsync()
        {
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Refreshing verification results...", "", "Information", "SYSTEM");
                
                var checksums = ChecksumService.LoadChecksums();
                LogService.WriteLiveLog($"[VERIFICATION] Loaded {checksums.Count} checksums", "", "Information", "SYSTEM");
                
                var verificationResults = await ChecksumService.VerifyAllChecksumsAsync();
                LogService.WriteLiveLog($"[VERIFICATION] Got {verificationResults.Count} verification results", "", "Information", "SYSTEM");
                
                _verificationItems.Clear();
                
                foreach (var result in verificationResults)
                {
                    var checksum = checksums.FirstOrDefault(c => c.FilePath == result.FilePath);
                    var item = new VerificationItem
                    {
                        FileName = Path.GetFileName(result.FilePath),
                        FilePath = result.FilePath,
                        Service = checksum?.Service ?? "Unknown",
                        Status = result.Status,
                        FileSize = checksum?.FileSize ?? 0,
                        Created = checksum?.Created ?? DateTime.MinValue,
                        Hash = checksum?.Hash ?? "",
                        IsValid = result.IsValid
                    };
                    
                    _verificationItems.Add(item);
                }
                
                LogService.WriteLiveLog($"[VERIFICATION] Created {_verificationItems.Count} verification items", "", "Information", "SYSTEM");
                
                var listBox = this.FindControl<ListBox>("VerificationDataGrid");
                if (listBox != null)
                {
                    listBox.ItemsSource = _verificationItems;
                    LogService.WriteLiveLog($"[VERIFICATION] Set ListBox ItemsSource with {_verificationItems.Count} items", "", "Information", "SYSTEM");
                }
                else
                {
                    LogService.WriteLiveLog("[VERIFICATION] ERROR: VerificationDataGrid ListBox not found", "", "Error", "SYSTEM");
                }
                
                FilterResults();
                
                // Update filter count
                var filterCount = this.FindControl<TextBlock>("TxtFilterCount");
                if (filterCount != null)
                {
                    filterCount.Text = $"{_verificationItems.Count} files displayed";
                }
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error refreshing results: {ex.Message}", "", "Error", "SYSTEM");
                
                // Show error state in UI
                var listBox = this.FindControl<ListBox>("VerificationDataGrid");
                if (listBox != null)
                {
                    listBox.ItemsSource = new List<VerificationItem>
                    {
                        new VerificationItem
                        {
                            FileName = "Error loading results",
                            FilePath = "",
                            Service = "Error",
                            Status = ex.Message,
                            FileSize = 0,
                            Created = DateTime.Now,
                            Hash = "",
                            IsValid = false
                        }
                    };
                }
            }
        }

        private void FilterResults()
        {
            try
            {
                var chkShowValid = this.FindControl<CheckBox>("ChkShowValid");
                var chkShowCorrupted = this.FindControl<CheckBox>("ChkShowCorrupted");
                var chkShowMissing = this.FindControl<CheckBox>("ChkShowMissing");
                
                var showValid = chkShowValid?.IsChecked == true;
                var showCorrupted = chkShowCorrupted?.IsChecked == true;
                var showMissing = chkShowMissing?.IsChecked == true;
                
                LogService.WriteLiveLog($"[VERIFICATION] Filter settings - Valid: {showValid}, Corrupted: {showCorrupted}, Missing: {showMissing}", "", "Information", "SYSTEM");
                LogService.WriteLiveLog($"[VERIFICATION] Total items before filtering: {_verificationItems.Count}", "", "Information", "SYSTEM");
                
                var filtered = _verificationItems.Where(item =>
                {
                    if (item.Status == "Valid" && showValid) return true;
                    if (item.Status == "Corrupted" && showCorrupted) return true;
                    if (item.Status == "File not found" && showMissing) return true;
                    return false;
                }).ToList();
                
                LogService.WriteLiveLog($"[VERIFICATION] Filtered items count: {filtered.Count}", "", "Information", "SYSTEM");
                
                var listBox = this.FindControl<ListBox>("VerificationDataGrid");
                if (listBox != null)
                {
                    listBox.ItemsSource = filtered;
                    LogService.WriteLiveLog($"[VERIFICATION] Set ListBox ItemsSource with {filtered.Count} filtered items", "", "Information", "SYSTEM");
                }
                else
                {
                    LogService.WriteLiveLog("[VERIFICATION] ERROR: VerificationDataGrid ListBox not found in FilterResults", "", "Error", "SYSTEM");
                }
                
                var filterCount = this.FindControl<TextBlock>("TxtFilterCount");
                if (filterCount != null)
                {
                    filterCount.Text = $"{filtered.Count} files displayed";
                }
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error filtering results: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private void UpdateStatusOverview()
        {
            try
            {
                var (valid, corrupted, missing) = ChecksumService.GetVerificationSummaryAsync().Result;
                var total = valid + corrupted + missing;
                
                // Update text values
                this.FindControl<TextBlock>("TxtTotalFiles")!.Text = total.ToString();
                this.FindControl<TextBlock>("TxtValidFiles")!.Text = valid.ToString();
                this.FindControl<TextBlock>("TxtCorruptedFiles")!.Text = corrupted.ToString();
                this.FindControl<TextBlock>("TxtMissingFiles")!.Text = missing.ToString();
                
                // Update progress bars
                if (total > 0)
                {
                    this.FindControl<ProgressBar>("ProgressTotal")!.Value = 100;
                    this.FindControl<ProgressBar>("ProgressValid")!.Value = (double)valid / total * 100;
                    this.FindControl<ProgressBar>("ProgressCorrupted")!.Value = (double)corrupted / total * 100;
                    this.FindControl<ProgressBar>("ProgressMissing")!.Value = (double)missing / total * 100;
                }
                
                // Show alerts section if there are issues
                var alertsSection = this.FindControl<Border>("AlertsSection");
                alertsSection!.IsVisible = corrupted > 0 || missing > 0;
                
                if (corrupted > 0 || missing > 0)
                {
                    UpdateAlertsSection(corrupted, missing);
                }
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error updating status overview: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private void UpdateServiceStatus()
        {
            try
            {
                var checksums = ChecksumService.LoadChecksums();
                var results = ChecksumService.VerifyAllChecksumsAsync().Result;
                
                // Update FTP status
                UpdateServiceStatusUI("FTP", "Ftp", checksums, results);
                
                // Update Mailchimp status
                UpdateServiceStatusUI("Mailchimp", "Mc", checksums, results);
                
                // Update SQL status
                UpdateServiceStatusUI("SQL", "Sql", checksums, results);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error updating service status: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private void UpdateServiceStatusUI(string serviceName, string prefix, List<ChecksumService.ChecksumRecord> checksums, List<ChecksumService.VerificationResult> results)
        {
            var serviceChecksums = checksums.Where(c => c.Service == serviceName).ToList();
            var serviceResults = results.Where(r => serviceChecksums.Any(c => c.FilePath == r.FilePath)).ToList();
            
            var statusText = this.FindControl<TextBlock>($"Txt{prefix}Status");
            var verifiedText = this.FindControl<TextBlock>($"Txt{prefix}Verified");
            
            var valid = serviceResults.Count(r => r.IsValid);
            var corrupted = serviceResults.Count(r => !r.IsValid && r.Status != "File not found");
            var missing = serviceResults.Count(r => r.Status == "File not found");
            var total = serviceResults.Count;
            
            verifiedText!.Text = $"{valid}/{total}";
            
            if (corrupted > 0 || missing > 0)
            {
                statusText!.Text = "⚠ Issues";
                statusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red);
            }
            else
            {
                statusText!.Text = "✓ OK";
                statusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Green);
            }
        }

        private void UpdateAlertsSection(int corrupted, int missing)
        {
            var alertsList = this.FindControl<StackPanel>("AlertsList");
            alertsList!.Children.Clear();
            
            if (corrupted > 0)
            {
                var corruptedAlert = CreateAlertItem("🔴 Corrupted Files", $"{corrupted} files have been corrupted and may need restoration", "Error");
                alertsList.Children.Add(corruptedAlert);
            }
            
            if (missing > 0)
            {
                var missingAlert = CreateAlertItem("🟡 Missing Files", $"{missing} files are no longer available at their expected locations", "Warning");
                alertsList.Children.Add(missingAlert);
            }
        }

        private Border CreateAlertItem(string title, string message, string type)
        {
            var color = type switch
            {
                "Error" => "#F38BA8",
                "Warning" => "#F9E2AF",
                _ => "#6C7086"
            };
            
            return new Border
            {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color)),
                CornerRadius = new Avalonia.CornerRadius(8),
                Padding = new Avalonia.Thickness(16, 12),
                Margin = new Avalonia.Thickness(0, 4),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 12, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.Black },
                        new TextBlock { Text = message, FontSize = 11, Foreground = Avalonia.Media.Brushes.Black, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                    }
                }
            };
        }

        private Border CreateIssueAlert(ChecksumService.VerificationResult result)
        {
            var icon = result.Status == "File not found" ? "🟡" : "🔴";
            var color = result.Status == "File not found" ? "#F9E2AF" : "#F38BA8";
            
            return new Border
            {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color)),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(8, 6),
                Margin = new Avalonia.Thickness(0, 2),
                Child = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = icon, FontSize = 10 },
                        new TextBlock { Text = Path.GetFileName(result.FilePath), FontSize = 10, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.Black }
                    }
                }
            };
        }

        private async Task ExportVerificationReportAsync()
        {
            try
            {
                var reportsFolder = Path.Combine(BackupConfig.FtpLocalFolder, "Reports");
                Directory.CreateDirectory(reportsFolder);
                
                var fileName = $"verification_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var filePath = Path.Combine(reportsFolder, fileName);
                
                var checksums = ChecksumService.LoadChecksums();
                var results = await ChecksumService.VerifyAllChecksumsAsync();
                
                using var writer = new StreamWriter(filePath);
                await writer.WriteLineAsync("File Name,Service,Status,File Size,Last Verified,SHA-256 Hash,File Path");
                
                foreach (var result in results)
                {
                    var checksum = checksums.FirstOrDefault(c => c.FilePath == result.FilePath);
                    var fileSize = checksum?.FileSize ?? 0;
                    var created = checksum?.Created ?? DateTime.MinValue;
                    var hash = checksum?.Hash ?? "";
                    var service = checksum?.Service ?? "Unknown";
                    
                    await writer.WriteLineAsync($"{Path.GetFileName(result.FilePath)},{service},{result.Status},{fileSize},{created:yyyy-MM-dd HH:mm:ss},{hash},{result.FilePath}");
                }
                
                LogService.WriteLiveLog($"[VERIFICATION] Report exported to: {filePath}", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", $"Report exported to {fileName}", "Success");
                
                // Open the file
                System.Diagnostics.Process.Start("notepad.exe", filePath);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error exporting report: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", $"Error exporting report: {ex.Message}", "Error");
            }
        }
    }

    public class VerificationItem
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string Service { get; set; } = "";
        public string Status { get; set; } = "";
        public long FileSize { get; set; }
        public DateTime Created { get; set; }
        public string Hash { get; set; } = "";
        public bool IsValid { get; set; }
        
        public string FileSizeFormatted => FileSize > 1024 * 1024 ? $"{FileSize / 1024.0 / 1024.0:F1} MB" : FileSize > 1024 ? $"{FileSize / 1024.0:F1} KB" : $"{FileSize} B";
        public string CreatedFormatted => Created == DateTime.MinValue ? "Never" : Created.ToString("yyyy-MM-dd HH:mm");
        public string HashShort => string.IsNullOrEmpty(Hash) ? "N/A" : Hash.Length > 16 ? Hash.Substring(0, 16) + "..." : Hash;
    }
}
