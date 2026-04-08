using Avalonia.Controls;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;
using PinayPalBackupManager.Services;
using MsBox.Avalonia.Enums;
using WinSCP;

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
            
            this.FindControl<Button>("BtnStart")!.Click += async (s, e) => 
            {
                // Check if already up to date before starting backup (disable auto-sync)
                await SyncCheckAsync(allowAutoSync: false);
                var txtStatus = this.FindControl<TextBlock>("TxtStatus");
                if (txtStatus != null && txtStatus.Text == "LATEST")
                {
                    NotificationService.ShowBackupToast("SQL", "Backup is already up to date.", "Info");
                    return;
                }
                await StartBackupAsync();
            };
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

            // Reload config to ensure we have latest settings
            ConfigService.Load();
            
            string taskError = string.Empty;
            NotificationService.ShowBackupToast("SQL Backup Started", $"Trigger: {trigger}. Connecting to MySQL staged folder...", "Info");

            SetBusy(true);
            _abortRequested = false;

            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            txtStatus.Text = "SYNCING SQL...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#e6c55c");

            // Report global backup progress
            _manager?.ReportBackupProgress("SQL", 0, "SYNCING SQL...");

            // --- TIMER RESET & LOGGING ---
            _manager?.ResetSqlTimer();
            var next = BackupManager.GetTzDate().AddHours(2).AddMinutes(15);
            string resetMsg = $"TIMER: SQL activity detected ({trigger}). Auto-Scan reset to 2h 15m ({next:HH:mm:ss}).";
            LogService.WriteLiveLog(resetMsg, BackupConfig.SqlLogFile, "Information", trigger);
            LogService.WriteLiveLog("SESSION: Starting Full SQL Backup...", BackupConfig.SqlLogFile, "Information", trigger);
            LogService.WriteLiveLog($"SQL BACKUP: Using local path: {BackupConfig.SqlLocalFolder}", BackupConfig.SqlLogFile, "Information", trigger);
            LogService.WriteLiveLog($"SQL BACKUP: Using remote path: {BackupConfig.SqlRemotePath}", BackupConfig.SqlLogFile, "Information", trigger);

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
                    bool connected = await sql.ConnectAsync();
                    if (_abortRequested) throw new OperationCanceledException();
                    if (connected)
                    {
                        if (_abortRequested) throw new OperationCanceledException();
                        // --- CLEANUP LOGIC ---
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            var tf = this.FindControl<TextBlock>("TxtFile");
                            if (tf != null) tf.Text = "Status: Performing maintenance...";
                        });

                        var retentionDays = ConfigService.Current.Operation.RetentionDays;
                        LogService.WriteLiveLog($"CLEANUP: Checking for MySQL files older than {retentionDays} days...", BackupConfig.SqlLogFile, "Information", trigger);

                        var limitDate = BackupManager.GetTzDate().AddDays(-retentionDays);
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
                            LogService.WriteLiveLog($"CLEANUP: No files older than {retentionDays} days found.", BackupConfig.SqlLogFile, "Information", trigger);
                        }

                        LogService.WriteLiveLog("COMPARING: Checking for new or updated files...", BackupConfig.SqlLogFile, "Information", trigger);

                        var remoteFiles = sql.ListFiles(BackupConfig.SqlRemotePath).Where(f => !f.IsDirectory).ToList();
                        LogService.WriteLiveLog($"REMOTE FILES: Found {remoteFiles.Count} file(s) in {BackupConfig.SqlRemotePath}", BackupConfig.SqlLogFile, "Information", trigger);
                        
                        int missingCount = 0;
                        foreach (var file in remoteFiles)
                        {
                            if (_abortRequested) throw new OperationCanceledException();
                            string localPath = Path.Combine(BackupConfig.SqlLocalFolder, file.Name);
                            LogService.WriteLiveLog($"CHECKING: {file.Name} (Size: {file.Length}, Modified: {file.LastWriteTime:yyyy-MM-dd HH:mm:ss} UTC)", BackupConfig.SqlLogFile, "Information", trigger);
                            
                            if (!File.Exists(localPath))
                            {
                                LogService.WriteLiveLog($"MISSING: {file.Name} does not exist locally", BackupConfig.SqlLogFile, "Information", trigger);
                                missingCount++;
                            }
                            else
                            {
                                var localFile = new FileInfo(localPath);
                                // Check file size OR if remote is significantly newer (more than 24 hours for timezone tolerance)
                                bool sizeDiffers = localFile.Length != file.Length;
                                bool timeDiffers = file.LastWriteTime > localFile.LastWriteTimeUtc.AddMinutes(1440);
                                
                                if (sizeDiffers || timeDiffers)
                                {
                                    missingCount++;
                                    string reason = sizeDiffers ? $"size differs (local: {localFile.Length}, remote: {file.Length})" : 
                                                   timeDiffers ? $"remote newer (local: {localFile.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC, remote: {file.LastWriteTime:yyyy-MM-dd HH:mm:ss} UTC)" : "unknown";
                                    LogService.WriteLiveLog($"CHANGE DETECTED: {file.Name} - {reason}", BackupConfig.SqlLogFile, "Information", trigger);
                                }
                                else
                                {
                                    LogService.WriteLiveLog($"UP TO DATE: {file.Name}", BackupConfig.SqlLogFile, "Information", trigger);
                                }
                            }
                        }

                        if (missingCount == 0)
                        {
                            LogService.WriteLiveLog("NO NEW OR UPDATED FILES FOUND.", BackupConfig.SqlLogFile, "Information", trigger);
                            NotificationService.ShowBackupToast("SQL Backup Finished", $"No new or updated database files found ({trigger}).", "Info");
                        }
                        else
                        {
                            LogService.WriteLiveLog($"SYNCING: {missingCount} change(s) detected. Starting transfer...", BackupConfig.SqlLogFile, "Information", trigger);
                            LogService.WriteLiveLog($"SYNC PATH: Local={BackupConfig.SqlLocalFolder}, Remote={BackupConfig.SqlRemotePath}", BackupConfig.SqlLogFile, "Information", trigger);

                            // Force download all remote files that are missing or different
                            int downloaded = 0;
                            int totalToDownload = remoteFiles.Count(f => 
                            {
                                string localPath = Path.Combine(BackupConfig.SqlLocalFolder, f.Name);
                                if (!File.Exists(localPath)) return true;
                                var localFile = new FileInfo(localPath);
                                return localFile.Length != f.Length || f.LastWriteTime > localFile.LastWriteTimeUtc.AddMinutes(1440); // 24 hours for timezone tolerance
                            });
                            
                            int currentFile = 0;
                            foreach (var file in remoteFiles)
                            {
                                if (_abortRequested) throw new OperationCanceledException();
                                
                                string localPath = Path.Combine(BackupConfig.SqlLocalFolder, file.Name);
                                bool needsDownload = !File.Exists(localPath);
                                
                                if (!needsDownload)
                                {
                                    var localFile = new FileInfo(localPath);
                                    bool sizeDiffers = localFile.Length != file.Length;
                                    bool timeDiffers = file.LastWriteTime > localFile.LastWriteTimeUtc.AddMinutes(1440); // 24 hours for timezone tolerance
                                    needsDownload = sizeDiffers || timeDiffers;
                                }
                                
                                // Update progress bar
                                if (totalToDownload > 0)
                                {
                                    int progress = (int)((double)currentFile / totalToDownload * 100);
                                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                                        var pb = this.FindControl<ProgressBar>("ProgressBar");
                                        if (pb != null) pb.Value = progress;
                                        var tf = this.FindControl<TextBlock>("TxtFile");
                                        if (tf != null) tf.Text = $"Processing: {file.Name} ({currentFile}/{totalToDownload})";
                                    });
                                    // Report global backup progress
                                    _manager?.ReportBackupProgress("SQL", progress, $"Downloading {file.Name} ({currentFile}/{totalToDownload})");
                                }
                                
                                if (needsDownload)
                                {
                                    try
                                    {
                                        LogService.WriteLiveLog($"DOWNLOADING: {file.Name} -> {BackupConfig.SqlLocalFolder}", BackupConfig.SqlLogFile, "Information", trigger);
                                        
                                        sql.GetSession()?.GetFileToDirectory(file.FullName, BackupConfig.SqlLocalFolder);
                                        
                                        downloaded++;
                                        LogService.WriteLiveLog($"SUCCESS: Downloaded {file.Name}", BackupConfig.SqlLogFile, "Information", trigger);
                                    }
                                    catch (Exception fileEx)
                                    {
                                        LogService.WriteLiveLog($"FAILED: {file.Name} - {fileEx.Message}", BackupConfig.SqlLogFile, "Error", trigger);
                                    }
                                }
                                
                                currentFile++;
                            }
                            
                            missingCount = downloaded;
                            LogService.WriteLiveLog($"DOWNLOAD COMPLETE: Downloaded {downloaded} file(s)", BackupConfig.SqlLogFile, "Information", trigger);

                            if (_abortRequested) throw new OperationCanceledException();
                            LogService.WriteLiveLog("SUCCESS: SQL Backup complete.", BackupConfig.SqlLogFile, "Information", trigger);
                            var integrity = CheckIntegrity(BackupConfig.SqlLocalFolder);
                            LogService.WriteLiveLog($"INTEGRITY: {integrity}", BackupConfig.SqlLogFile, "Information", trigger);
                            NotificationService.ShowBackupToast("SQL Backup Success", $"Synchronized {missingCount} file(s). {integrity}", "Success");
                        }

                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            txtStatus.Text = "COMPLETE";
                            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#588157");
                        });
                        // Report global backup progress complete
                        _manager?.ReportBackupProgress("SQL", 100, "COMPLETE");
                    }
                    else
                    {
                        taskError = "Authentication Error. Check SQL credentials.";
                        LogService.WriteLiveLog($"STOPPED: Error detected - {taskError}", BackupConfig.SqlLogFile, "Warning", trigger);
                        NotificationService.ShowBackupToast("SQL Backup Failed", $"Error: {taskError}", "Error");
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            txtStatus.Text = "LOGIN FAILED";
                            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                        });
                        // Report global backup progress failed
                        _manager?.ReportBackupProgress("SQL", 0, "LOGIN FAILED");
                    }
                }
                catch (OperationCanceledException)
                {
                    taskError = "Cancelled by user.";
                    LogService.WriteLiveLog("CANCELLED: SQL task cancelled by user.", BackupConfig.SqlLogFile, "Warning", trigger);
                    NotificationService.ShowBackupToast("SQL Backup Cancelled", "User cancelled the task.", "Warning");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "CANCELLED";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#e6c55c");
                    });
                    // Report global backup progress cancelled
                    _manager?.ReportBackupProgress("SQL", 0, "CANCELLED");
                }
                catch (Exception ex) when (_abortRequested && ex.Message.Contains("Aborted", StringComparison.OrdinalIgnoreCase))
                {
                    taskError = "Cancelled by user.";
                    LogService.WriteLiveLog("CANCELLED: SQL task cancelled by user.", BackupConfig.SqlLogFile, "Warning", trigger);
                    NotificationService.ShowBackupToast("SQL Backup Cancelled", "User cancelled the task.", "Warning");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        txtStatus.Text = "CANCELLED";
                        txtStatus.Foreground = Avalonia.Media.Brush.Parse("#e6c55c");
                    });
                    // Report global backup progress cancelled
                    _manager?.ReportBackupProgress("SQL", 0, "CANCELLED");
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
                    // Report global backup progress error
                    _manager?.ReportBackupProgress("SQL", 0, "SYNC ERROR");
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

        private async Task SyncCheckAsync(bool allowAutoSync = true)
        {
            SetBusy(true);
            var txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            var txtFile = this.FindControl<TextBlock>("TxtFile")!;
            
            // Set initial status to prevent showing old status during comparison
            txtStatus.Text = "SYNC CHECK...";
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#e6c55c");
            txtFile.Text = "Status: Comparing local vs remote...";
            
            string statusText = "SYNC CHECK";
            string detailText = "Status: Idle";
            string colorHex = "#6C7086";
            string toastMessage = "Sync check finished.";
            string toastType = "Info";

            await Task.Run(async () =>
            {
                try
                {
                    // Reload config to ensure we have latest settings
                    ConfigService.Load();
                    
                    using var sql = new SqlService();
                    string decryptedPass = SecurityService.GetDecryptedSqlPassword();
                    sql.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, decryptedPass, BackupConfig.SqlTlsFingerprint);

                    LogService.WriteLiveLog("SYNC CHECK: Comparing local vs remote latest .sql backup...", BackupConfig.SqlLogFile, "Information", "MANUAL");
                    LogService.WriteLiveLog($"SYNC CHECK: Using remote path: {BackupConfig.SqlRemotePath}", BackupConfig.SqlLogFile, "Information", "MANUAL");

                    if (!await sql.ConnectAsync())
                    {
                        statusText = "CONNECTION FAILED";
                        detailText = "Status: Unable to connect to SQL FTP.";
                        colorHex = "#F38BA8";
                        toastMessage = "Sync check failed: connection error.";
                        toastType = "Error";
                        return;
                    }

                    // --- CLEANUP LOGIC ---
                    var retentionDays = ConfigService.Current.Operation.RetentionDays;
                    LogService.WriteLiveLog($"CLEANUP: Checking for MySQL files older than {retentionDays} days...", BackupConfig.SqlLogFile, "Information", "MANUAL");

                    var limitDate = BackupManager.GetTzDate().AddDays(-retentionDays);
                    var oldFiles = new DirectoryInfo(BackupConfig.SqlLocalFolder).GetFiles()
                        .Where(f => f.LastWriteTime < limitDate && f.Name != "backuplog.txt")
                        .ToList();

                    if (oldFiles.Count > 0)
                    {
                        LogService.WriteLiveLog($"CLEANUP: Found {oldFiles.Count} old database backups. Removing...", BackupConfig.SqlLogFile, "Warning", "MANUAL");
                        foreach (var file in oldFiles)
                        {
                            try
                            {
                                LogService.WriteLiveLog($"DELETING OLD DB: {file.Name} (Modified: {file.LastWriteTime})", BackupConfig.SqlLogFile, "Information", "MANUAL");
                                file.Delete();
                            }
                            catch { }
                        }
                        LogService.WriteLiveLog("CLEANUP: Finished maintenance.", BackupConfig.SqlLogFile, "Information", "MANUAL");
                    }
                    else
                    {
                        LogService.WriteLiveLog($"CLEANUP: No files older than {retentionDays} days found.", BackupConfig.SqlLogFile, "Information", "MANUAL");
                    }

                    var remoteFiles = sql.ListFiles(BackupConfig.SqlRemotePath);
                    var remoteLatest = remoteFiles
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
                        detailText = "Status: No .sql backups found on remote server.";
                        colorHex = "#dad7cd";
                        toastMessage = "Remote has no .sql backups.";
                        toastType = "Warning";
                        return;
                    }

                    if (localLatest == null)
                    {
                        statusText = "OUTDATED";
                        detailText = $"Remote latest: {remoteLatest.Name} ({remoteLatest.LastWriteTime:MM/dd HH:mm} UTC, {remoteLatest.Length:n0} bytes) | Local: none";
                        colorHex = "#F38BA8";
                        toastMessage = "Local folder has no backups. Remote is newer.";
                        toastType = "Warning";
                        return;
                    }

                    string expectedLocalPath = Path.Combine(BackupConfig.SqlLocalFolder, remoteLatest.Name);
                    bool hasRemoteFileLocally = File.Exists(expectedLocalPath);
                    long remoteSize = remoteLatest.Length;
                    long localSize = hasRemoteFileLocally ? new FileInfo(expectedLocalPath).Length : -1;

                    bool sameName = string.Equals(localLatest.Name, remoteLatest.Name, StringComparison.OrdinalIgnoreCase);
                    bool sameSize = hasRemoteFileLocally && localSize == remoteSize;

                    LogService.WriteLiveLog($"SYNC CHECK: Remote file: {remoteLatest.Name}, Size: {remoteSize:n0} bytes", BackupConfig.SqlLogFile, "Information", "MANUAL");
                    LogService.WriteLiveLog($"SYNC CHECK: Local file: {localLatest.Name}, Size: {localSize:n0} bytes", BackupConfig.SqlLogFile, "Information", "MANUAL");
                    LogService.WriteLiveLog($"SYNC CHECK: Same name: {sameName}, Has remote file locally: {hasRemoteFileLocally}, Same size: {sameSize}", BackupConfig.SqlLogFile, "Information", "MANUAL");

                    // Primary check: if we have the exact same file with same size, consider it up to date
                    if (hasRemoteFileLocally && sameName && sameSize)
                    {
                        statusText = "LATEST";
                        detailText = $"Local matches remote: {remoteLatest.Name} ({localSize:n0} bytes)";
                        colorHex = "#588157";
                        toastMessage = "Local SQL backup is up to date.";
                        toastType = "Info";
                        return;
                    }

                    // Check for size mismatch - if file exists but size differs, delete and resync
                    if (hasRemoteFileLocally && !sameSize)
                    {
                        // Auto-delete mismatched file and trigger resync
                        try
                        {
                            File.Delete(expectedLocalPath);
                            LogService.WriteLiveLog($"SYNC CHECK: Deleted mismatched SQL file {expectedLocalPath} (size: {localSize:n0} bytes, expected: {remoteSize:n0} bytes)", BackupConfig.SqlLogFile, "Warning", "MANUAL");
                        }
                        catch (Exception ex)
                        {
                            LogService.WriteLiveLog($"SYNC CHECK: Failed to delete mismatched SQL file: {ex.Message}", BackupConfig.SqlLogFile, "Error", "MANUAL");
                        }

                        statusText = "RESYNCING";
                        detailText = $"Deleted mismatched file. Remote: {remoteLatest.Name} ({remoteSize:n0} bytes)";
                        colorHex = "#e6c55c";
                        toastMessage = "Size mismatch detected. Deleted local file and starting resync...";
                        toastType = "Warning";

                        // Trigger auto-resync after a short delay
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(1000);
                            await StartBackupAsync("AUTO-RESYNC");
                        });
                        return;
                    }

                    // Secondary check: if remote file exists locally with same size (even if not the localLatest), consider it up to date
                    if (hasRemoteFileLocally && sameSize)
                    {
                        statusText = "LATEST";
                        detailText = $"Local has remote file: {remoteLatest.Name} ({localSize:n0} bytes)";
                        colorHex = "#588157";
                        toastMessage = "Local SQL backup is up to date.";
                        toastType = "Info";
                        return;
                    }

                    // Tertiary check: if file names match and local is recent (within 24 hours for timezone tolerance), consider it up to date
                    if (sameName)
                    {
                        var timeDiff = remoteLatest.LastWriteTime - localLatest.LastWriteTimeUtc;
                        LogService.WriteLiveLog($"SYNC CHECK: Time difference = {timeDiff.TotalMinutes:F1} minutes (Remote: {remoteLatest.LastWriteTime:yyyy-MM-dd HH:mm:ss} UTC, Local: {localLatest.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC)", BackupConfig.SqlLogFile, "Information", "MANUAL");
                        
                        if (timeDiff.TotalMinutes <= 1440) // 24 hours to account for timezone differences
                        {
                            statusText = "LATEST";
                            detailText = $"Local matches remote: {localLatest.Name} (recent sync)";
                            colorHex = "#588157";
                            toastMessage = "Local SQL backup is up to date.";
                            toastType = "Info";
                            return;
                        }
                    }

                    // If we get here, remote is genuinely newer or different
                    statusText = "OUTDATED";
                    detailText = $"Remote latest: {remoteLatest.Name} ({remoteLatest.LastWriteTime:MM/dd HH:mm} UTC, {remoteSize:n0} bytes) | Local latest: {localLatest.Name} ({localLatest.LastWriteTimeUtc:MM/dd HH:mm} UTC, {localSize:n0} bytes)";
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

            // Only update UI after comparison is complete
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                txtStatus.Text = statusText;
                txtStatus.Foreground = Avalonia.Media.Brush.Parse(colorHex);
                txtFile.Text = detailText;
            });

            string logLevel = toastType.Equals("Error", StringComparison.OrdinalIgnoreCase)
                ? "Error"
                : toastType.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                    ? "Warning"
                    : "Information";
            LogService.WriteLiveLog($"SYNC CHECK RESULT: {statusText} | {detailText}", BackupConfig.SqlLogFile, logLevel, "MANUAL");

            NotificationService.ShowBackupToast("SQL", toastMessage, toastType);

            if (allowAutoSync && statusText == "OUTDATED")
            {
                LogService.WriteLiveLog("AUTO-SYNC: Remote is newer, prompting user to sync...", BackupConfig.SqlLogFile, "Information", "MANUAL");
                SetBusy(false);
                bool confirm = await NotificationService.ConfirmAsync(
                    "Remote SQL backup is newer than local. Do you want to sync now?",
                    "Sync Now?"
                );
                if (confirm)
                {
                    _ = StartBackupAsync("AUTO-SYNC");
                }
                return;
            }

            // Run health check after sync check completes
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
            txtStatus.Foreground = Avalonia.Media.Brush.Parse("#e6c55c");

            using var sql = new SqlService();
            string decryptedPass = SecurityService.GetDecryptedSqlPassword();
            sql.Initialize(BackupConfig.FtpHost, BackupConfig.SqlUser, decryptedPass, BackupConfig.SqlTlsFingerprint);

            if (await sql.ConnectAsync())
            {
                LogService.WriteLiveLog("TEST SUCCESS: SQL Connection Verified.", BackupConfig.SqlLogFile, "Information", "MANUAL");
                txtStatus.Text = "TEST SUCCESS";
                txtStatus.Foreground = Avalonia.Media.Brush.Parse("#e6c55c");
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
        public Task TriggerSyncCheckAsync() => SyncCheckAsync(allowAutoSync: true);

        private static string CheckIntegrity(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return "No local folder found.";
                var newest = new DirectoryInfo(folder)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f => f.Name != "backuplog.txt" && f.Name != "backup_log.txt")
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
                txtStatus.Foreground = Avalonia.Media.Brush.Parse("#e6c55c");
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
