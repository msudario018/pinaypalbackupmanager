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
        private readonly HashSet<string> _shownNotifications = new();

        public VerificationControl()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            _verificationItems = new ObservableCollection<VerificationItem>();
            
            SetupEventHandlers();
            InitializeControl();
            _ = LoadInitialData();
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
            // Button handlers - with null checks to prevent crashes
            var btnVerifyAll = this.FindControl<Button>("BtnVerifyAll");
            if (btnVerifyAll != null) btnVerifyAll.Click += async (_, _) => await VerifyAllFilesAsync();
            
            var btnGenerateChecksums = this.FindControl<Button>("BtnGenerateChecksums");
            if (btnGenerateChecksums != null) btnGenerateChecksums.Click += async (_, _) => await GenerateAllChecksumsAsync();
            
            var btnExportReport = this.FindControl<Button>("BtnExportReport");
            if (btnExportReport != null) btnExportReport.Click += async (_, _) => await ExportVerificationReportAsync();
            
            var btnRestoreFiles = this.FindControl<Button>("BtnRestoreFiles");
            if (btnRestoreFiles != null) btnRestoreFiles.Click += async (_, _) => await RestoreCorruptedFilesAsync();
            
            var btnRefreshResults = this.FindControl<Button>("BtnRefreshResults");
            if (btnRefreshResults != null) btnRefreshResults.Click += async (_, _) => await RefreshResultsAsync();
            
            // Bulk selection handlers
            var btnSelectAll = this.FindControl<Button>("BtnSelectAll");
            if (btnSelectAll != null) btnSelectAll.Click += (_, _) => SelectAllFiles();
            
            var btnSelectCorrupted = this.FindControl<Button>("BtnSelectCorrupted");
            if (btnSelectCorrupted != null) btnSelectCorrupted.Click += (_, _) => SelectCorruptedFiles();
            
            var btnSelectMissing = this.FindControl<Button>("BtnSelectMissing");
            if (btnSelectMissing != null) btnSelectMissing.Click += (_, _) => SelectMissingFiles();
            
            // Checkbox handlers
            var chkShowValid = this.FindControl<CheckBox>("ChkShowValid");
            if (chkShowValid != null) chkShowValid.IsCheckedChanged += (_, _) => FilterResults();
            
            var chkShowCorrupted = this.FindControl<CheckBox>("ChkShowCorrupted");
            if (chkShowCorrupted != null) chkShowCorrupted.IsCheckedChanged += (_, _) => FilterResults();
            
            var chkShowMissing = this.FindControl<CheckBox>("ChkShowMissing");
            if (chkShowMissing != null) chkShowMissing.IsCheckedChanged += (_, _) => FilterResults();
        }

        private async Task LoadInitialData()
        {
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Loading initial verification data...", "", "Information", "SYSTEM");
                
                // Try to load last verification history first
                await LoadLastVerificationAsync();
                
                // Then refresh with current data
                await RefreshResultsAsync();
                await UpdateStatusOverview();
                await UpdateServiceStatus();
                LogService.WriteLiveLog("[VERIFICATION] Initial data loaded successfully", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error loading initial data: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private async Task LoadLastVerificationAsync()
        {
            try
            {
                var lastVerification = await VerificationHistoryService.LoadLastVerificationAsync("All");
                if (lastVerification == null)
                {
                    LogService.WriteLiveLog("[VERIFICATION] No previous verification history found", "", "Information", "SYSTEM");
                    return;
                }

                LogService.WriteLiveLog($"[VERIFICATION] Loading last verification from {lastVerification.FormattedTimestamp}", "", "Information", "SYSTEM");

                // Update status overview with last verification data
                var total = lastVerification.TotalFiles;
                var valid = lastVerification.ValidFiles;
                var corrupted = lastVerification.CorruptedFiles;
                var missing = lastVerification.MissingFiles;

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

                // Show notification about last verification
                if (corrupted > 0 || missing > 0)
                {
                    NotificationService.ShowBackupToast("Verification", 
                        $"Last check ({lastVerification.FormattedTimestamp}): {corrupted} corrupted, {missing} missing", "Warning");
                }
                else
                {
                    NotificationService.ShowBackupToast("Verification", 
                        $"Last check ({lastVerification.FormattedTimestamp}): All {valid} files valid", "Success");
                }

                // Load results into list
                _verificationItems.Clear();
                foreach (var result in lastVerification.Results)
                {
                    _verificationItems.Add(new VerificationItem
                    {
                        FileName = Path.GetFileName(result.FilePath),
                        FilePath = result.FilePath,
                        Service = "Unknown",
                        Status = result.Status,
                        FileSize = 0,
                        Created = lastVerification.Timestamp,
                        Hash = "",
                        IsValid = result.IsValid
                    });
                }

                FilterResults();
                LogService.WriteLiveLog($"[VERIFICATION] Loaded {lastVerification.Results.Count} items from history", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error loading last verification: {ex.Message}", "", "Debug", "SYSTEM");
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
            
            // Yield to UI thread to update button state
            await Task.Yield();
            
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Starting comprehensive file verification...", "", "Information", "SYSTEM");
                
                // Run verifications on thread pool to prevent UI blocking
                var ftpTask = Task.Run(() => VerifyServiceAsync("FTP", "FtpProgressBar", "TxtFtpVerified"));
                var mcTask = Task.Run(() => VerifyServiceAsync("Mailchimp", "McProgressBar", "TxtMcVerified"));
                var sqlTask = Task.Run(() => VerifyServiceAsync("SQL", "SqlProgressBar", "TxtSqlVerified"));
                
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
                await UpdateStatusOverview();
                await UpdateServiceStatus();
                
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
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (progressBar != null) progressBar.Value = 0;
                    if (verifiedText != null) verifiedText.Text = "0/0";
                    if (issuesList != null) issuesList.Children.Clear();
                });
                
                var results = await ChecksumService.VerifyServiceChecksumsAsync(service);
                
                // Check if files exist even if no checksums
                var actualFileCount = GetActualFileCount(service);
                
                if (results == null || results.Count == 0)
                {
                    if (actualFileCount > 0)
                    {
                        // Files exist but no checksums - show 100% progress
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (progressBar != null) 
                            {
                                progressBar.Value = 100;
                                progressBar.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#34D399"));
                            }
                            if (verifiedText != null) verifiedText.Text = $"{actualFileCount}/{actualFileCount}";
                        });
                        
                        // Show notification to generate checksums
                        NotificationService.ShowBackupToast(service, $"{actualFileCount} files found but no checksums. Click 'Generate Checksums' to verify file integrity.", "Warning");
                    }
                    else
                    {
                        // No files and no checksums
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (progressBar != null) progressBar.Value = 0;
                        });
                    }
                    return;
                }
                
                var total = results.Count;
                var corruptedCount = 0;
                var issueAlerts = new List<Control>();
                var logMessages = new List<string>();
                
                // Process results without blocking UI updates
                const int maxAlertsToShow = 50; // Limit UI alerts to prevent freeze
                foreach (var result in results)
                {
                    if (!result.IsValid)
                    {
                        corruptedCount++;
                        
                        // Collect issue alerts for batching (limit to prevent UI freeze)
                        if (issuesList != null && issueAlerts.Count < maxAlertsToShow)
                        {
                            issueAlerts.Add(CreateIssueAlert(result));
                        }
                        
                        // Collect log messages (batch to avoid spam)
                        if (corruptedCount <= 5)
                        {
                            logMessages.Add($"[VERIFICATION] CORRUPTION DETECTED: {Path.GetFileName(result.FilePath)} - {result.Status}");
                        }
                    }
                }
                
                // Single UI update at the end - fire and forget for speed
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (progressBar != null) progressBar.Value = 100;
                    if (verifiedText != null) verifiedText.Text = $"{total - corruptedCount}/{total}";
                    
                    // Batch add all issue alerts at once
                    if (issuesList != null && issueAlerts.Count > 0)
                    {
                        foreach (var alert in issueAlerts)
                        {
                            issuesList.Children.Add(alert);
                        }
                        
                        // Add a "more items" indicator if we hit the limit
                        if (corruptedCount > maxAlertsToShow)
                        {
                            issuesList.Children.Add(new Border
                            {
                                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6C7086")),
                                CornerRadius = new Avalonia.CornerRadius(6),
                                Padding = new Avalonia.Thickness(8, 6),
                                Margin = new Avalonia.Thickness(0, 2),
                                Child = new TextBlock 
                                { 
                                    Text = $"... and {corruptedCount - maxAlertsToShow} more corrupted files", 
                                    FontSize = 10, 
                                    Foreground = Avalonia.Media.Brushes.White 
                                }
                            });
                        }
                    }
                });
                
                // Log any pending messages
                foreach (var msg in logMessages)
                {
                    LogService.WriteLiveLog(msg, "", "Error", "SYSTEM");
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
                _ = Dispatcher.UIThread.InvokeAsync(() =>
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
                
                // Generate checksums for each service folder sequentially to avoid race conditions
                var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(10));
                try
                {
                    if (ftpFolderExists)
                        await ChecksumService.SaveChecksumsForFolderAsync(BackupConfig.FtpLocalFolder, "FTP");
                    
                    if (mcFolderExists)
                        await ChecksumService.SaveChecksumsForFolderAsync(BackupConfig.MailchimpFolder, "Mailchimp");
                    
                    if (sqlFolderExists)
                        await ChecksumService.SaveChecksumsForFolderAsync(BackupConfig.SqlLocalFolder, "SQL");
                }
                catch (OperationCanceledException)
                {
                    LogService.WriteLiveLog("[VERIFICATION] Checksum generation timed out", "", "Warning", "SYSTEM");
                    NotificationService.ShowBackupToast("Verification", "Checksum generation timed out", "Warning");
                    return;
                }
                
                await RefreshResultsAsync();
                await UpdateStatusOverview();
                
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
                        Service = ValidationService.GetStringOrDefault(checksum?.Service, "Unknown"),
                        Status = result.Status,
                        FileSize = ValidationService.GetValueOrDefault(checksum?.FileSize, 0),
                        Created = ValidationService.GetValueOrDefault(checksum?.Created, DateTime.MinValue),
                        Hash = ValidationService.GetStringOrDefault(checksum?.Hash),
                        IsValid = result.IsValid
                    };
                    
                    _verificationItems.Add(item);
                }
                
                LogService.WriteLiveLog($"[VERIFICATION] Created {_verificationItems.Count} verification items", "", "Information", "SYSTEM");
                
                // Save verification history
                await VerificationHistoryService.SaveVerificationAsync(verificationResults, "All");
                
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
                
                var showValid = chkShowValid != null && chkShowValid.IsChecked == true;
                var showCorrupted = chkShowCorrupted != null && chkShowCorrupted.IsChecked == true;
                var showMissing = chkShowMissing != null && chkShowMissing.IsChecked == true;
                
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

        private async Task UpdateStatusOverview()
        {
            try
            {
                var (valid, corrupted, missing) = await ChecksumService.GetVerificationSummaryAsync();
                var total = valid + corrupted + missing;
                
                // Update text values with null checks
                var txtTotalFiles = this.FindControl<TextBlock>("TxtTotalFiles");
                if (txtTotalFiles != null) txtTotalFiles.Text = total.ToString();
                
                var txtValidFiles = this.FindControl<TextBlock>("TxtValidFiles");
                if (txtValidFiles != null) txtValidFiles.Text = valid.ToString();
                
                var txtCorruptedFiles = this.FindControl<TextBlock>("TxtCorruptedFiles");
                if (txtCorruptedFiles != null) txtCorruptedFiles.Text = corrupted.ToString();
                
                var txtMissingFiles = this.FindControl<TextBlock>("TxtMissingFiles");
                if (txtMissingFiles != null) txtMissingFiles.Text = missing.ToString();
                
                // Update progress bars
                if (total > 0)
                {
                    var progressTotal = this.FindControl<ProgressBar>("ProgressTotal");
                    if (progressTotal != null) progressTotal.Value = 100;
                    
                    var progressValid = this.FindControl<ProgressBar>("ProgressValid");
                    if (progressValid != null) progressValid.Value = (double)valid / total * 100;
                    
                    var progressCorrupted = this.FindControl<ProgressBar>("ProgressCorrupted");
                    if (progressCorrupted != null) progressCorrupted.Value = (double)corrupted / total * 100;
                    
                    var progressMissing = this.FindControl<ProgressBar>("ProgressMissing");
                    if (progressMissing != null) progressMissing.Value = (double)missing / total * 100;
                }
                
                // Show alerts section if there are issues
                var alertsSection = this.FindControl<Border>("AlertsSection");
                if (alertsSection != null) alertsSection.IsVisible = corrupted > 0 || missing > 0;
                
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

        private async Task UpdateServiceStatus()
        {
            try
            {
                var checksums = ChecksumService.LoadChecksums();
                var results = await ChecksumService.VerifyAllChecksumsAsync();
                
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
            
            var valid = serviceResults.Count(r => r.IsValid && r.Status != "Cleaned by retention");
            var corrupted = serviceResults.Count(r => !r.IsValid && r.Status != "File not found" && r.Status != "Cleaned by retention");
            var missing = serviceResults.Count(r => r.Status == "File not found");
            var cleanedByRetention = serviceResults.Count(r => r.Status == "Cleaned by retention");
            var total = serviceResults.Count;
            
            // Get actual file count from folder when no checksums exist
            var actualFileCount = GetActualFileCount(serviceName);
            
            if (total == 0 && actualFileCount > 0)
            {
                // Files exist but no checksums generated yet
                verifiedText!.Text = $"{actualFileCount}/{actualFileCount}";
                statusText!.Text = "⚠ No Checksums";
                statusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F59E0B"));
                
                // Show notification to generate checksums (only once per session per service)
                var notificationKey = $"ChecksumWarning_{serviceName}";
                if (!_shownNotifications.Contains(notificationKey))
                {
                    _shownNotifications.Add(notificationKey);
                    NotificationService.ShowBackupToast(serviceName, $"{actualFileCount} files need checksums generated. Click 'Generate Checksums' button to enable verification.", "Warning");
                }
            }
            else
            {
                verifiedText!.Text = $"{valid}/{total}";

                if (total == 0)
                {
                    statusText!.Text = "— Empty";
                    statusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Gray);
                }
                else if (corrupted > 0 || missing > 0)
                {
                    statusText!.Text = "⚠ Issues";
                    statusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red);
                }
                else if (cleanedByRetention > 0 && valid == 0)
                {
                    // All files cleaned by retention - show as empty/OK
                    statusText!.Text = "✓ Clean";
                    statusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#34D399"));
                }
                else
                {
                    statusText!.Text = "✓ OK";
                    statusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#34D399"));
                }
            }
        }

        private int GetActualFileCount(string serviceName)
        {
            try
            {
                string? folder = serviceName switch
                {
                    "FTP" => BackupConfig.FtpLocalFolder,
                    "Mailchimp" => BackupConfig.MailchimpFolder,
                    "SQL" => BackupConfig.SqlLocalFolder,
                    _ => null
                };

                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    return 0;

                // Count files (excluding log and system files)
                return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Count(f => !f.EndsWith("backuplog.txt") && 
                               !f.EndsWith("checksums.json") &&
                               !Path.GetFileName(f).StartsWith("."));
            }
            catch
            {
                return 0;
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
                    var fileSize = ValidationService.GetValueOrDefault(checksum?.FileSize, 0);
                    var created = ValidationService.GetValueOrDefault(checksum?.Created, DateTime.MinValue);
                    var hash = ValidationService.GetStringOrDefault(checksum?.Hash);
                    var service = ValidationService.GetStringOrDefault(checksum?.Service, "Unknown");
                    
                    await writer.WriteLineAsync($"{Path.GetFileName(result.FilePath)},{service},{result.Status},{fileSize},{created:yyyy-MM-dd HH:mm:ss},{hash},{result.FilePath}");
                }
                
                LogService.WriteLiveLog($"[VERIFICATION] Report exported to: {filePath}", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", $"Report exported to {fileName}", "Success");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error exporting report: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Verification", $"Error exporting report: {ex.Message}", "Error");
            }
        }

        private void SelectAllFiles()
        {
            try
            {
                var selectedCount = 0;
                foreach (var item in _verificationItems)
                {
                    item.IsSelected = true;
                    selectedCount++;
                }
                LogService.WriteLiveLog($"[VERIFICATION] Selected all {selectedCount} files", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Selection", $"Selected {selectedCount} files", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error selecting all files: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private void SelectCorruptedFiles()
        {
            try
            {
                var selectedCount = 0;
                foreach (var item in _verificationItems)
                {
                    item.IsSelected = item.Status == "Corrupted";
                    if (item.IsSelected) selectedCount++;
                }
                LogService.WriteLiveLog($"[VERIFICATION] Selected {selectedCount} corrupted files", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Selection", $"Selected {selectedCount} corrupted files", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error selecting corrupted files: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private void SelectMissingFiles()
        {
            try
            {
                var selectedCount = 0;
                foreach (var item in _verificationItems)
                {
                    item.IsSelected = item.Status == "File not found";
                    if (item.IsSelected) selectedCount++;
                }
                LogService.WriteLiveLog($"[VERIFICATION] Selected {selectedCount} missing files", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Selection", $"Selected {selectedCount} missing files", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Error selecting missing files: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private async Task RestoreSelectedFilesAsync()
        {
            try
            {
                var selectedItems = _verificationItems.Where(i => i.IsSelected).ToList();
                if (selectedItems.Count == 0)
                {
                    NotificationService.ShowBackupToast("Recovery", "No files selected for recovery", "Warning");
                    return;
                }

                LogService.WriteLiveLog($"[VERIFICATION] Starting recovery for {selectedItems.Count} selected files...", "", "Information", "SYSTEM");
                
                var btnRestore = this.FindControl<Button>("BtnRestoreFiles");
                if (btnRestore != null)
                {
                    btnRestore.IsEnabled = false;
                    btnRestore.Content = "Restoring...";
                }

                var corruptedFiles = selectedItems.Where(i => i.Status == "Corrupted").ToList();
                var missingFiles = selectedItems.Where(i => i.Status == "File not found").ToList();

                // Create verification results from selected items
                var corruptedResults = corruptedFiles.Select(f => new ChecksumService.VerificationResult
                {
                    FilePath = f.FilePath,
                    IsValid = false,
                    Status = "Corrupted"
                }).ToList();

                var missingResults = missingFiles.Select(f => new ChecksumService.VerificationResult
                {
                    FilePath = f.FilePath,
                    IsValid = false,
                    Status = "File not found"
                }).ToList();

                // Restore corrupted files by re-downloading from sources
                await RestoreFromSourcesAsync(corruptedResults);
                
                // Handle missing files by triggering backup operations
                await RestoreMissingFilesAsync(missingResults);
                
                // Regenerate checksums after recovery
                await Task.Delay(1000);
                await GenerateAllChecksumsAsync();
                
                // Refresh results
                await RefreshResultsAsync();
                await UpdateStatusOverview();
                await UpdateServiceStatus();
                
                // Clear selections
                foreach (var item in _verificationItems)
                {
                    item.IsSelected = false;
                }
                
                LogService.WriteLiveLog($"[VERIFICATION] Recovery completed for {selectedItems.Count} selected files", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Recovery", $"Restored {selectedItems.Count} selected files", "Success");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Recovery failed: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Recovery", $"Recovery failed: {ex.Message}", "Error");
            }
            finally
            {
                var btnRestore = this.FindControl<Button>("BtnRestoreFiles");
                if (btnRestore != null)
                {
                    btnRestore.IsEnabled = true;
                    btnRestore.Content = "Restore Files";
                }
            }
        }

        private async Task RestoreCorruptedFilesAsync()
        {
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Starting file recovery process...", "", "Information", "SYSTEM");
                
                var results = await ChecksumService.VerifyAllChecksumsAsync();
                var corruptedFiles = results.Where(r => !r.IsValid && r.Status != "File not found").ToList();
                var missingFiles = results.Where(r => r.Status == "File not found").ToList();
                
                if (corruptedFiles.Count == 0 && missingFiles.Count == 0)
                {
                    LogService.WriteLiveLog("[VERIFICATION] No corrupted or missing files found", "", "Information", "SYSTEM");
                    NotificationService.ShowBackupToast("Recovery", "No files need recovery", "Info");
                    return;
                }
                
                var btnRestore = this.FindControl<Button>("BtnRestoreFiles");
                if (btnRestore != null)
                {
                    btnRestore.IsEnabled = false;
                    btnRestore.Content = "Restoring...";
                }
                
                // Restore corrupted files by re-downloading from sources
                await RestoreFromSourcesAsync(corruptedFiles);
                
                // Handle missing files by triggering backup operations
                await RestoreMissingFilesAsync(missingFiles);
                
                // Regenerate checksums after recovery
                await Task.Delay(1000); // Brief pause for file operations to complete
                await GenerateAllChecksumsAsync();
                
                // Refresh results
                await RefreshResultsAsync();
                await UpdateStatusOverview();
                await UpdateServiceStatus();
                
                LogService.WriteLiveLog($"[VERIFICATION] Recovery completed: {corruptedFiles.Count} corrupted, {missingFiles.Count} missing files processed", "", "Information", "SYSTEM");
                NotificationService.ShowBackupToast("Recovery", $"Restored {corruptedFiles.Count} corrupted and {missingFiles.Count} missing files", "Success");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Recovery failed: {ex.Message}", "", "Error", "SYSTEM");
                NotificationService.ShowBackupToast("Recovery", $"Recovery failed: {ex.Message}", "Error");
            }
            finally
            {
                var btnRestore = this.FindControl<Button>("BtnRestoreFiles");
                if (btnRestore != null)
                {
                    btnRestore.IsEnabled = true;
                    btnRestore.Content = "Restore Files";
                }
            }
        }

        private async Task RestoreFromSourcesAsync(List<ChecksumService.VerificationResult> corruptedFiles)
        {
            foreach (var corruptedFile in corruptedFiles)
            {
                try
                {
                    var serviceName = DetermineServiceFromPath(corruptedFile.FilePath);
                    
                    switch (serviceName)
                    {
                        case "FTP":
                            await RestoreFromFtpAsync(corruptedFile);
                            break;
                        case "Mailchimp":
                            await RestoreFromMailchimpAsync(corruptedFile);
                            break;
                        case "SQL":
                            await RestoreFromSqlAsync(corruptedFile);
                            break;
                        default:
                            LogService.WriteLiveLog($"[VERIFICATION] Unknown service for file: {corruptedFile.FilePath}", "", "Warning", "SYSTEM");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[VERIFICATION] Failed to restore {corruptedFile.FilePath}: {ex.Message}", "", "Error", "SYSTEM");
                }
            }
        }

        private async Task RestoreMissingFilesAsync(List<ChecksumService.VerificationResult> missingFiles)
        {
            foreach (var missingFile in missingFiles)
            {
                try
                {
                    var serviceName = DetermineServiceFromPath(missingFile.FilePath);
                    
                    switch (serviceName)
                    {
                        case "FTP":
                            await TriggerFtpBackupAsync();
                            break;
                        case "Mailchimp":
                            await TriggerMailchimpBackupAsync();
                            break;
                        case "SQL":
                            await TriggerSqlBackupAsync();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"[VERIFICATION] Failed to trigger backup for {missingFile.FilePath}: {ex.Message}", "", "Error", "SYSTEM");
                }
            }
        }

        private string DetermineServiceFromPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "Unknown";
            
            if (filePath.Contains(BackupConfig.FtpLocalFolder, StringComparison.OrdinalIgnoreCase)) return "FTP";
            if (filePath.Contains(BackupConfig.MailchimpFolder, StringComparison.OrdinalIgnoreCase)) return "Mailchimp";
            if (filePath.Contains(BackupConfig.SqlLocalFolder, StringComparison.OrdinalIgnoreCase)) return "SQL";
            
            return "Unknown";
        }

        private async Task RestoreFromFtpAsync(ChecksumService.VerificationResult corruptedFile)
        {
            try
            {
                LogService.WriteLiveLog($"[VERIFICATION] Restoring FTP file: {Path.GetFileName(corruptedFile.FilePath)}", "", "Information", "SYSTEM");
                
                // Trigger FTP backup operation
                using var ftpService = new Services.FtpService();
                string decryptedPass = SecurityService.GetDecryptedFtpPassword();
                ftpService.Initialize(BackupConfig.FtpHost, BackupConfig.FtpUser, decryptedPass, BackupConfig.FtpTlsFingerprint, BackupConfig.FtpPort);
                if (await ftpService.ConnectAsync())
                {
                    await ftpService.SynchronizeLocalAsync(BackupConfig.FtpLocalFolder, "/", (e) => { });
                }
                
                LogService.WriteLiveLog($"[VERIFICATION] FTP restore completed for: {Path.GetFileName(corruptedFile.FilePath)}", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] FTP restore failed: {ex.Message}", "", "Error", "SYSTEM");
                throw;
            }
        }

        private async Task RestoreFromMailchimpAsync(ChecksumService.VerificationResult corruptedFile)
        {
            try
            {
                LogService.WriteLiveLog($"[VERIFICATION] Restoring Mailchimp file: {Path.GetFileName(corruptedFile.FilePath)}", "", "Information", "SYSTEM");
                
                // Trigger Mailchimp backup operation - run all tasks
                var mailchimpService = new Services.MailchimpService(Services.SecurityService.GetDecryptedMailchimpApiKey(), BackupConfig.McAudienceId);
                string[] tasks = ["Members", "Campaigns", "Reports", "Merge_Fields", "Tags"];
                foreach (var task in tasks)
                {
                    await mailchimpService.RunSpecificTaskAsync(task, BackupConfig.MailchimpFolder);
                }
                
                LogService.WriteLiveLog($"[VERIFICATION] Mailchimp restore completed for: {Path.GetFileName(corruptedFile.FilePath)}", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Mailchimp restore failed: {ex.Message}", "", "Error", "SYSTEM");
                throw;
            }
        }

        private async Task RestoreFromSqlAsync(ChecksumService.VerificationResult corruptedFile)
        {
            try
            {
                LogService.WriteLiveLog($"[VERIFICATION] Restoring SQL file: {Path.GetFileName(corruptedFile.FilePath)}", "", "Information", "SYSTEM");
                
                // Trigger SQL backup operation
                using var sqlService = new Services.SqlService();
                string decryptedPass = SecurityService.GetDecryptedSqlPassword();
                sqlService.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, decryptedPass, BackupConfig.SqlTlsFingerprint);
                if (await sqlService.ConnectAsync())
                {
                    await sqlService.SynchronizeLocalAsync(BackupConfig.SqlLocalFolder, BackupConfig.SqlRemotePath, (e) => { });
                }
                
                LogService.WriteLiveLog($"[VERIFICATION] SQL restore completed for: {Path.GetFileName(corruptedFile.FilePath)}", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] SQL restore failed: {ex.Message}", "", "Error", "SYSTEM");
                throw;
            }
        }

        private async Task TriggerFtpBackupAsync()
        {
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Triggering FTP backup for missing files", "", "Information", "SYSTEM");
                
                using var ftpService = new Services.FtpService();
                string decryptedPass = SecurityService.GetDecryptedFtpPassword();
                ftpService.Initialize(BackupConfig.FtpHost, BackupConfig.FtpUser, decryptedPass, BackupConfig.FtpTlsFingerprint, BackupConfig.FtpPort);
                if (await ftpService.ConnectAsync())
                {
                    await ftpService.SynchronizeLocalAsync(BackupConfig.FtpLocalFolder, "/", (e) => { });
                }
                
                LogService.WriteLiveLog("[VERIFICATION] FTP backup triggered successfully", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Failed to trigger FTP backup: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private async Task TriggerMailchimpBackupAsync()
        {
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Triggering Mailchimp backup for missing files", "", "Information", "SYSTEM");
                
                var mailchimpService = new Services.MailchimpService(Services.SecurityService.GetDecryptedMailchimpApiKey(), BackupConfig.McAudienceId);
                string[] tasks = ["Members", "Campaigns", "Reports", "Merge_Fields", "Tags"];
                foreach (var task in tasks)
                {
                    await mailchimpService.RunSpecificTaskAsync(task, BackupConfig.MailchimpFolder);
                }
                
                LogService.WriteLiveLog("[VERIFICATION] Mailchimp backup triggered successfully", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Failed to trigger Mailchimp backup: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private async Task TriggerSqlBackupAsync()
        {
            try
            {
                LogService.WriteLiveLog("[VERIFICATION] Triggering SQL backup for missing files", "", "Information", "SYSTEM");
                
                using var sqlService = new Services.SqlService();
                string decryptedPass = SecurityService.GetDecryptedSqlPassword();
                sqlService.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, decryptedPass, BackupConfig.SqlTlsFingerprint);
                if (await sqlService.ConnectAsync())
                {
                    await sqlService.SynchronizeLocalAsync(BackupConfig.SqlLocalFolder, BackupConfig.SqlRemotePath, (e) => { });
                }
                
                LogService.WriteLiveLog("[VERIFICATION] SQL backup triggered successfully", "", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[VERIFICATION] Failed to trigger SQL backup: {ex.Message}", "", "Error", "SYSTEM");
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
        public bool IsSelected { get; set; } = false;
        
        public string FileSizeFormatted => FileSize > 1024 * 1024 ? $"{FileSize / 1024.0 / 1024.0:F1} MB" : FileSize > 1024 ? $"{FileSize / 1024.0:F1} KB" : $"{FileSize} B";
        public string CreatedFormatted => Created == DateTime.MinValue ? "Never" : Created.ToString("yyyy-MM-dd HH:mm");
        public string HashShort => string.IsNullOrEmpty(Hash) ? "N/A" : Hash.Length > 16 ? Hash.Substring(0, 16) + "..." : Hash;
    }
}
