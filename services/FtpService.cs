using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>Minimum gap between forwarded progress ticks, to keep the UI thread free.</summary>
        private static readonly TimeSpan MinProgressInterval = TimeSpan.FromMilliseconds(120);

        private DateTime _lastProgressDispatch = DateTime.MinValue;

        public void Initialize(string host, string user, string password, string fingerprint = "", int port = 21)
        {
            LogService.WriteLiveLog($"FTP INIT: Connecting to server on port {port}", AppDataPaths.SystemLogPath, "Information", "SYSTEM");
            
            if (string.IsNullOrEmpty(password))
            {
                LogService.WriteLiveLog("FTP INIT WARNING: Password is empty after decryption!", AppDataPaths.SystemLogPath, "Warning", "SYSTEM");
            }

            bool useTls = !string.IsNullOrWhiteSpace(fingerprint) || ConfigService.Current.Operation.AcceptAnyTlsCert;
            _options = new SessionOptions
            {
                Protocol = Protocol.Ftp,
                HostName = host,
                UserName = user,
                Password = password,
                PortNumber = port,
                FtpSecure = useTls ? FtpSecure.Explicit : FtpSecure.None
            };

            // Use passive data connections: this is the only mode that works reliably
            // behind a home router / NAT, where an active connection from the server
            // cannot reach this PC.
            _options.FtpMode = FtpMode.Passive;
            if (ConfigService.Current.Operation.AcceptAnyTlsCert)
            {
                _options.GiveUpSecurityAndAcceptAnyTlsHostCertificate = true;
            }
            else if (useTls && !string.IsNullOrWhiteSpace(fingerprint))
            {
                _options.TlsHostCertificateFingerprint = fingerprint;
            }
        }

        /// <summary>
        /// Builds the transfer options shared by every directory sync.
        ///
        /// This is the real reason a sync can crawl at kilobytes-per-second:
        ///  - <see cref="TransferOptions.SpeedLimit"/> is a KB/s cap. 0 means unlimited, and we set
        ///    it explicitly so a previously persisted throttle can never silently throttle us.
        ///  - <see cref="TransferOptions.ResumeSupport"/> defaults to off, so an interrupted upload
        ///    restarts from byte zero. Smart resume lets WinSCP continue partial files instead.
        ///  - Binary mode avoids a slow ASCII translation path for text and binary alike.
        /// </summary>
        internal static TransferOptions BuildTransferOptions()
        {
            return new TransferOptions
            {
                SpeedLimit = 0, // 0 = unlimited; never throttle a backup
                TransferMode = TransferMode.Binary,

                // Resume partial uploads instead of restarting from byte zero. Without this an
                // interrupted file re-transfers in full, which is what "sync stuck in KB/s" looks like.
                OverwriteMode = OverwriteMode.Resume,
                ResumeSupport = new TransferResumeSupport
                {
                    State = TransferResumeSupportState.Smart,
                    Threshold = 100 * 1024 // only bother resuming files >= 100 KB
                }
            };
        }

        public static string ScanTlsFingerprint(string host, int port = 21)
        {
            try
            {
                var options = new SessionOptions
                {
                    Protocol = Protocol.Ftp,
                    HostName = host,
                    PortNumber = port,
                    FtpSecure = FtpSecure.Explicit
                };
                using var session = new Session();
                return session.ScanFingerprint(options, "SHA-256") ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FtpService] ScanTlsFingerprint failed: {ex.Message}", "Warning", "SYSTEM");
                return string.Empty;
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

                    // Handle TLS certificate rotation / fingerprint mismatch
                    if (ConfigService.Current.Operation.AutoUpdateTlsFingerprint && 
                        (ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) || 
                         ex.Message.Contains("fingerprint", StringComparison.OrdinalIgnoreCase) ||
                         ex.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            LogService.WriteLiveLog("FTP: Attempting auto-recovery for rotated TLS certificate...", AppDataPaths.SystemLogPath, "Information", "SYSTEM");
                            string newFingerprint = ScanTlsFingerprint(_options.HostName, _options.PortNumber);
                            if (!string.IsNullOrWhiteSpace(newFingerprint) && newFingerprint != _options.TlsHostCertificateFingerprint)
                            {
                                LogService.WriteLiveLog($"FTP: New TLS fingerprint detected: {newFingerprint}. Updating configuration...", AppDataPaths.SystemLogPath, "Information", "SYSTEM");
                                _options.TlsHostCertificateFingerprint = newFingerprint;
                                ConfigService.Current.Ftp.TlsFingerprint = newFingerprint;
                                try { ConfigService.SaveCredentials(); } catch { }
                                NotificationService.ShowBackupToast("TLS Updated", "Server TLS certificate changed and fingerprint was automatically updated.", "Info");

                                // Retry with new fingerprint
                                _session?.Dispose();
                                _session = new Session();
                                _session.FileTransferProgress += Session_FileTransferProgress;
                                _session.Open(_options);
                                LogService.WriteLiveLog("FTP CONNECT: Successfully connected with updated TLS certificate.", AppDataPaths.SystemLogPath, "Information", "SYSTEM");
                                return true;
                            }
                        }
                        catch (Exception retryEx)
                        {
                            LogService.WriteLiveLog($"FTP AUTO-RECOVERY FAILED: {retryEx.Message}", AppDataPaths.SystemLogPath, "Error", "SYSTEM");
                        }
                    }

                    return false;
                }
            });
        }

        /// <summary>
        /// WinSCP raises this event very frequently - often many times per second per file.
        /// Forwarding every tick to the UI (which then marshals, formats and re-renders) costs
        /// more than the transfer itself, so intermediate ticks are throttled. The final tick
        /// (OverallProgress >= 1.0) is always forwarded so the UI settles on "complete".
        /// </summary>
        private void Session_FileTransferProgress(object sender, FileTransferProgressEventArgs e)
        {
            Action<FileTransferProgressEventArgs>? cb;
            lock (_progressLock)
            {
                cb = _progressCallback;
            }
            if (cb == null) return;

            // WinSCP fires this event several times per second per file. Forwarding every tick to the UI
            // thread costs more than the transfer itself, so we always forward the final tick
            // (OverallProgress >= 1.0) and throttle the intermediate ones.
            var isFinalTick = e.OverallProgress >= 1.0;

            if (!isFinalTick)
            {
                lock (_progressLock)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastProgressDispatch) < MinProgressInterval) return;
                    _lastProgressDispatch = now;
                }
            }

            cb.Invoke(e);
        }

        /// <summary>
        /// Synchronises a directory tree.
        ///
        /// The real overload is (mode, localPath, remotePath, removeFiles, mirror, criteria, options).
        /// The old code supplied only the first four, inheriting default criteria and, more
        /// importantly, default transfer options where <c>ResumeSupport</c> was off — meaning an
        /// interrupted upload restarted from byte zero, which shows up as a crawl in KB/s.
        ///
        /// removeFiles = false and mirror = false are preserved deliberately: a backup must never
        /// remove remote files because of a bad local scan.
        /// </summary>
        private void RunSync(SynchronizationMode mode, string localPath, string remotePath)
        {
            var result = _session!.SynchronizeDirectories(
                mode,
                localPath,
                remotePath,
                removeFiles: false,
                mirror: false,
                criteria: SynchronizationCriteria.Time,
                options: BuildTransferOptions());
            result.Check();
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
                // Snapshot the remote copies of anything we are about to overwrite, so the
                // change can be rolled back. Best effort: a failed snapshot must not stop a
                // backup, so failures are logged and ignored.
                if (ConfigService.Current.Operation.EnableSyncRollback)
                {
                    try
                    {
                        var plan = await BuildInSessionPlanAsync(localPath, remotePath);
                        if (plan != null && plan.ModifiedCount > 0)
                        {
                            var snapshot = await CreateInSessionSnapshotAsync(plan, remotePath);
                            if (snapshot.FileCount > 0)
                            {
                                LogService.WriteSystemLog(
                                    $"[FtpService] Rollback snapshot '{snapshot.Id}' captured ({snapshot.FileCount} file(s), {SyncPreviewService.FormatBytes(snapshot.TotalBytes)}).",
                                    "Information", "SYSTEM");
                            }
                        }
                    }
                    catch (Exception snapEx)
                    {
                        LogService.WriteSystemLog($"[FtpService] Rollback snapshot skipped: {snapEx.Message}", "Warning", "SYSTEM");
                    }
                }

                await Task.Run(() =>
                {
                    try
                    {
                        RunSync(SynchronizationMode.Local, localPath, remotePath);
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
                        RunSync(SynchronizationMode.Remote, localPath, remotePath);
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

        /// <summary>
        /// Walks a remote tree and returns every file as a flat, comparable record.
        /// Used by the sync preview to produce a diff without transferring anything.
        /// Depth is capped so a symlink loop or a huge tree cannot hang the caller.
        /// </summary>
        public List<RemoteFileEntry> ListFilesRecursive(string remotePath, int maxDepth = 8)
        {
            var results = new List<RemoteFileEntry>();
            if (_session == null || !_session.Opened) return results;

            // WinSCP 6.x exposes only a single-level listing, so we walk manually: every
            // returned file reveals its parent directory, which becomes the next work item.
            var root = (remotePath ?? "/").Replace('\\', '/').TrimEnd('/');
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                var (dir, depth) = queue.Dequeue();
                if (depth > maxDepth) continue;

                RemoteDirectoryInfo info;
                try
                {
                    info = _session.ListDirectory(dir);
                }
                catch
                {
                    continue; // An unreadable sub-folder must not abort the whole scan.
                }

                foreach (var file in info.Files)
                {
                    // Skip "." and ".." pseudo entries.
                    if (file.Name == "." || file.Name == "..") continue;
                    if (file.IsDirectory) continue;

                    var full = (file.FullName ?? "").Replace('\\', '/');
                    var relative = MakeRelative(root, full);
                    if (string.IsNullOrEmpty(relative)) continue;

                    results.Add(new RemoteFileEntry
                    {
                        RemotePath = full,
                        RelativePath = relative,
                        SizeBytes = file.Length,
                        LastWriteUtc = file.LastWriteTime
                    });

                    // Register the containing folder for a later listing.
                    var parent = full.Contains('/') ? full.Substring(0, full.LastIndexOf('/')) : dir;
                    if (!string.IsNullOrEmpty(parent) && visited.Add(parent))
                    {
                        queue.Enqueue((parent, depth + 1));
                    }
                }
            }

            return results;
        }

        private static string MakeRelative(string root, string fullPath)
        {
            var r = (root ?? "/").Replace('\\', '/').TrimEnd('/');
            var f = (fullPath ?? "").Replace('\\', '/');
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? f.Substring(r.Length).TrimStart('/') : f.TrimStart('/');
        }

        /// <summary>Downloads a single remote file, used to snapshot before an overwrite.</summary>
        public bool TryDownloadFile(string remotePath, string localPath)
        {
            if (_session == null || !_session.Opened) return false;
            try
            {
                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var transfer = _session.GetFiles(remotePath, localPath, false, new TransferOptions
                {
                    TransferMode = TransferMode.Binary
                });
                transfer.Check();
                return File.Exists(localPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Uploads a single local file, used to restore a rollback snapshot.</summary>
        public bool TryUploadFile(string localPath, string remotePath)
        {
            if (_session == null || !_session.Opened) return false;
            try
            {
                var transfer = _session.PutFiles(localPath, remotePath, false, new TransferOptions
                {
                    TransferMode = TransferMode.Binary
                });
                transfer.Check();
                return true;
            }
            catch
            {
                return false;
            }
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
    /// <summary>
    /// Builds the diff using the already-open session, so the caller does not pay for a
    /// second connection. Mirrors SyncPreviewService.BuildPlanAsync.
    /// </summary>
    /// <summary>FTP servers report times inconsistently, so normalise the kind before comparing.</summary>
        private static DateTime ToUtcSafe(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };

        private SyncPlan? BuildInSessionPlan(string localPath, string remotePath)
        {
            if (_session == null || !_session.Opened) return null;
            if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath)) return null;

            var remote = ListFilesRecursive(remotePath)
                            .ToDictionary(r => r.RelativePath, r => r, StringComparer.OrdinalIgnoreCase);

            var plan = new SyncPlan { Service = "ftp" };

            foreach (var file in Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".filepart", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("~$")) continue;

                var relative = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                var info = new FileInfo(file);

                if (remote.TryGetValue(relative, out var remoteFile))
                {
                    var newer = info.LastWriteTimeUtc > ToUtcSafe(remoteFile.LastWriteUtc).AddSeconds(1);
                    plan.Changes.Add(new SyncChange
                    {
                        RelativePath = relative,
                        Kind = newer ? SyncChangeKind.Modified : SyncChangeKind.Unchanged,
                        LocalSizeBytes = info.Length,
                        RemoteSizeBytes = remoteFile.SizeBytes,
                        LocalWriteUtc = info.LastWriteTimeUtc
                    });
                }
                else
                {
                    plan.Changes.Add(new SyncChange
                    {
                        RelativePath = relative,
                        Kind = SyncChangeKind.New,
                        LocalSizeBytes = info.Length,
                        LocalWriteUtc = info.LastWriteTimeUtc
                    });
                }
            }

            return plan;
        }

        private async Task<SyncPlan?> BuildInSessionPlanAsync(string localPath, string remotePath)
        {
            return await Task.Run(() => BuildInSessionPlan(localPath, remotePath));
        }

        /// <summary>Downloads the previous remote version of every file the plan marks Modified.</summary>
        private async Task<RollbackSnapshot> CreateInSessionSnapshotAsync(SyncPlan plan, string remoteRoot)
        {
            return await Task.Run(() =>
            {
                var service = "ftp";
                var snapshot = new RollbackSnapshot
                {
                    Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
                    Service = service
                };

                var dir = Path.Combine(AppDataPaths.DataDirectory, "rollback", service, snapshot.Id);
                Directory.CreateDirectory(dir);

                var root = (remoteRoot ?? "/").Replace('\\', '/').TrimEnd('/');
                foreach (var change in plan.Changes.Where(c => c.Kind == SyncChangeKind.Modified))
                {
                    var localCopy = Path.Combine(dir, change.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    var fullRemote = root.TrimEnd('/') + "/" + change.RelativePath;

                    if (TryDownloadFile(fullRemote, localCopy))
                    {
                        snapshot.Files[change.RelativePath] = localCopy;
                        snapshot.FileCount++;
                        snapshot.TotalBytes += change.RemoteSizeBytes;
                    }
                }

                SyncPreviewService.WriteSnapshotManifest(snapshot, dir);
                SyncPreviewService.PruneSnapshots(service);
                return snapshot;
            });
        }
        // ANCHOR_FTP
    }
}
