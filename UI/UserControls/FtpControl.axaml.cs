using Avalonia.Controls;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;
using MsBox.Avalonia.Enums;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class FtpControl : UserControl
    {
        private bool _abortRequested;
        private readonly BackupManager? _manager;
        private FtpService? _activeFtp;

        public FtpControl() : this(null) { }
        public FtpControl(BackupManager? manager)
        {
            _manager = manager;
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            if (_manager != null)
            {
                _manager.OnFtpAutoSyncRequested += () => {
                    if (!_isBusy) Avalonia.Threading.Dispatcher.UIThread.Post(async () => await StartBackupAsync("AUTO-SYNC"));
                };
            }
            
            // Click handlers for all buttons
            this.FindControl<Button>("BtnStart")!.Click += async (s, e) => { NotificationService.ShowBackupToast("FTP", "Starting sync...", "Info"); await StartBackupAsync("MANUAL"); };
            this.FindControl<Button>("BtnCancel")!.Click += async (s, e) => { await ConfirmCancelAsync(); };
            this.FindControl<Button>("BtnSyncCheck")!.Click += async (s, e) => { NotificationService.ShowBackupToast("FTP", "Running sync check...", "Info"); await SyncCheckAsync(); };
            this.FindControl<Button>("BtnTest")!.Click += async (s, e) => { NotificationService.ShowBackupToast("FTP", "Testing connection...", "Info"); await TestFtpAsync(); };
            this.FindControl<Button>("BtnClear")!.Click += async (s, e) => { 
                bool confirm = await NotificationService.ConfirmAsync("Are you sure you want to clear the terminal logs and log file?", "Clear Logs");
                if (confirm) {
                    LogService.ClearLogs(BackupConfig.FtpLogFile);
                    this.FindControl<TextBox>("TxtLogs")!.Clear(); 
                    LogService.WriteLiveLog("SYSTEM: Terminal and log file cleared by user.", BackupConfig.FtpLogFile, "Information", "MANUAL");
                    NotificationService.ShowBackupToast("FTP", "Logs cleared.", "Info"); 
                }
            };
            this.FindControl<Button>("BtnViewLog")!.Click += (s, e) => { if (File.Exists(BackupConfig.FtpLogFile)) { System.Diagnostics.Process.Start("notepad.exe", BackupConfig.FtpLogFile); NotificationService.ShowBackupToast("FTP", "Opened log file.", "Info"); } };

            LogService.OnNewLogEntry += (entry, file) => { 
                if (file == BackupConfig.FtpLogFile) {
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

        private void LoadInitialLogs()
        {
            var lines = LogService.ImportLatestLogs(BackupConfig.FtpLogFile).ToList();
            // lines are already latest first from LogService.ImportLatestLogs
            var textBox = this.FindControl<TextBox>("TxtLogs")!;
            textBox.Text = string.Join(Environment.NewLine, lines);
            textBox.CaretIndex = 0; // Ensure top view
        }

        private async Task StartBackupAsync(string trigger = "MANUAL")
        {
            if (_isBusy) return;

            // Reload config to ensure we have latest settings
            ConfigService.Load();
            
            string taskError = string.Empty;
            NotificationService.ShowBackupToast("FTP Sync Started", $"Trigger: {trigger}. Checking pinaypal.net for updates...", "Info");

            SetBusy(true);
            _abortRequested = false;

            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            txtStatus.Text = "SYNCING...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#C77DFF");

            // Report global backup progress
            _manager?.ReportBackupProgress("FTP", 0, "SYNCING...");

            // --- TIMER RESET & LOGGING ---
            _manager?.ResetFtpTimer();
            string resetMsg = $"TIMER: FTP activity detected ({trigger}). Auto-Scan reset to 3 hours ({BackupManager.GetTzDate().AddHours(3):HH:mm:ss}).";
            LogService.WriteLiveLog(resetMsg, BackupConfig.FtpLogFile, "Information", trigger);
            LogService.WriteLiveLog("SESSION: Starting FTP Sync...", BackupConfig.FtpLogFile, "Information", trigger);

            var ftp = new FtpService();
            _activeFtp = ftp;
            await Task.Run(async () =>
            {
                try
                {
                    if (_abortRequested) throw new OperationCanceledException();

                    // --- WinSCP Cleanup (.filepart) ---
                    if (Directory.Exists(BackupConfig.FtpLocalFolder))
                    {
                        var leftovers = Directory.GetFiles(BackupConfig.FtpLocalFolder, "*.filepart", SearchOption.AllDirectories);
                        if (leftovers.Length > 0)
                        {
                            LogService.WriteLiveLog($"CLEANUP: Removing {leftovers.Length} leftover .filepart files...", BackupConfig.FtpLogFile, "Information", trigger);
                            foreach (var file in leftovers) { try { File.Delete(file); } catch { } }
                        }
                    }

                    string decryptedPass = SecurityService.GetDecryptedFtpPassword();
                    ftp.Initialize(BackupConfig.FtpHost, BackupConfig.FtpUser, decryptedPass, BackupConfig.FtpTlsFingerprint, BackupConfig.FtpPort);

                    LogService.WriteLiveLog("CONNECTING: Starting FTP Sync...", BackupConfig.FtpLogFile, "Information", trigger);
                    bool connected = await ftp.ConnectAsync();
                    if (_abortRequested) throw new OperationCanceledException();
                    if (connected)
                    {
                        if (_abortRequested) throw new OperationCanceledException();

                        // --- OLD BACKUP DELETE CHECK ---
                        var retentionDays = ConfigService.Current.Operation.RetentionDays;
                        LogService.WriteLiveLog($"CLEANUP: Checking for backups older than {retentionDays} days...", BackupConfig.FtpLogFile, "Information", trigger);

                        var limitDate = BackupManager.GetTzDate().AddDays(-retentionDays);
                        var oldFiles = new DirectoryInfo(BackupConfig.FtpLocalFolder).GetFiles("*", SearchOption.AllDirectories)
                            .Where(f => f.LastWriteTime < limitDate && f.Name != "backuplog.txt")
                            .ToList();

                        if (oldFiles.Count > 0)
                        {
                            LogService.WriteLiveLog($"CLEANUP: Found {oldFiles.Count} backups older than {retentionDays} days. Removing...", BackupConfig.FtpLogFile, "Information", trigger);
                            foreach (var file in oldFiles)
                            {
                                if (_abortRequested) throw new OperationCanceledException();
                                try
                                {
                                    LogService.WriteLiveLog($"DELETING OLD: {file.Name} (Last Modified: {file.LastWriteTime})", BackupConfig.FtpLogFile, "Information", trigger);
                                    file.Delete();
                                }
                                catch { }
                            }
                        }

                        LogService.WriteLiveLog("COMPARING: Checking for new or updated files...", BackupConfig.FtpLogFile, "Information", trigger);

                        var remoteFiles = ftp.ListFiles("/").Where(f => !f.IsDirectory).ToList();
                        int missingCount = 0;
                        foreach (var file in remoteFiles)
                        {
                            if (_abortRequested) throw new OperationCanceledException();
                            string localPath = Path.Combine(BackupConfig.FtpLocalFolder, file.Name);
                            if (!File.Exists(localPath)) missingCount++;
                            else if (new FileInfo(localPath).Length != file.Length) missingCount++;
                        }

                        if (missingCount > 0)
                        {
                            LogService.WriteLiveLog($"SYNCING: {missingCount} change(s) detected. Starting transfer...", BackupConfig.FtpLogFile, "Information", trigger);
                            await ftp.SynchronizeLocalAsync(BackupConfig.FtpLocalFolder, "/", (e) =>
                            {
                                if (_abortRequested) e.Cancel = true;
                                int pct = (int)(e.OverallProgress * 100);
                                string speed = e.CPS > 1048576 ? $"{Math.Round(e.CPS / 1048576.0, 2)} MB/s" : $"{Math.Round(e.CPS / 1024.0, 2)} KB/s";
                                string fileName = !string.IsNullOrEmpty(e.FileName) ? Path.GetFileName(e.FileName) : "file";

                                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                                    var pb = this.FindControl<ProgressBar>("ProgressBar");
                                    if (pb != null) pb.Value = pct;
                                    var tf = this.FindControl<TextBlock>("TxtFile");
                                    if (tf != null) tf.Text = $"Transferring {fileName} ({speed})";
                                });
                                // Report global backup progress
                                _manager?.ReportBackupProgress("FTP", pct, $"Transferring {fileName} ({speed})"); 
                            });

                            if (_abortRequested) throw new OperationCanceledException();
                            LogService.WriteLiveLog($"COMPLETE: {missingCount} backup(s) synchronized.", BackupConfig.FtpLogFile, "Information", trigger);
                            var integrity = CheckIntegrity(BackupConfig.FtpLocalFolder);
                            LogService.WriteLiveLog($"INTEGRITY: {integrity}", BackupConfig.FtpLogFile, "Information", trigger);
                            NotificationService.ShowBackupToast("FTP Sync Success", $"{missingCount} new backup(s) downloaded ({trigger}). {integrity}", "Success");
                        }
                        else
                        {
                            LogService.WriteLiveLog("STATUS: Local folder is up to date.", BackupConfig.FtpLogFile, "Information", trigger);
                            NotificationService.ShowBackupToast("FTP Sync Finished", "No new backups found. Local folder is up to date.", "Info");
                        }

                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            txtStatus.Text = "COMPLETE";
                            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#52B788");
                        });
                        // Report global backup progress complete
                        _manager?.ReportBackupProgress("FTP", 100, "COMPLETE");
                    }
                    else
                    {
                        taskError = "Authentication Error. Check credentials.";
                        LogService.WriteLiveLog($"LOGIN FAILED: {taskError}", BackupConfig.FtpLogFile, "Error", trigger);
                        NotificationService.ShowBackupToast("FTP Sync Failed", $"Error: {taskError}", "Error");
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            txtStatus.Text = "LOGIN FAILED";
                            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                        });
                        // Report global backup progress failed
                        _manager?.ReportBackupProgress("FTP", 0, "LOGIN FAILED");
                    }
                }
                catch (OperationCanceledException)
                {
                    taskError = "Cancelled by user.";
                    LogService.WriteLiveLog("CANCELLED: FTP task cancelled by user.", BackupConfig.FtpLogFile, "Warning", trigger);
                    NotificationService.ShowBackupToast("FTP Sync Cancelled", "User cancelled the task.", "Warning");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        txtStatus.Text = "CANCELLED";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#FAD643");
                    });
                    // Report global backup progress cancelled
                    _manager?.ReportBackupProgress("FTP", 0, "CANCELLED");
                }
                catch (Exception ex) when (_abortRequested && ex.Message.Contains("Aborted", StringComparison.OrdinalIgnoreCase))
                {
                    taskError = "Cancelled by user.";
                    LogService.WriteLiveLog("CANCELLED: FTP task cancelled by user.", BackupConfig.FtpLogFile, "Warning", trigger);
                    NotificationService.ShowBackupToast("FTP Sync Cancelled", "User cancelled the task.", "Warning");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        txtStatus.Text = "CANCELLED";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#FAD643");
                    });
                    // Report global backup progress cancelled
                    _manager?.ReportBackupProgress("FTP", 0, "CANCELLED");
                }
                catch (Exception ex)
                {
                    taskError = ex.Message;
                    LogService.WriteLiveLog($"STOPPED: Error detected - {taskError}", BackupConfig.FtpLogFile, "Warning", trigger);
                    NotificationService.ShowBackupToast("FTP Sync Failed", $"Error: {taskError}", "Error");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "SYNC ERROR";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                    });
                    // Report global backup progress error
                    _manager?.ReportBackupProgress("FTP", 0, "SYNC ERROR");
                }
                finally
                {
                    try { ftp.Dispose(); } catch { }
                    _activeFtp = null;

                    if (trigger == "MANUAL")
                    {
                        if (!string.IsNullOrEmpty(taskError))
                        {
                            if (string.Equals(taskError, "Cancelled by user.", StringComparison.OrdinalIgnoreCase))
                            {
                                await NotificationService.ShowMessageBoxAsync("FTP Sync was cancelled.", "Cancelled", ButtonEnum.Ok, Icon.Info);
                            }
                            else
                            {
                                await NotificationService.ShowMessageBoxAsync($"FTP Sync FAILED.\n\nError: {taskError}", "Task Error", ButtonEnum.Ok, Icon.Error);
                            }
                        }
                        else
                        {
                            await NotificationService.ShowMessageBoxAsync("FTP Sync Task Finished.\n\nAuto-Scan timer has been reset to 3 hours.", "Task Complete", ButtonEnum.Ok, Icon.Info);
                        }
                    }

                    if (_manager != null)
                    {
                        _ = _manager.RunHealthCheckAsync();
                    }
                }
            });

            SetBusy(false);
        }

        private async Task SyncCheckAsync(bool allowAutoSync = true)
        {
            SetBusy(true);
            
            // Reload config to ensure we have latest settings
            ConfigService.Load();
            
            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            var txtFile = this.FindControl<TextBlock>("TxtFile")!;
            txtStatus.Text = "SYNC CHECK...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#C77DFF");
            txtFile.Text = "Status: Comparing local vs remote...";
            
            string statusText = "SYNC CHECK";
            string detailText = "Status: Idle";
            string colorHex = "#C77DFF";
            string toastTitle = "FTP";
            string toastMessage = "Sync check finished.";
            string toastType = "Info";

            await Task.Run(async () =>
            {
                try
                {
                    using var ftp = new FtpService();
                    string decryptedPass = SecurityService.GetDecryptedFtpPassword();
                    ftp.Initialize(BackupConfig.FtpHost, BackupConfig.FtpUser, decryptedPass, BackupConfig.FtpTlsFingerprint, BackupConfig.FtpPort);

                    LogService.WriteLiveLog("SYNC CHECK: Comparing local vs remote latest backup...", BackupConfig.FtpLogFile, "Information", "MANUAL");

                    if (!await ftp.ConnectAsync())
                    {
                        statusText = "CONNECTION FAILED";
                        detailText = "Status: Unable to connect to FTP.";
                        colorHex = "#F38BA8";
                        toastMessage = "Sync check failed: connection error.";
                        toastType = "Error";
                        return;
                    }

                    var remoteLatest = ftp.ListFiles("/")
                        .Where(f => !f.IsDirectory)
                        .Where(f => f.Name.Contains("PinayPal", StringComparison.OrdinalIgnoreCase) && f.Name.Contains(".tar", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    FileInfo? localLatest = null;
                    if (Directory.Exists(BackupConfig.FtpLocalFolder))
                    {
                        localLatest = new DirectoryInfo(BackupConfig.FtpLocalFolder)
                            .EnumerateFiles("*PinayPal*.tar*", SearchOption.AllDirectories)
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault();
                    }

                    if (remoteLatest == null)
                    {
                        statusText = "REMOTE EMPTY";
                        detailText = "Status: No PinayPal .tar backup found on server.";
                        colorHex = "#FAD643";
                        toastMessage = "Remote has no PinayPal backups.";
                        toastType = "Warning";
                        return;
                    }

                    if (localLatest == null)
                    {
                        statusText = "OUTDATED";
                        detailText = $"Remote latest: {remoteLatest.Name} ({remoteLatest.LastWriteTime:MM/dd HH:mm} UTC) | Local: none";
                        colorHex = "#F38BA8";
                        toastMessage = "Local folder has no backups. Remote is newer.";
                        toastType = "Warning";
                        return;
                    }

                    var matchingLocal = new DirectoryInfo(BackupConfig.FtpLocalFolder)
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .FirstOrDefault(f => string.Equals(f.Name, remoteLatest.Name, StringComparison.OrdinalIgnoreCase));

                    bool hasRemoteFileLocally = matchingLocal != null;
                    long remoteSize = remoteLatest.Length;
                    long localSize = matchingLocal?.Length ?? -1;

                    LogService.WriteLiveLog($"SYNC CHECK: Remote file: {remoteLatest.Name}, Size: {remoteSize:n0} bytes", BackupConfig.FtpLogFile, "Information", "MANUAL");
                    LogService.WriteLiveLog($"SYNC CHECK: Local latest: {localLatest?.Name ?? "none"}, Size: {localLatest?.Length ?? 0:n0} bytes", BackupConfig.FtpLogFile, "Information", "MANUAL");
                    LogService.WriteLiveLog($"SYNC CHECK: Has remote file locally: {hasRemoteFileLocally}, Local size: {localSize:n0} bytes", BackupConfig.FtpLogFile, "Information", "MANUAL");

                    if (hasRemoteFileLocally && localSize == remoteSize)
                    {
                        statusText = "LATEST";
                        detailText = $"Local has latest remote: {remoteLatest.Name} ({remoteLatest.LastWriteTime:MM/dd HH:mm} UTC)";
                        colorHex = "#52B788";
                        toastMessage = "Local backup is up to date.";
                        toastType = "Info";
                        return;
                    }

                    if (hasRemoteFileLocally && localSize != remoteSize)
                    {
                        statusText = "SIZE MISMATCH";
                        detailText = $"Remote: {remoteLatest.Name} ({remoteSize:n0} bytes) | Local: {localSize:n0} bytes)";
                        colorHex = "#FAD643";
                        toastMessage = "Remote file exists locally but size differs.";
                        toastType = "Warning";
                        return;
                    }

                    // Fallback: if local file name matches remote and is recent (within 24 hours for timezone tolerance), consider it up to date
                    if (localLatest != null && string.Equals(localLatest.Name, remoteLatest.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        var timeDiff = remoteLatest.LastWriteTime - localLatest.LastWriteTimeUtc;
                        LogService.WriteLiveLog($"SYNC CHECK: Time difference = {timeDiff.TotalMinutes:F1} minutes (Remote: {remoteLatest.LastWriteTime:yyyy-MM-dd HH:mm:ss} UTC, Local: {localLatest.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC)", BackupConfig.FtpLogFile, "Information", "MANUAL");
                        
                        if (timeDiff.TotalMinutes <= 1440) // 24 hours to account for timezone differences
                        {
                            statusText = "LATEST";
                            detailText = $"Local matches remote: {localLatest.Name} (recent sync)";
                            colorHex = "#52B788";
                            toastMessage = "Local backup is up to date.";
                            toastType = "Info";
                            return;
                        }
                    }

                    statusText = "OUTDATED";
                    detailText = $"Remote latest: {remoteLatest.Name} ({remoteLatest.LastWriteTime:MM/dd HH:mm} UTC) | Local latest: {localLatest.Name} ({localLatest.LastWriteTimeUtc:MM/dd HH:mm} UTC)";
                    colorHex = "#F38BA8";
                    toastMessage = "Remote backup is newer than local.";
                    toastType = "Warning";
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

            string logLevel = toastType.Equals("Error", StringComparison.OrdinalIgnoreCase)
                ? "Error"
                : toastType.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                    ? "Warning"
                    : "Information";
            LogService.WriteLiveLog($"SYNC CHECK RESULT: {statusText} | {detailText}", BackupConfig.FtpLogFile, logLevel, "MANUAL");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                txtStatus.Text = statusText;
                txtStatus.Foreground = Avalonia.Media.Brush.Parse(colorHex);
                txtFile.Text = detailText;
            });
            NotificationService.ShowBackupToast(toastTitle, toastMessage, toastType);

            if (allowAutoSync && statusText == "OUTDATED")
            {
                SetBusy(false);
                _ = StartBackupAsync("AUTO-SYNC");
                return;
            }

            if (_manager != null)
            {
                _ = _manager.RunHealthCheckAsync();
            }

            SetBusy(false);
        }

        private async Task TestFtpAsync()
        {
            SetBusy(true);
            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            txtStatus.Text = "TESTING CONNECTION...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#C77DFF");

            using var ftp = new FtpService();
            string decryptedPass = SecurityService.GetDecryptedFtpPassword();
            ftp.Initialize(BackupConfig.FtpHost, BackupConfig.FtpUser, decryptedPass, BackupConfig.FtpTlsFingerprint, BackupConfig.FtpPort);
            
            if (await ftp.ConnectAsync())
            {
                LogService.WriteLiveLog("TEST SUCCESS: Connection Verified.", BackupConfig.FtpLogFile, "Information", "MANUAL");
                txtStatus.Text = "TEST SUCCESS";
                txtStatus.Foreground = Avalonia.Media.Brush.Parse("#52B788");
            }
            else
            {
                LogService.WriteLiveLog("TEST FAILED: Authentication Error.", BackupConfig.FtpLogFile, "Error", "MANUAL");
                txtStatus.Text = "TEST FAILED";
                txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
            }
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
                if (newest.Extension.Equals(".gz", StringComparison.OrdinalIgnoreCase) || newest.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    using var fs = File.OpenRead(newest.FullName);
                    using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
                    var buf = new byte[512];
                    gz.Read(buf, 0, buf.Length);
                }
                return $"OK — {newest.Name} ({newest.Length / 1024.0:F1} KB)";
            }
            catch (Exception ex) { return $"WARNING: {ex.Message}"; }
        }

        public void RequestCancelFromShell()
        {
            if (!_isBusy) return;
            _abortRequested = true;
            _activeFtp?.Abort();
            LogService.WriteLiveLog("CANCEL: Cancel requested.", BackupConfig.FtpLogFile, "Warning", "SYSTEM");
        }

        private async Task ConfirmCancelAsync()
        {
            if (!_isBusy) return;
            bool ok = await NotificationService.ConfirmAsync("Cancel the current FTP task?", "Confirm Cancel");
            if (!ok) return;

            RequestCancelFromShell();
            var txtStatus = this.FindControl<TextBlock>("TxtStatus");
            if (txtStatus != null)
            {
                txtStatus.Text = "CANCELLING...";
                txtStatus.Foreground = Avalonia.Media.Brush.Parse("#FAD643");
            }
        }

        private bool _isBusy;
        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            this.FindControl<Button>("BtnStart")!.IsEnabled = !busy;
            this.FindControl<Button>("BtnSyncCheck")!.IsEnabled = !busy;
            this.FindControl<Button>("BtnTest")!.IsEnabled = !busy;
            this.FindControl<Button>("BtnCancel")!.IsEnabled = busy;
            this.FindControl<ProgressBar>("ProgressBar")!.Value = 0;
            if (!busy) this.FindControl<TextBlock>("TxtFile")!.Text = "Status: Idle / Sync Finished";
        }

        public void PerformSyncCheck()
        {
            _ = SyncCheckAsync(false);
        }

        public void StartBackupFromShell()
        {
            _ = StartBackupAsync("MANUAL");
        }
    }
}
