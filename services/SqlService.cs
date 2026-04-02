using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinSCP;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public class SqlService : IDisposable
    {
        private readonly object _progressLock = new();
        private Session? _session;
        private SessionOptions? _options;
        private Action<FileTransferProgressEventArgs>? _progressCallback;

        public void Initialize(string host, string user, string password, string fingerprint)
        {
            LogService.WriteLiveLog($"SQL INIT: Host={host}, User={user}, Port=21", BackupConfig.SqlLogFile, "Information", "SYSTEM");
            
            if (string.IsNullOrEmpty(password))
            {
                LogService.WriteLiveLog("SQL INIT WARNING: Password is empty after decryption!", BackupConfig.SqlLogFile, "Warning", "SYSTEM");
            }
            else
            {
                LogService.WriteLiveLog($"SQL INIT: Password recovered ({password.Length} characters).", BackupConfig.SqlLogFile, "Information", "SYSTEM");
            }

            _options = new SessionOptions
            {
                Protocol = Protocol.Ftp,
                HostName = host,
                UserName = user,
                Password = password,
                PortNumber = 21,
                FtpSecure = FtpSecure.Explicit,
                TlsHostCertificateFingerprint = fingerprint
            };
        }

        public async Task<bool> ConnectAsync()
        {
            if (_session != null && _session.Opened) 
            {
                LogService.WriteLiveLog("SQL CONNECT: Session already open.", BackupConfig.SqlLogFile, "Information", "SYSTEM");
                return true;
            }
            if (_options == null) 
            {
                LogService.WriteLiveLog("SQL CONNECT ERROR: Options not initialized.", BackupConfig.SqlLogFile, "Error", "SYSTEM");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    LogService.WriteLiveLog("SQL CONNECT: Opening session...", BackupConfig.SqlLogFile, "Information", "SYSTEM");
                    _session = new Session();
                    _session.FileTransferProgress += Session_FileTransferProgress;
                    _session.Open(_options);
                    LogService.WriteLiveLog("SQL CONNECT: Session opened successfully.", BackupConfig.SqlLogFile, "Information", "SYSTEM");
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"SQL CONNECTION FAILED: {ex.Message}", BackupConfig.SqlLogFile, "Error", "SYSTEM");
                    if (ex.InnerException != null)
                        LogService.WriteLiveLog($"SQL INNER ERROR: {ex.InnerException.Message}", BackupConfig.SqlLogFile, "Error", "SYSTEM");
                    return false;
                }
            });
        }

        private void Session_FileTransferProgress(object sender, FileTransferProgressEventArgs e)
        {
            Action<FileTransferProgressEventArgs>? cb;
            lock (_progressLock)
            {
                cb = _progressCallback;
            }
            cb?.Invoke(e);
        }

        public async Task SynchronizeLocalAsync(string localPath, string remotePath, Action<FileTransferProgressEventArgs> progressCallback)
        {
            if (_session == null || !_session.Opened) return;

            lock (_progressLock)
            {
                _progressCallback = progressCallback;
            }

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var result = _session.SynchronizeDirectories(SynchronizationMode.Local, localPath, remotePath, false);
                        result.Check();
                    }
                    catch (SessionLocalException ex) when (ex.Message.Contains("Aborted", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new OperationCanceledException("Cancelled by user.", ex);
                    }
                    catch (Exception ex) when (ex.Message.Contains("Aborted", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new OperationCanceledException("Cancelled by user.", ex);
                    }
                });
            }
            finally
            {
                lock (_progressLock)
                {
                    _progressCallback = null;
                }
            }
        }

        public IEnumerable<RemoteFileInfo> ListFiles(string path)
        {
            if (_session == null || !_session.Opened) return [];
            return _session.ListDirectory(path).Files;
        }

        public void Dispose()
        {
            _session?.Dispose();
            GC.SuppressFinalize(this);
        }

        public void Abort()
        {
            try
            {
                _session?.Abort();
            }
            catch
            {
            }
        }
    }
}
