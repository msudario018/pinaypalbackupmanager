using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public static class NetworkDriveService
    {
        public static event Action<string, int, string, int, int>? OnMirrorProgress; // service, percent, msg, currentFile, totalFiles

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword,
            int dwLogonType, int dwLogonProvider, out IntPtr phToken);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int LOGON32_LOGON_NEW_CREDENTIALS = 9;
        private const int LOGON32_PROVIDER_DEFAULT = 0;

        public static async Task<bool> CopyToNetworkDriveAsync(string sourceFile, string destinationPath, 
            string username, string password, Action<string>? progressCallback = null)
        {
            try
            {
                progressCallback?.Invoke("Connecting to network drive...");

                // Ensure destination directory exists
                var destDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                progressCallback?.Invoke("Copying file to network drive...");

                // Copy file
                await Task.Run(() =>
                {
                    File.Copy(sourceFile, destinationPath, true);
                });

                progressCallback?.Invoke("Network drive backup completed successfully");
                LogService.WriteSystemLog($"[NETWORKDRIVE] Successfully copied {Path.GetFileName(sourceFile)} to {destinationPath}", "Information", "NETWORKDRIVE");
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                progressCallback?.Invoke("Access denied - check credentials");
                LogService.WriteSystemLog($"[NETWORKDRIVE] Access denied: {ex.Message}", "Error", "NETWORKDRIVE");
                return false;
            }
            catch (DirectoryNotFoundException ex)
            {
                progressCallback?.Invoke("Network path not found");
                LogService.WriteSystemLog($"[NETWORKDRIVE] Path not found: {ex.Message}", "Error", "NETWORKDRIVE");
                return false;
            }
            catch (IOException ex)
            {
                progressCallback?.Invoke($"Network error: {ex.Message}");
                LogService.WriteSystemLog($"[NETWORKDRIVE] IO error: {ex.Message}", "Error", "NETWORKDRIVE");
                return false;
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"Error: {ex.Message}");
                LogService.WriteSystemLog($"[NETWORKDRIVE] Unexpected error: {ex.Message}", "Error", "NETWORKDRIVE");
                return false;
            }
        }

        public static async Task<bool> CopyDirectoryToNetworkDriveAsync(string sourceDir, string destinationDir,
            string username, string password, Action<string, int, int, int>? progressCallback = null)
        {
            try
            {
                // Handle FTP / FTPS / SFTP URLs using WinSCP FtpService
                if (destinationDir.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                    destinationDir.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) ||
                    destinationDir.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase))
                {
                    return await MirrorViaFtpAsync(sourceDir, destinationDir, username, password, progressCallback);
                }

                if (!Directory.Exists(sourceDir))
                {
                    LogService.WriteSystemLog($"[NETWORKDRIVE] Source directory not found: {sourceDir}", "Error", "NETWORKDRIVE");
                    return false;
                }

                progressCallback?.Invoke("Connecting to network drive...", 0, 0, 0);

                // Ensure destination directory exists
                if (!Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                // Enumerate files lazily to avoid loading all paths into memory for large backup sets
                var files = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories).ToList();
                var totalFiles = files.Count;

                if (totalFiles == 0)
                {
                    LogService.WriteSystemLog($"[NETWORKDRIVE] No files to mirror in {sourceDir}", "Warning", "NETWORKDRIVE");
                    progressCallback?.Invoke("No files to mirror", 0, 0, 0);
                    throw new InvalidOperationException($"No files found in source folder: '{sourceDir}'. Run backup first.");
                }

                var processedFiles = 0;

                await Task.Run(() =>
                {
                    // Use impersonation for UNC paths when credentials are provided
                    bool needsImpersonation = !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password)
                        && (destinationDir.StartsWith(@"\\") || destinationDir.StartsWith("//"));

                    if (needsImpersonation)
                    {
                        // Parse domain\user or user@domain
                        string domain = ".";
                        string user = username;
                        int backslash = username.IndexOf('\\');
                        if (backslash >= 0)
                        {
                            domain = username.Substring(0, backslash);
                            user = username.Substring(backslash + 1);
                        }

                        if (!LogonUser(user, domain, password, LOGON32_LOGON_NEW_CREDENTIALS, LOGON32_PROVIDER_DEFAULT, out IntPtr token))
                        {
                            throw new UnauthorizedAccessException("Failed to authenticate with provided network credentials.");
                        }

                        using var safeToken = new SafeAccessTokenHandle(token);
                        WindowsIdentity.RunImpersonated(safeToken, () =>
                        {
                            CopyFilesWithProgress(sourceDir, destinationDir, files, ref processedFiles, totalFiles, progressCallback);
                        });
                    }
                    else
                    {
                        CopyFilesWithProgress(sourceDir, destinationDir, files, ref processedFiles, totalFiles, progressCallback);
                    }
                });

                progressCallback?.Invoke("Network drive backup completed successfully", 100, totalFiles, totalFiles);
                LogService.WriteSystemLog($"[NETWORKDRIVE] Successfully copied {totalFiles} files to {destinationDir}", "Information", "NETWORKDRIVE");
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                progressCallback?.Invoke("Access denied - check credentials", 0, 0, 0);
                LogService.WriteSystemLog($"[NETWORKDRIVE] Access denied: {ex.Message}", "Error", "NETWORKDRIVE");
                throw;
            }
            catch (DirectoryNotFoundException ex)
            {
                progressCallback?.Invoke("Network path not found", 0, 0, 0);
                LogService.WriteSystemLog($"[NETWORKDRIVE] Path not found: {ex.Message}", "Error", "NETWORKDRIVE");
                throw;
            }
            catch (IOException ex)
            {
                progressCallback?.Invoke($"Network error: {ex.Message}", 0, 0, 0);
                LogService.WriteSystemLog($"[NETWORKDRIVE] IO error: {ex.Message}", "Error", "NETWORKDRIVE");
                throw;
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"Error: {ex.Message}", 0, 0, 0);
                LogService.WriteSystemLog($"[NETWORKDRIVE] Unexpected error: {ex.Message}", "Error", "NETWORKDRIVE");
                throw;
            }
        }

        private static void CopyFilesWithProgress(string sourceDir, string destinationDir, IEnumerable<string> files, ref int processedFiles, int totalFiles, Action<string, int, int, int>? progressCallback)
        {
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("backup_log.txt", StringComparison.OrdinalIgnoreCase))
                {
                    processedFiles++;
                    progressCallback?.Invoke($"Skipping log file {fileName}...", (int)((processedFiles / (double)totalFiles) * 100), processedFiles, totalFiles);
                    continue;
                }

                var relativePath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
                var destFile = Path.Combine(destinationDir, relativePath);

                var destFileDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destFileDir) && !Directory.Exists(destFileDir))
                {
                    Directory.CreateDirectory(destFileDir);
                }

                File.Copy(file, destFile, true);
                processedFiles++;

                var progress = (int)((processedFiles / (double)totalFiles) * 100);
                progressCallback?.Invoke($"Copying {Path.GetFileName(file)}...", progress, processedFiles, totalFiles);
            }
        }

        public static bool TestNetworkConnection(string networkPath, string username, string password)
        {
            try
            {
                if (!Directory.Exists(networkPath))
                {
                    LogService.WriteSystemLog($"[NETWORKDRIVE] Network path not accessible: {networkPath}", "Warning", "NETWORKDRIVE");
                    return false;
                }

                // Try to list directory contents to verify access
                Directory.GetFiles(networkPath);
                LogService.WriteSystemLog($"[NETWORKDRIVE] Network connection successful: {networkPath}", "Information", "NETWORKDRIVE");
                return true;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[NETWORKDRIVE] Network connection test failed: {ex.Message}", "Error", "NETWORKDRIVE");
                return false;
            }
        }

        private static async Task<bool> MirrorViaFtpAsync(string sourceDir, string destinationDir, string username, string password, Action<string, int, int, int>? progressCallback)
        {
            try
            {
                var uri = new Uri(destinationDir);
                string host = uri.Host;
                int port = uri.Port > 0 ? uri.Port : (uri.Scheme.Equals("ftps", StringComparison.OrdinalIgnoreCase) ? 990 : 21);
                string remotePath = uri.AbsolutePath;

                int totalFiles = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories).Count();
                int currentFile = 0;

                LogService.WriteSystemLog($"[NETWORKDRIVE] FTP mirror to {host}:{port}{remotePath}", "Information", "NETWORKDRIVE");
                progressCallback?.Invoke($"Connecting to {host}...", 0, 0, totalFiles);

                using var ftp = new FtpService();
                ftp.Initialize(host, username, password, "", port);

                bool connected = await ftp.ConnectAsync();
                if (!connected)
                {
                    LogService.WriteSystemLog("[NETWORKDRIVE] FTP connection failed", "Error", "NETWORKDRIVE");
                    progressCallback?.Invoke("FTP connection failed", -1, 0, totalFiles);
                    throw new InvalidOperationException("FTP connection failed — check host, credentials, and port.");
                }

                progressCallback?.Invoke("Uploading files...", 5, 0, totalFiles);
                bool success = await ftp.SynchronizeRemoteAsync(sourceDir, remotePath, (e) =>
                {
                    int pct = (int)(e.OverallProgress * 100);
                    string speed = e.CPS > 1048576 ? $"{Math.Round(e.CPS / 1048576.0, 2)} MB/s" : $"{Math.Round(e.CPS / 1024.0, 2)} KB/s";
                    string fileName = !string.IsNullOrEmpty(e.FileName) ? Path.GetFileName(e.FileName) : "file";
                    // Estimate current file from overall progress
                    currentFile = Math.Max(currentFile, (int)(e.OverallProgress * totalFiles));
                    progressCallback?.Invoke($"Uploading {fileName} ({speed})", pct, currentFile, totalFiles);
                });

                if (!success)
                {
                    progressCallback?.Invoke("Upload failed", -1, currentFile, totalFiles);
                    LogService.WriteSystemLog("[NETWORKDRIVE] FTP mirror failed", "Error", "NETWORKDRIVE");
                    throw new InvalidOperationException("FTP upload failed — check remote path and permissions.");
                }

                progressCallback?.Invoke("Upload complete", 100, totalFiles, totalFiles);
                LogService.WriteSystemLog($"[NETWORKDRIVE] FTP mirror complete: {remotePath}", "Information", "NETWORKDRIVE");
                return true;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[NETWORKDRIVE] FTP mirror error: {ex.Message}", "Error", "NETWORKDRIVE");
                progressCallback?.Invoke($"Error: {ex.Message}", -1, 0, 0);
                throw;
            }
        }

        public static bool IsNetworkDriveConfigured()
        {
            return BackupConfig.NetworkDriveEnabled 
                && !string.IsNullOrWhiteSpace(BackupConfig.NetworkDrivePath);
        }

        public static async Task MirrorToNetworkDriveAsync(string localFolder, string serviceName)
        {
            LogService.WriteSystemLog($"[NETWORKDRIVE] MirrorToNetworkDriveAsync called: service={serviceName}, localFolder='{localFolder}'", "Information", "NETWORKDRIVE");

            if (!IsNetworkDriveConfigured())
            {
                LogService.WriteSystemLog($"[NETWORKDRIVE] Aborted {serviceName}: network drive not configured (Enabled={BackupConfig.NetworkDriveEnabled}, Path='{BackupConfig.NetworkDrivePath}')", "Warning", "NETWORKDRIVE");
                return;
            }
            if (!Directory.Exists(localFolder))
            {
                LogService.WriteSystemLog($"[NETWORKDRIVE] Aborted {serviceName}: local folder does not exist '{localFolder}'", "Warning", "NETWORKDRIVE");
                throw new DirectoryNotFoundException($"Local backup folder not found: '{localFolder}'. Run backup first.");
            }

            var basePath = BackupConfig.NetworkDrivePath;
            var destDir = basePath.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                          basePath.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) ||
                          basePath.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase)
                ? (basePath.TrimEnd('/') + "/" + serviceName)
                : Path.Combine(basePath, serviceName);

            LogService.WriteSystemLog($"[NETWORKDRIVE] Mirroring {serviceName} backup from '{localFolder}' to '{destDir}' (basePath='{basePath}')", "Information", "NETWORKDRIVE");
            OnMirrorProgress?.Invoke(serviceName, 0, $"Starting {serviceName} mirror...", 0, 0);

            try
            {
                await CopyDirectoryToNetworkDriveAsync(
                    localFolder,
                    destDir,
                    BackupConfig.NetworkDriveUsername,
                    BackupConfig.NetworkDrivePassword,
                    (msg, pct, currentFile, totalFiles) =>
                    {
                        LogService.WriteSystemLog($"[NETWORKDRIVE] {serviceName}: {msg} ({pct}%)", "Information", "NETWORKDRIVE");
                        OnMirrorProgress?.Invoke(serviceName, pct, msg, currentFile, totalFiles);
                    }
                );

                OnMirrorProgress?.Invoke(serviceName, 100, $"{serviceName} mirror complete", 0, 0);
                NotificationService.ShowBackupToast("Network Drive", $"{serviceName} mirrored to network drive.", "Success");
            }
            catch (Exception ex)
            {
                string details = ex.Message;
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.Message}";
                LogService.WriteSystemLog($"[NETWORKDRIVE] Mirror error for {serviceName}: {details} | Stack: {ex.StackTrace?.Substring(0, Math.Min(500, ex.StackTrace?.Length ?? 0))}", "Error", "NETWORKDRIVE");
                throw;
            }
        }
    }
}
