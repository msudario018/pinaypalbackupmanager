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
    public partial class SqlControl : UserControl
    {
        private bool _abortRequested;
        private readonly BackupManager? _manager;
        private SqlService? _activeSql;

        public SqlControl() : this(null) { }
        public SqlControl(BackupManager? manager)
        {
            _manager = manager;
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            if (_manager != null)
            {
                _manager.OnSqlAutoSyncRequested += () => {
                    if (!_isBusy) Avalonia.Threading.Dispatcher.UIThread.Post(async () => await StartBackupAsync("AUTO-SYNC"));
                };
            }
            
            this.FindControl<Button>("BtnStart")!.Click += async (s, e) => await StartBackupAsync();
            this.FindControl<Button>("BtnCancel")!.Click += async (s, e) => await ConfirmCancelAsync();
            this.FindControl<Button>("BtnSyncCheck")!.Click += async (s, e) => { NotificationService.ShowBackupToast("SQL", "Checking server...", "Info"); await SyncCheckAsync(); };
            this.FindControl<Button>("BtnTest")!.Click += async (s, e) => { NotificationService.ShowBackupToast("SQL", "Testing connection...", "Info"); await TestSqlAsync(); };
            this.FindControl<Button>("BtnClear")!.Click += async (s, e) => { 
                bool confirm = await NotificationService.ConfirmAsync("Are you sure you want to clear the terminal logs and log file?", "Clear Logs");
                if (confirm) {
                    LogService.ClearLogs(BackupConfig.SqlLogFile);
                    this.FindControl<TextBox>("TxtLogs")!.Clear(); 
                    LogService.WriteLiveLog("SYSTEM: Terminal and log file cleared by user.", BackupConfig.SqlLogFile, "Information", "MANUAL");
                    NotificationService.ShowBackupToast("SQL", "Logs cleared.", "Info"); 
                }
            };
            this.FindControl<Button>("BtnViewLog")!.Click += (s, e) => { if (File.Exists(BackupConfig.SqlLogFile)) { System.Diagnostics.Process.Start("notepad.exe", BackupConfig.SqlLogFile); NotificationService.ShowBackupToast("SQL", "Opened log file.", "Info"); } };

            LogService.OnNewLogEntry += (entry, file) => { 
                if (file == BackupConfig.SqlLogFile) {
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
            var lines = LogService.ImportLatestLogs(BackupConfig.SqlLogFile).ToList();
            // lines are already latest first from LogService.ImportLatestLogs
            var textBox = this.FindControl<TextBox>("TxtLogs")!;
            textBox.Text = string.Join(Environment.NewLine, lines);
            textBox.CaretIndex = 0; // Ensure top view
        }

        private async Task StartBackupAsync(string trigger = "MANUAL")
        {
            if (_isBusy) return;

            string taskError = string.Empty;
            NotificationService.ShowBackupToast("SQL Backup Started", $"Trigger: {trigger}. Connecting to MySQL staged folder...", "Info");

            SetBusy(true);
            _abortRequested = false;

            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            txtStatus.Text = "SYNCING SQL...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F9E2AF");

            // --- TIMER RESET & LOGGING ---
            _manager?.ResetSqlTimer();
            var next = BackupManager.GetTzDate().AddHours(2).AddMinutes(15);
            string resetMsg = $"TIMER: SQL activity detected ({trigger}). Auto-Scan reset to 2h 15m ({next:HH:mm:ss}).";
            LogService.WriteLiveLog(resetMsg, BackupConfig.SqlLogFile, "Information", trigger);
            LogService.WriteLiveLog("SESSION: Starting Full SQL Backup...", BackupConfig.SqlLogFile, "Information", trigger);

            var sql = new SqlService();
            _activeSql = sql;
            await Task.Run(async () =>
            {
                try
                {
                    if (_abortRequested) throw new OperationCanceledException();
                    LogService.WriteLiveLog("INIT: Accessing staged sync folder...", BackupConfig.SqlLogFile, "Information", trigger);

                    string decryptedPass = SecurityService.GetDecryptedSqlPassword();
                    sql.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, decryptedPass, BackupConfig.SqlTlsFingerprint);

                    LogService.WriteLiveLog("CONNECTING TO HOST...", BackupConfig.SqlLogFile, "Information", trigger);
                    if (await sql.ConnectAsync())
                    {
                        if (_abortRequested) throw new OperationCanceledException();
                        // --- 7-DAY CLEANUP LOGIC ---
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            var tf = this.FindControl<TextBlock>("TxtFile");
                            if (tf != null) tf.Text = "Status: Performing maintenance...";
                        });

                        LogService.WriteLiveLog("CLEANUP: Checking for MySQL files older than 7 days...", BackupConfig.SqlLogFile, "Information", trigger);

                        var limitDate = BackupManager.GetTzDate().AddDays(-7);
                        var oldFiles = new DirectoryInfo(BackupConfig.SqlLocalFolder).GetFiles()
                            .Where(f => f.LastWriteTime < limitDate && f.Name != "backuplog.txt")
                            .ToList();

                        if (oldFiles.Count > 0)
                        {
                            LogService.WriteLiveLog($"CLEANUP: Found {oldFiles.Count} old database backups. Removing...", BackupConfig.SqlLogFile, "Warning", trigger);
                            foreach (var file in oldFiles)
                            {
                                if (_abortRequested) throw new OperationCanceledException();
                                try
                                {
                                    LogService.WriteLiveLog($"DELETING OLD DB: {file.Name} (Modified: {file.LastWriteTime})", BackupConfig.SqlLogFile, "Information", trigger);
                                    file.Delete();
                                }
                                catch { }
                            }
                            LogService.WriteLiveLog("CLEANUP: Finished maintenance.", BackupConfig.SqlLogFile, "Information", trigger);
                        }
                        else
                        {
                            LogService.WriteLiveLog("CLEANUP: No files older than 7 days found.", BackupConfig.SqlLogFile, "Information", trigger);
                        }

                        LogService.WriteLiveLog("COMPARING: Checking for new or updated files...", BackupConfig.SqlLogFile, "Information", trigger);

                        var remoteFiles = sql.ListFiles(BackupConfig.SqlRemotePath).Where(f => !f.IsDirectory).ToList();
                        int missingCount = 0;
                        foreach (var file in remoteFiles)
                        {
                            if (_abortRequested) throw new OperationCanceledException();
                            string localPath = Path.Combine(BackupConfig.SqlLocalFolder, file.Name);
                            if (!File.Exists(localPath)) missingCount++;
                            else if (new FileInfo(localPath).Length != file.Length) missingCount++;
                        }

                        if (missingCount == 0)
                        {
                            LogService.WriteLiveLog("NO NEW OR UPDATED FILES FOUND.", BackupConfig.SqlLogFile, "Information", trigger);
                            NotificationService.ShowBackupToast("SQL Backup Finished", $"No new or updated database files found ({trigger}).", "Info");
                        }
                        else
                        {
                            LogService.WriteLiveLog($"SYNCING: {missingCount} change(s) detected. Starting transfer...", BackupConfig.SqlLogFile, "Information", trigger);

                            await sql.SynchronizeLocalAsync(BackupConfig.SqlLocalFolder, BackupConfig.SqlRemotePath, (e) =>
                            {
                                if (_abortRequested) e.Cancel = true;
                                int pct = (int)(e.OverallProgress * 100);
                                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                                    var pb = this.FindControl<ProgressBar>("ProgressBar");
                                    if (pb != null) pb.Value = pct;
                                    var tf = this.FindControl<TextBlock>("TxtFile");
                                    if (tf != null) tf.Text = $"Downloading: {Path.GetFileName(e.FileName)} ({pct}%)";
                                });
                            });

                            if (_abortRequested) throw new OperationCanceledException();
                            LogService.WriteLiveLog("SUCCESS: SQL Backup complete.", BackupConfig.SqlLogFile, "Information", trigger);
                            NotificationService.ShowBackupToast("SQL Backup Success", $"Successfully synchronized {missingCount} database file(s).", "Info");
                        }

                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            txtStatus.Text = "COMPLETE";
                            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
                        });
                    }
                    else
                    {
                        taskError = "Authentication Error. Check SQL credentials.";
                        LogService.WriteLiveLog($"LOGIN FAILED: {taskError}", BackupConfig.SqlLogFile, "Error", trigger);
                        NotificationService.ShowBackupToast("SQL Backup Failed", $"Error: {taskError}", "Error");
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            txtStatus.Text = "LOGIN FAILED";
                            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    taskError = "Cancelled by user.";
                    LogService.WriteLiveLog("CANCELLED: SQL task cancelled by user.", BackupConfig.SqlLogFile, "Warning", trigger);
                    NotificationService.ShowBackupToast("SQL Backup Cancelled", "User cancelled the task.", "Warning");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "CANCELLED";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F9E2AF");
                    });
                }
                catch (Exception ex) when (_abortRequested && ex.Message.Contains("Aborted", StringComparison.OrdinalIgnoreCase))
                {
                    taskError = "Cancelled by user.";
                    LogService.WriteLiveLog("CANCELLED: SQL task cancelled by user.", BackupConfig.SqlLogFile, "Warning", trigger);
                    NotificationService.ShowBackupToast("SQL Backup Cancelled", "User cancelled the task.", "Warning");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "CANCELLED";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F9E2AF");
                    });
                }
                catch (Exception ex)
                {
                    taskError = ex.Message;
                    LogService.WriteLiveLog($"ERROR: SQL Sync failed - {taskError}", BackupConfig.SqlLogFile, "Error", trigger);
                    NotificationService.ShowBackupToast("SQL Backup Failed", $"Error: {taskError}", "Error");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "SYNC ERROR";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                    });
                }
                finally
                {
                    try { sql.Dispose(); } catch { }
                    _activeSql = null;

                    if (trigger == "MANUAL")
                    {
                        if (!string.IsNullOrEmpty(taskError))
                        {
                            if (string.Equals(taskError, "Cancelled by user.", StringComparison.OrdinalIgnoreCase))
                            {
                                await NotificationService.ShowMessageBoxAsync("SQL Backup was cancelled.", "Cancelled", ButtonEnum.Ok, Icon.Info);
                            }
                            else
                            {
                                await NotificationService.ShowMessageBoxAsync($"Full SQL Backup FAILED.\n\nError: {taskError}", "Task Error", ButtonEnum.Ok, Icon.Error);
                            }
                        }
                        else
                        {
                            await NotificationService.ShowMessageBoxAsync("Full SQL Backup Task Finished.\n\nAuto-Scan timer has been reset to 2h 15m.", "Task Complete", ButtonEnum.Ok, Icon.Info);
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

        private async Task SyncCheckAsync()
        {
            SetBusy(true);
            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            var txtFile = this.FindControl<TextBlock>("TxtFile")!;
            txtStatus.Text = "SYNC CHECK...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F9E2AF");
            txtFile.Text = "Status: Comparing local vs remote...";
            
            string statusText = "SYNC CHECK";
            string detailText = "Status: Idle";
            string colorHex = "#A6ADC8";
            string toastMessage = "Sync check finished.";
            string toastType = "Info";

            await Task.Run(async () =>
            {
                try
                {
                    using var sql = new SqlService();
                    string decryptedPass = SecurityService.GetDecryptedSqlPassword();
                    sql.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, decryptedPass, BackupConfig.SqlTlsFingerprint);

                    LogService.WriteLiveLog("SYNC CHECK: Comparing local vs remote latest .sql backup...", BackupConfig.SqlLogFile, "Information", "MANUAL");

                    if (!await sql.ConnectAsync())
                    {
                        statusText = "CONNECTION FAILED";
                        detailText = "Status: Unable to connect to SQL FTP.";
                        colorHex = "#F38BA8";
                        toastMessage = "Sync check failed: connection error.";
                        toastType = "Error";
                        return;
                    }

                    var remoteLatest = sql.ListFiles(BackupConfig.SqlRemotePath)
                        .Where(f => !f.IsDirectory)
                        .Where(f => f.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    FileInfo? localLatest = null;
                    if (Directory.Exists(BackupConfig.SqlLocalFolder))
                    {
                        localLatest = new DirectoryInfo(BackupConfig.SqlLocalFolder)
                            .EnumerateFiles("*.sql*", SearchOption.AllDirectories)
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault();
                    }

                    if (remoteLatest == null)
                    {
                        statusText = "REMOTE EMPTY";
                        detailText = "Status: No .sql backup found on server.";
                        colorHex = "#F9E2AF";
                        toastMessage = "Remote has no .sql backups.";
                        toastType = "Warning";
                        return;
                    }

                    if (localLatest == null)
                    {
                        statusText = "OUTDATED";
                        detailText = $"Remote latest: {remoteLatest.Name} ({remoteLatest.LastWriteTime:MM/dd HH:mm} UTC) | Local: none";
                        colorHex = "#F38BA8";
                        toastMessage = "Local folder has no .sql backups. Remote is newer.";
                        toastType = "Warning";
                        return;
                    }

                    string expectedLocalPath = Path.Combine(BackupConfig.SqlLocalFolder, remoteLatest.Name);
                    bool hasRemoteFileLocally = File.Exists(expectedLocalPath);
                    long remoteSize = remoteLatest.Length;
                    long localSize = hasRemoteFileLocally ? new FileInfo(expectedLocalPath).Length : -1;

                    bool sameName = string.Equals(localLatest.Name, remoteLatest.Name, StringComparison.OrdinalIgnoreCase);
                    bool sameSize = hasRemoteFileLocally && localSize == remoteSize;

                    // Use UTC with 1-minute buffer for comparison to avoid false "OUTDATED" reports
                    // Note: RemoteFileInfo.LastWriteTime is typically UTC in WinSCP
                    if (remoteLatest.LastWriteTime <= localLatest.LastWriteTimeUtc.AddMinutes(1))
                    {
                        statusText = "LATEST";
                        detailText = $"Local latest matches or newer: {localLatest.Name} ({localLatest.LastWriteTimeUtc:MM/dd HH:mm} UTC)";
                        colorHex = "#A6E3A1";
                        toastMessage = "Local SQL backup is up to date.";
                        toastType = "Info";
                        return;
                    }

                    if (hasRemoteFileLocally && localSize != remoteSize)
                    {
                        statusText = "SIZE MISMATCH";
                        detailText = $"Remote: {remoteLatest.Name} ({remoteSize:n0} bytes) | Local: {localSize:n0} bytes";
                        colorHex = "#F9E2AF";
                        toastMessage = "Remote file exists locally but size differs.";
                        toastType = "Warning";
                        return;
                    }

                    statusText = "OUTDATED";
                    detailText = $"Remote latest: {remoteLatest.Name} ({remoteLatest.LastWriteTime:MM/dd HH:mm} UTC) | Local latest: {localLatest.Name} ({localLatest.LastWriteTimeUtc:MM/dd HH:mm} UTC)";
                    colorHex = "#F38BA8";
                    toastMessage = "Remote SQL backup is newer than local.";
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
            LogService.WriteLiveLog($"SYNC CHECK RESULT: {statusText} | {detailText}", BackupConfig.SqlLogFile, logLevel, "MANUAL");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                txtStatus.Text = statusText;
                txtStatus.Foreground = Avalonia.Media.Brush.Parse(colorHex);
                txtFile.Text = detailText;
            });
            NotificationService.ShowBackupToast("SQL", toastMessage, toastType);

            if (statusText == "OUTDATED")
            {
                bool syncNow = await NotificationService.ConfirmAsync("Remote SQL backup is newer than local. Do you want to sync now?", "Sync Now?");
                if (syncNow)
                {
                    _ = StartBackupAsync("AUTO-SYNC");
                }
            }

            if (_manager != null)
            {
                _ = _manager.RunHealthCheckAsync();
            }

            SetBusy(false);
        }

        private async Task TestSqlAsync()
        {
            SetBusy(true);
            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            txtStatus.Text = "TESTING SQL...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F9E2AF");

            using var sql = new SqlService();
            string decryptedPass = SecurityService.GetDecryptedSqlPassword();
            sql.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, decryptedPass, BackupConfig.SqlTlsFingerprint);

            if (await sql.ConnectAsync())
            {
                LogService.WriteLiveLog("TEST SUCCESS: SQL Connection Verified.", BackupConfig.SqlLogFile, "Information", "MANUAL");
                txtStatus.Text = "TEST SUCCESS";
                txtStatus.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
            }
            else
            {
                LogService.WriteLiveLog("TEST FAILED: Authentication Error.", BackupConfig.SqlLogFile, "Error", "MANUAL");
                txtStatus.Text = "TEST FAILED";
                txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
            }
            SetBusy(false);
        }

        public bool IsBusy => _isBusy;

        public void RequestCancelFromShell()
        {
            if (!_isBusy) return;
            _abortRequested = true;
            _activeSql?.Abort();
            LogService.WriteLiveLog("CANCEL: Cancel requested.", BackupConfig.SqlLogFile, "Warning", "SYSTEM");
        }

        private async Task ConfirmCancelAsync()
        {
            if (!_isBusy) return;
            bool ok = await NotificationService.ConfirmAsync("Cancel the current SQL task?", "Confirm Cancel");
            if (!ok) return;

            RequestCancelFromShell();
            var txtStatus = this.FindControl<TextBlock>("TxtStatus");
            if (txtStatus != null)
            {
                txtStatus.Text = "CANCELLING...";
                txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F9E2AF");
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
            if (!busy) this.FindControl<TextBlock>("TxtFile")!.Text = "Status: Idle";
        }
    }
}
