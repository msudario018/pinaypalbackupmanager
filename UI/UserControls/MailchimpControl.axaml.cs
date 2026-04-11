using Avalonia.Controls;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class MailchimpControl : UserControl
    {
        private bool _abortRequested;
        private readonly BackupManager? _manager;
        private bool _isBusy;

        public MailchimpControl() : this(null) { }

        public MailchimpControl(BackupManager? manager)
        {
            _manager = manager;
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            if (_manager != null)
            {
                _manager.OnMailchimpAutoSyncRequested += () => {
                    if (!_isBusy) Avalonia.Threading.Dispatcher.UIThread.Post(async () => await StartFullBackupAsync("AUTO-SYNC"));
                };
                _manager.OnAutoScanTimersReset += OnAutoScanTimersReset;
                _manager.OnDailyScheduleUpdated += OnDailyScheduleUpdated;
            }
            
            this.FindControl<Button>("BtnRunFull")!.Click += async (s, e) => { NotificationService.ShowBackupToast("Mailchimp", "Starting full backup...", "Info"); await StartFullBackupAsync(); };
            this.FindControl<Button>("BtnCancel")!.Click += async (s, e) => await ConfirmCancelAsync();
            this.FindControl<Button>("BtnSyncCheck")!.Click += async (s, e) => { NotificationService.ShowBackupToast("Mailchimp", "Checking data freshness...", "Info"); await SyncCheckAsync(); };
            this.FindControl<Button>("BtnMembers")!.Click += async (s, e) => { NotificationService.ShowBackupToast("Mailchimp", "Starting Members export...", "Info"); await StartSpecificTaskAsync("Members"); };
            this.FindControl<Button>("BtnCampaigns")!.Click += async (s, e) => { NotificationService.ShowBackupToast("Mailchimp", "Starting Campaigns export...", "Info"); await StartSpecificTaskAsync("Campaigns"); };
            this.FindControl<Button>("BtnReports")!.Click += async (s, e) => { NotificationService.ShowBackupToast("Mailchimp", "Starting Reports export...", "Info"); await StartSpecificTaskAsync("Reports"); };
            this.FindControl<Button>("BtnMergeFields")!.Click += async (s, e) => { NotificationService.ShowBackupToast("Mailchimp", "Starting Merge Fields export...", "Info"); await StartSpecificTaskAsync("Merge_Fields"); };
            this.FindControl<Button>("BtnTags")!.Click += async (s, e) => { NotificationService.ShowBackupToast("Mailchimp", "Starting Tags export...", "Info"); await StartSpecificTaskAsync("Tags"); };
            this.FindControl<Button>("BtnClear")!.Click += async (s, e) => { 
                bool confirm = await NotificationService.ConfirmAsync("Are you sure you want to clear the terminal logs and log file?", "Clear Logs");
                if (confirm) {
                    LogService.ClearLogs(BackupConfig.McLogFile);
                    this.FindControl<TextBox>("TxtLogs")!.Clear(); 
                    LogService.WriteLiveLog("SYSTEM: Terminal and log file cleared by user.", BackupConfig.McLogFile, "Information", "MANUAL");
                    NotificationService.ShowBackupToast("Mailchimp", "Logs cleared.", "Info"); 
                }
            };
            this.FindControl<Button>("BtnViewLog")!.Click += (s, e) => { if (File.Exists(BackupConfig.McLogFile)) { System.Diagnostics.Process.Start("notepad.exe", BackupConfig.McLogFile); NotificationService.ShowBackupToast("Mailchimp", "Opened log file.", "Info"); } };

            LogService.OnNewLogEntry += (entry, file) => {
                if (file == BackupConfig.McLogFile) {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        var textBox = this.FindControl<TextBox>("TxtLogs")!;
                        // Prepend for latest on top
                        textBox.Text = entry + (string.IsNullOrEmpty(textBox.Text) ? "" : Environment.NewLine + textBox.Text);
                        textBox.CaretIndex = 0; // Scroll to top
                    });
                }
            };
            LoadInitialLogs();
        }

        private void OnAutoScanTimersReset()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var txtAuto = this.FindControl<TextBlock>("TxtAutoScan");
                if (txtAuto != null && _manager != null)
                {
                    var now = DateTime.Now;
                    var diff = _manager.NextMailchimpAutoScan - now;
                    txtAuto.Text = $"Auto-Scan: {(diff.TotalSeconds > 0 ? diff.ToString(@"hh\:mm\:ss") : "00:00:00")}";
                }
            });
        }

        private void OnDailyScheduleUpdated()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var txtDaily = this.FindControl<TextBlock>("TxtNextDaily");
                if (txtDaily != null)
                {
                    var now = DateTime.Now;
                    var mnlTime = now.AddHours(15); // UTC-7 to UTC+8 is +15 hours
                    var diff = BackupManager.NextMailchimpDailySyncMnl - mnlTime;
                    txtDaily.Text = $"Next Daily: {(diff.TotalSeconds > 0 ? diff.ToString(@"hh\:mm\:ss") : "00:00:00")}";
                }
            });
        }

        private void LoadInitialLogs()
        {
            var lines = LogService.ImportLatestLogs(BackupConfig.McLogFile).ToList();
            // lines are already latest first from LogService.ImportLatestLogs
            var textBox = this.FindControl<TextBox>("TxtLogs")!;
            textBox.Text = string.Join(Environment.NewLine, lines);
            textBox.CaretIndex = 0; // Ensure top view
        }

        private async Task StartFullBackupAsync(string trigger = "MANUAL")
        {
            // Reload config to ensure we have latest settings
            ConfigService.Load();
            
            SetBusy(true);
            _abortRequested = false;
            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            txtStatus.Text = "EXPORTING FULL...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#48a9c9");

            // Report global backup progress
            _manager?.ReportBackupProgress("Mailchimp", 0, "EXPORTING FULL...");

            // --- TIMER RESET & LOGGING ---
            if (_manager != null)
            {
                _manager.ResetMailchimpTimer();
                string resetMsg = $"TIMER: Mailchimp activity detected ({trigger}). Auto-Scan reset to 2 hours ({BackupManager.GetTzDate().AddHours(2):HH:mm:ss}).";
                LogService.WriteLiveLog(resetMsg, BackupConfig.McLogFile, "Information", trigger);
            }
            LogService.WriteLiveLog("SESSION: Starting Full Mailchimp Data Export...", BackupConfig.McLogFile, "Information", trigger);

            await Task.Run(async () =>
            {
                try
                {
                    if (_abortRequested) throw new OperationCanceledException();
                    // Cleanup old files based on retention setting
                    var retentionDays = ConfigService.Current.Operation.RetentionDays;
                    LogService.WriteLiveLog($"CLEANUP: Checking for Mailchimp files older than {retentionDays} days...", BackupConfig.McLogFile, "Information", trigger);
                    
                    var limitDate = BackupManager.GetTzDate().AddDays(-retentionDays);
                    var oldFiles = new DirectoryInfo(BackupConfig.MailchimpFolder).GetFiles("*", SearchOption.AllDirectories)
                        .Where(f => f.LastWriteTime < limitDate && f.Name != "backuplog.txt")
                        .ToList();

                    if (oldFiles.Count > 0)
                    {
                        LogService.WriteLiveLog($"CLEANUP: Found {oldFiles.Count} Mailchimp files older than {retentionDays} days. Removing...", BackupConfig.McLogFile, "Information", trigger);
                        foreach (var file in oldFiles)
                        {
                            if (_abortRequested) break;
                            try { file.Delete(); } catch { }
                        }
                    }

                    if (_abortRequested) throw new OperationCanceledException();

                    var mc = new MailchimpService(BackupConfig.McApiKey, BackupConfig.McAudienceId);
                    string[] tasks = ["Members", "Campaigns", "Reports", "Merge_Fields", "Tags"];
                    
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        if (_abortRequested)
                        {
                            LogService.WriteLiveLog("CANCELLED: Mailchimp export stopped by user.", BackupConfig.McLogFile, "Warning", trigger);
                            break;
                        }
                        
                        string task = tasks[i];
                        int pct = (i + 1) * 20;
                        
                        LogService.WriteLiveLog($"EXPORT: Fetching {task}...", BackupConfig.McLogFile, "Information", trigger);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            this.FindControl<TextBlock>("TxtFile")!.Text = $"Exporting {task}... ({pct}%)";
                            this.FindControl<ProgressBar>("ProgressBar")!.Value = pct;
                        });
                        // Report global backup progress
                        _manager?.ReportBackupProgress("Mailchimp", pct, $"Exporting {task}...");
                        
                        string result = await mc.RunSpecificTaskAsync(task, BackupConfig.MailchimpFolder);
                        LogService.WriteLiveLog(result, BackupConfig.McLogFile, "Information", trigger);
                    }

                    if (!_abortRequested)
                    {
                        var integrity = CheckIntegrity(BackupConfig.MailchimpFolder);
                        LogService.WriteLiveLog($"INTEGRITY: {integrity}", BackupConfig.McLogFile, "Information", trigger);
                        NotificationService.ShowBackupToast("Mailchimp Backup Done", $"Full export complete. {integrity}", "Success");
                    }
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = _abortRequested ? "CANCELLED" : "COMPLETE";
                        txtStatus.Foreground = _abortRequested ? Avalonia.Media.Brush.Parse("#F38BA8") : Avalonia.Media.Brush.Parse("#588157");
                        if (!_abortRequested) LogService.WriteLiveLog("COMPLETE: Full Mailchimp session finished successfully.", BackupConfig.McLogFile, "Information", trigger);
                    });
                    // Report global backup progress
                    if (!_abortRequested)
                    {
                        _manager?.ReportBackupProgress("Mailchimp", 100, "COMPLETE");
                        // Update Firebase timestamp
                        _ = SystemStatusService.UpdateMailchimpBackupTimestampAsync();
                        // Write backup history to Firebase
                        _ = SystemStatusService.WriteBackupHistoryAsync("mailchimp", "success");
                    }
                    else _manager?.ReportBackupProgress("Mailchimp", 0, "CANCELLED");
                }
                catch (OperationCanceledException)
                {
                    LogService.WriteLiveLog("CANCELLED: Mailchimp export cancelled by user.", BackupConfig.McLogFile, "Warning", trigger);
                    NotificationService.ShowBackupToast("Mailchimp Cancelled", "User cancelled the task.", "Warning");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "CANCELLED";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#dad7cd");
                    });
                    // Report global backup progress cancelled
                    _manager?.ReportBackupProgress("Mailchimp", 0, "CANCELLED");
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"ERROR: Full export failed - {ex.Message}", BackupConfig.McLogFile, "Error", trigger);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "EXPORT ERROR";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                    });
                    // Report global backup progress error
                    _manager?.ReportBackupProgress("Mailchimp", 0, "EXPORT ERROR");
                }
                finally
                {
                    if (_manager != null)
                    {
                        _ = _manager.RunHealthCheckAsync();
                    }
                }
            });
            SetBusy(false);
        }

        private async Task StartSpecificTaskAsync(string type)
        {
            if (_isBusy) return;
            SetBusy(true);
            _abortRequested = false;
            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            txtStatus.Text = $"EXPORTING {type.ToUpper()}...";
            
            // --- TIMER RESET ---
            _manager?.ResetMailchimpTimer();

            await Task.Run(async () =>
            {
                try
                {
                    if (_abortRequested) throw new OperationCanceledException();
                    var mc = new MailchimpService(BackupConfig.McApiKey, BackupConfig.McAudienceId);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        this.FindControl<TextBlock>("TxtFile")!.Text = $"Exporting {type}...";
                        this.FindControl<ProgressBar>("ProgressBar")!.IsIndeterminate = true;
                    });
                    
                    string result = await mc.RunSpecificTaskAsync(type, BackupConfig.MailchimpFolder);
                    LogService.WriteLiveLog(result, BackupConfig.McLogFile, "Information", "MANUAL");
                    
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = _abortRequested ? "CANCELLED" : "COMPLETE";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse(_abortRequested ? "#dad7cd" : "#588157");
                        this.FindControl<ProgressBar>("ProgressBar")!.IsIndeterminate = false;
                    });
                }
                catch (OperationCanceledException)
                {
                    LogService.WriteLiveLog($"CANCELLED: {type} export cancelled by user.", BackupConfig.McLogFile, "Warning", "MANUAL");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "CANCELLED";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#dad7cd");
                        this.FindControl<ProgressBar>("ProgressBar")!.IsIndeterminate = false;
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"ERROR: {type} export failed - {ex.Message}", BackupConfig.McLogFile, "Error", "MANUAL");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "EXPORT ERROR";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                        this.FindControl<ProgressBar>("ProgressBar")!.IsIndeterminate = false;
                    });
                }
            });
            SetBusy(false);
        }

        public bool IsBusy => _isBusy;
        public Task TriggerSyncCheckAsync() => SyncCheckAsync();

        private static string CheckIntegrity(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return "No local folder found.";
                var newest = new DirectoryInfo(folder)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f => f.Name != "backuplog.txt")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();
                if (newest == null) return "No backup files found.";
                if (newest.Length == 0) return $"WARNING: {newest.Name} is zero-byte!";
                return $"OK — {newest.Name} ({newest.Length / 1024.0:F1} KB)";
            }
            catch (Exception ex) { return $"WARNING: {ex.Message}"; }
        }

        public void RequestCancelFromShell()
        {
            if (!_isBusy) return;
            _abortRequested = true;
            LogService.WriteLiveLog("CANCEL: Cancel requested.", BackupConfig.McLogFile, "Warning", "SYSTEM");
        }

        private async Task ConfirmCancelAsync()
        {
            if (!_isBusy) return;
            bool ok = await NotificationService.ConfirmAsync("Cancel the current Mailchimp task?", "Confirm Cancel");
            if (!ok) return;

            RequestCancelFromShell();
            var txtStatus = this.FindControl<TextBlock>("TxtStatus");
            if (txtStatus != null)
            {
                txtStatus.Text = "CANCELLING...";
                txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F9E2AF");
            }
        }

        private async Task SyncCheckAsync(bool allowAutoSync = true)
        {
            SetBusy(true);
            
            // Reload config to ensure we have latest settings
            ConfigService.Load();
            
            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            var txtFile = this.FindControl<TextBlock>("TxtFile")!;
            txtStatus.Text = "SYNC CHECK...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#48a9c9");
            txtFile.Text = "Status: Checking local data freshness...";

            string statusText = "SYNC CHECK";
            string detailText = "Status: Idle";
            string colorHex = "#6C7086";
            string toastMessage = "Sync check finished.";
            string toastType = "Info";

            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(BackupConfig.MailchimpFolder))
                    {
                        statusText = "FOLDER MISSING";
                        detailText = "Status: Mailchimp folder not found.";
                        colorHex = "#F38BA8";
                        toastMessage = "Mailchimp local folder is missing.";
                        toastType = "Error";
                        return;
                    }

                    var nowUtc = DateTime.UtcNow;
                    var freshWindowUtc = nowUtc.AddHours(-25);
                    var allFiles = new DirectoryInfo(BackupConfig.MailchimpFolder).GetFiles("*", SearchOption.AllDirectories).ToList();

                    if (allFiles.Count == 0)
                    {
                        statusText = "OUTDATED";
                        detailText = "Status: No local Mailchimp data found.";
                        colorHex = "#F38BA8";
                        toastMessage = "No Mailchimp data found locally.";
                        toastType = "Warning";
                        return;
                    }

                    static DateTime GetFreshnessUtc(FileInfo file) =>
                        file.LastWriteTimeUtc > file.CreationTimeUtc ? file.LastWriteTimeUtc : file.CreationTimeUtc;

                    var latestFile = allFiles.OrderByDescending(GetFreshnessUtc).First();
                    var freshnessUtc = GetFreshnessUtc(latestFile);
                    if (freshnessUtc < freshWindowUtc)
                    {
                        statusText = "OUTDATED";
                        detailText = $"Latest data: {freshnessUtc:MM/dd HH:mm} UTC (Older than 24h)";
                        colorHex = "#F38BA8";
                        toastMessage = "Mailchimp data is older than 24 hours.";
                        toastType = "Warning";
                    }
                    else
                    {
                        statusText = "LATEST";
                        detailText = $"Local data is fresh: {freshnessUtc:MM/dd HH:mm} UTC";
                        colorHex = "#588157";
                        toastMessage = "Mailchimp data is up to date.";
                        toastType = "Info";
                    }
                }
                catch (Exception ex)
                {
                    statusText = "SYNC CHECK ERROR";
                    detailText = $"Status: {ex.Message}";
                    colorHex = "#F38BA8";
                    toastMessage = $"Sync check error: {ex.Message}";
                    toastType = "Error";
                }
            });

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                txtStatus.Text = statusText;
                txtStatus.Foreground = Avalonia.Media.Brush.Parse(colorHex);
                txtFile.Text = detailText;
            });
            NotificationService.ShowBackupToast("Mailchimp", toastMessage, toastType);

            if (allowAutoSync && statusText == "OUTDATED")
            {
                SetBusy(false);
                bool confirm = await NotificationService.ConfirmAsync(
                    "Mailchimp data is outdated. Do you want to sync now?",
                    "Sync Now?"
                );
                if (confirm)
                {
                    _ = StartFullBackupAsync("AUTO-SYNC");
                }
                return;
            }

            if (_manager != null)
            {
                _ = _manager.RunHealthCheckAsync();
            }

            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            this.FindControl<Button>("BtnRunFull")!.IsEnabled = !busy;
            this.FindControl<Button>("BtnCancel")!.IsEnabled = busy;
            this.FindControl<Button>("BtnSyncCheck")!.IsEnabled = !busy;
            this.FindControl<Button>("BtnMembers")!.IsEnabled = !busy;
            this.FindControl<Button>("BtnCampaigns")!.IsEnabled = !busy;
            this.FindControl<Button>("BtnReports")!.IsEnabled = !busy;
            this.FindControl<Button>("BtnMergeFields")!.IsEnabled = !busy;
            this.FindControl<Button>("BtnTags")!.IsEnabled = !busy;
            this.FindControl<ProgressBar>("ProgressBar")!.Value = 0;
            if (!busy) this.FindControl<TextBlock>("TxtFile")!.Text = "Status: Idle";
        }

        public void PerformSyncCheck()
        {
            _ = SyncCheckAsync(false);
        }

        public void StartFullBackupFromShell()
        {
            _ = StartFullBackupAsync("MANUAL");
        }
    }
}
