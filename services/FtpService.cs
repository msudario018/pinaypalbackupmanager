using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinSCP;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{    public class FtpService : IDisposable
    {
        private readonly object _progressLock = new();
        private Session? _session;
        private SessionOptions? _options;
        private Action<FileTransferProgressEventArgs>? _progressCallback;

        public void Initialize(string host, string user, string password, string fingerprint = "", int port = 21)
        {
            LogService.WriteLiveLog($"FTP INIT: Connecting to server on port {port}", AppDataPaths.SystemLogPath, "Information", "SYSTEM");
            
            if (string.IsNullOrEmpty(password))
            {
                LogService.WriteLiveLog("FTP INIT WARNING: Password is empty after decryption!", AppDataPaths.SystemLogPath, "Warning", "SYSTEM");
            }

            bool useTls = !string.IsNullOrWhiteSpace(fingerprint);
            _options = new SessionOptions
            {
                Protocol = Protocol.Ftp,
                HostName = host,
                UserName = user,
                Password = password,
                PortNumber = port,
                FtpSecure = useTls ? FtpSecure.Explicit : FtpSecure.None
            };
            if (useTls)
            {
                _options.TlsHostCertificateFingerprint = fingerprint;
            }
        }

        public async Task<bool> ConnectAsync()
        {
            if (_session != null && _session.Opened) 
            {
                LogService.WriteLiveLog("FTP CONNECT: Session already open.", AppDataPaths.SystemLogPath, "Information", "SYSTEM");
                return true;
            }
            if (_options == null) 
            {
                LogService.WriteLiveLog("FTP CONNECT ERROR: Options not initialized.", AppDataPaths.SystemLogPath, "Error", "SYSTEM");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    LogService.WriteLiveLog("FTP CONNECT: Opening session...", AppDataPaths.SystemLogPath, "Information", "SYSTEM");
                    _session = new Session();
                    _session.FileTransferProgress += Session_FileTransferProgress;
                    _session.Open(_options);
                    LogService.WriteLiveLog("FTP CONNECT: Session opened successfully.", AppDataPaths.SystemLogPath, "Information", "SYSTEM");
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.WriteLiveLog($"FTP CONNECTION FAILED: {ex.Message}", AppDataPaths.SystemLogPath, "Error", "SYSTEM");
                    if (ex.InnerException != null)
                        LogService.WriteLiveLog($"FTP INNER ERROR: {ex.InnerException.Message}", AppDataPaths.SystemLogPath, "Error", "SYSTEM");
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

        public async Task<bool> SynchronizeRemoteAsync(string localPath, string remotePath, Action<FileTransferProgressEventArgs> progressCallback)
        {
            if (_session == null || !_session.Opened) return false;

            lock (_progressLock)
            {
                _progressCallback = progressCallback;
            }

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        var result = _session.SynchronizeDirectories(SynchronizationMode.Remote, localPath, remotePath, false);
                        result.Check();
                        return true;
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

        public void Dispose()
        {
            if (_session != null)
            {
                try
                {
                    if (_session.Opened)
                        _session.Close();
                }
                catch { }

                try
                {
                    _session.FileTransferProgress -= Session_FileTransferProgress;
                }
                catch { /* WinSCP may not allow removing handlers from an opened session */ }

                try
                {
                    _session.Dispose();
                }
                catch { }

                _session = null;
            }
            GC.SuppressFinalize(this);
        }

        public IEnumerable<RemoteFileInfo> ListFiles(string path)
        {
            if (_session == null || !_session.Opened) return [];
            return _session.ListDirectory(path).Files;
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
