using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    /// <summary>A remote file flattened for comparison against a local tree.</summary>
    public class RemoteFileEntry
    {
        public string RemotePath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime LastWriteUtc { get; set; }
    }

    public enum SyncChangeKind
    {
        New = 0,
        Modified = 1,
        Unchanged = 2,
        RemoteOnly = 3
    }

    public class SyncChange
    {
        public string RelativePath { get; set; } = "";
        public SyncChangeKind Kind { get; set; }
        public long LocalSizeBytes { get; set; }
        public long RemoteSizeBytes { get; set; }
        public DateTime? LocalWriteUtc { get; set; }
        public DateTime? RemoteWriteUtc { get; set; }

        public long DeltaBytes => LocalSizeBytes - RemoteSizeBytes;
    }

    /// <summary>A read-only "what would change" report. Nothing is transferred to build it.</summary>
    public class SyncPlan
    {
        public string Service { get; set; } = "";
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        public List<SyncChange> Changes { get; set; } = new();

        public int NewCount => Changes.Count(c => c.Kind == SyncChangeKind.New);
        public int ModifiedCount => Changes.Count(c => c.Kind == SyncChangeKind.Modified);
        public int UnchangedCount => Changes.Count(c => c.Kind == SyncChangeKind.Unchanged);
        public int RemoteOnlyCount => Changes.Count(c => c.Kind == SyncChangeKind.RemoteOnly);

        /// <summary>Bytes that would actually be uploaded (new + changed).</summary>
        public long UploadBytes => Changes.Where(c => c.Kind is SyncChangeKind.New or SyncChangeKind.Modified)
                                          .Sum(c => c.LocalSizeBytes);

        public bool IsEmpty => NewCount == 0 && ModifiedCount == 0;

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var i = 0;
            double value = bytes;
            while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
            return $"{value:F1} {units[i]}";
        }

        public string Summary()
        {
            if (IsEmpty) return "Everything is already in sync. Nothing would be transferred.";

            return $"{NewCount} new, {ModifiedCount} changed, {UnchangedCount} unchanged, " +
                   $"{RemoteOnlyCount} only on the server. About {SyncPreviewService.FormatBytes(UploadBytes)} would upload.";
        }
    }

    /// <summary>A snapshot of the remote files a sync is about to overwrite.</summary>
    public class RollbackSnapshot
    {
        public string Id { get; set; } = "";
        public string Service { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }

        /// <summary>Relative path -> local snapshot file path.</summary>
        public Dictionary<string, string> Files { get; set; } = new();
    }
    public static class SyncPreviewService
    {
        private static readonly string RollbackRoot =
            Path.Combine(AppDataPaths.DataDirectory, "rollback");

        /// <summary>
        /// Builds a diff of what a sync would do, without transferring a single byte.
        /// Both listings are read and compared by relative path + modification time.
        /// </summary>
        public static async Task<SyncPlan> BuildPlanAsync(string service, int maxDepth = 8)
        {
            var plan = new SyncPlan { Service = service };
            var target = ResolveTarget(service);
            if (target == null)
            {
                plan.Changes.Add(new SyncChange { Kind = SyncChangeKind.Unchanged, RelativePath = "(service not configured)" });
                return plan;
            }

            using var ftp = new FtpService();
            ftp.Initialize(target.Value.Host, target.Value.User,
                SecurityService.GetDecryptedFtpPassword(),
                BackupConfig.FtpTlsFingerprint, target.Value.Port);

            if (!await ftp.ConnectAsync())
            {
                plan.Changes.Add(new SyncChange { Kind = SyncChangeKind.Unchanged, RelativePath = "(could not connect to the server)" });
                return plan;
            }

            var remote = ftp.ListFilesRecursive(target.Value.RemotePath, maxDepth)
                            .ToDictionary(r => r.RelativePath.Replace('\\', '/'), r => r, StringComparer.OrdinalIgnoreCase);

            foreach (var local in EnumerateLocal(target.Value.LocalPath))
            {
                var rel = local.RelativePath;
                if (remote.TryGetValue(rel, out var remoteFile))
                {
                    // Mirrors SynchronizationCriteria.Time: only a newer local file re-uploads.
                    var remoteTime = ToUtcSafe(remoteFile.LastWriteUtc);
                    var newer = local.WriteUtc > remoteTime.AddSeconds(1);

                    plan.Changes.Add(new SyncChange
                    {
                        RelativePath = rel,
                        Kind = newer ? SyncChangeKind.Modified : SyncChangeKind.Unchanged,
                        LocalSizeBytes = local.SizeBytes,
                        RemoteSizeBytes = remoteFile.SizeBytes,
                        LocalWriteUtc = local.WriteUtc,
                        RemoteWriteUtc = remoteTime
                    });
                }
                else
                {
                    plan.Changes.Add(new SyncChange
                    {
                        RelativePath = rel,
                        Kind = SyncChangeKind.New,
                        LocalSizeBytes = local.SizeBytes,
                        LocalWriteUtc = local.WriteUtc
                    });
                }
            }

            // Remote-only files are reported but never acted on: a backup must not delete.
            var seen = new HashSet<string>(plan.Changes.Select(c => c.RelativePath), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in remote)
            {
                if (!seen.Add(kv.Key)) continue;
                plan.Changes.Add(new SyncChange
                {
                    RelativePath = kv.Key,
                    Kind = SyncChangeKind.RemoteOnly,
                    RemoteSizeBytes = kv.Value.SizeBytes,
                    RemoteWriteUtc = ToUtcSafe(kv.Value.LastWriteUtc)
                });
            }

            return plan;
        }
        /// <summary>
        /// Downloads the remote copies of every file the next sync would overwrite, so the
        /// change can be undone. Only *modified* files are snapshotted - new files have no
        /// previous version to preserve, and unchanged files are not touched at all.
        /// </summary>
        public static async Task<RollbackSnapshot> CreateSnapshotAsync(SyncPlan plan)
        {
            var snapshot = new RollbackSnapshot
            {
                Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
                Service = plan.Service
            };

            var target = ResolveTarget(plan.Service);
            if (target == null) return snapshot;

            var toSnapshot = plan.Changes.Where(c => c.Kind == SyncChangeKind.Modified).ToList();
            if (toSnapshot.Count == 0) return snapshot;

            var dir = Path.Combine(RollbackRoot, plan.Service, snapshot.Id);
            Directory.CreateDirectory(dir);

            using var ftp = new FtpService();
            ftp.Initialize(target.Value.Host, target.Value.User,
                SecurityService.GetDecryptedFtpPassword(),
                BackupConfig.FtpTlsFingerprint, target.Value.Port);

            if (!await ftp.ConnectAsync()) return snapshot;

            foreach (var change in toSnapshot)
            {
                var localCopy = Path.Combine(dir, change.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var remotePath = CombineRemote(target.Value.RemotePath, change.RelativePath);

                if (ftp.TryDownloadFile(remotePath, localCopy))
                {
                    snapshot.Files[change.RelativePath] = localCopy;
                    snapshot.FileCount++;
                    snapshot.TotalBytes += change.RemoteSizeBytes;
                }
            }

            SaveManifest(snapshot);
            PruneOldSnapshots(plan.Service);
            return snapshot;
        }

        /// <summary>Re-uploads a snapshot, restoring the server to its previous state.</summary>
        public static async Task<(bool ok, string message)> RestoreSnapshotAsync(string service, string snapshotId)
        {
            var manifestPath = Path.Combine(RollbackRoot, service, snapshotId, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return (false, $"No rollback snapshot named '{snapshotId}' was found.");
            }

            var snapshot = JsonSerializer.Deserialize<RollbackSnapshot>(await File.ReadAllTextAsync(manifestPath));
            if (snapshot == null || snapshot.Files.Count == 0)
            {
                return (false, "That snapshot is empty, so there is nothing to restore.");
            }

            var target = ResolveTarget(service);
            if (target == null) return (false, "This service is not configured.");

            using var ftp = new FtpService();
            ftp.Initialize(target.Value.Host, target.Value.User,
                SecurityService.GetDecryptedFtpPassword(),
                BackupConfig.FtpTlsFingerprint, target.Value.Port);

            if (!await ftp.ConnectAsync()) return (false, "Could not connect to the server.");

            var restored = 0;
            var failed = 0;
            foreach (var kv in snapshot.Files)
            {
                if (!File.Exists(kv.Value)) { failed++; continue; }

                var remotePath = CombineRemote(target.Value.RemotePath, kv.Key);
                if (ftp.TryUploadFile(kv.Value, remotePath)) restored++;
                else failed++;
            }

            var message = failed == 0
                ? $"Restored {restored} file(s) from the {snapshot.CreatedAtUtc:yyyy-MM-dd HH:mm} snapshot."
                : $"Restored {restored} file(s); {failed} could not be restored.";

            return (failed == 0, message);
        }

        /// <summary>Lists available snapshots for a service, newest first.</summary>
        public static List<RollbackSnapshot> ListSnapshots(string service)
        {
            var results = new List<RollbackSnapshot>();
            var dir = Path.Combine(RollbackRoot, service);
            if (!Directory.Exists(dir)) return results;

            foreach (var manifest in Directory.GetFiles(dir, "manifest.json", SearchOption.AllDirectories))
            {
                try
                {
                    var snap = JsonSerializer.Deserialize<RollbackSnapshot>(File.ReadAllText(manifest));
                    if (snap != null) results.Add(snap);
                }
                catch { /* a corrupt manifest must not hide the healthy ones */ }
            }

            return results.OrderByDescending(s => s.CreatedAtUtc).ToList();
        }

        /// <summary>Writes a snapshot manifest into the snapshot folder. Shared with the in-session path.</summary>
        public static void WriteSnapshotManifest(RollbackSnapshot snapshot, string directory)
        {
            File.WriteAllText(
                Path.Combine(directory, "manifest.json"),
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Deletes snapshots beyond the configured history depth.</summary>
        public static void PruneSnapshots(string service)
        {
            var keep = Math.Clamp(ConfigService.Current.Operation.RollbackHistoryCount, 0, 50);
            var dir = Path.Combine(RollbackRoot, service);
            if (!Directory.Exists(dir)) return;

            foreach (var old in ListSnapshots(service).Skip(keep))
            {
                try { Directory.Delete(Path.Combine(dir, old.Id), true); } catch { }
            }
        }

        private static void SaveManifest(RollbackSnapshot snapshot)
        {
            var dir = Path.GetDirectoryName(snapshot.Files.Values.FirstOrDefault() ?? "");
            if (string.IsNullOrEmpty(dir)) return;
            WriteSnapshotManifest(snapshot, dir);
        }

        private static void PruneOldSnapshots(string service)
        {
            PruneSnapshots(service);
        }
        /// <summary>Human-readable byte size, shared by the plan summary and the markdown report.</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var i = 0;
            double value = bytes;
            while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
            return $"{value:F1} {units[i]}";
        }

        private static (string LocalPath, string RemotePath, string Host, string User, int Port)? ResolveTarget(string service)
        {
            var key = (service ?? "").Trim().ToLowerInvariant();

            if (key.Contains("sql") || key.Contains("database"))
            {
                var local = BackupConfig.SqlLocalFolder;
                if (string.IsNullOrWhiteSpace(local)) return null;
                return (local, BackupConfig.SqlRemotePath, BackupConfig.FtpHost, BackupConfig.SqlUser, BackupConfig.FtpPort);
            }

            var ftpLocal = BackupConfig.FtpLocalFolder;
            if (string.IsNullOrWhiteSpace(ftpLocal)) return null;
            return (ftpLocal, "/", BackupConfig.FtpHost, BackupConfig.FtpUser, BackupConfig.FtpPort);
        }

        private static List<(string RelativePath, long SizeBytes, DateTime WriteUtc)> EnumerateLocal(string root)
        {
            var results = new List<(string, long, DateTime)>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return results;

            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file);

                    // Skip in-flight and editor scratch files; they are not part of the backup.
                    if (name.EndsWith(".filepart", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith("~$")) continue;
                    if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                    var info = new FileInfo(file);
                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                    results.Add((relative, info.Length, ToUtcSafe(info.LastWriteTime)));
                }
            }
            catch
            {
                // A locked file should not break the preview for everything else.
            }

            return results;
        }

        private static DateTime ToUtcSafe(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            };
        }

        private static string CombineRemote(string root, string relative)
        {
            var r = (root ?? "/").Replace('\\', '/').TrimEnd('/');
            var rel = (relative ?? "").Replace('\\', '/').TrimStart('/');
            return string.IsNullOrEmpty(r) ? "/" + rel : $"{r}/{rel}";
        }

        /// <summary>Renders a plan as a compact markdown report for the assistant and the web UI.</summary>
        public static string DescribePlan(SyncPlan plan, int maxRows = 15)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"### 📋 Sync Preview - {plan.Service.ToUpperInvariant()}");
            sb.AppendLine(plan.Summary());

            var interesting = plan.Changes
                .Where(c => c.Kind is SyncChangeKind.New or SyncChangeKind.Modified)
                .OrderByDescending(c => c.Kind == SyncChangeKind.Modified)
                .Take(maxRows)
                .ToList();

            if (interesting.Count == 0)
            {
                sb.AppendLine("\nNo uploads would happen.");
                return sb.ToString();
            }

            sb.AppendLine();
            foreach (var c in interesting)
            {
                var icon = c.Kind == SyncChangeKind.New ? "🆕" : "✏️";
                var delta = c.Kind == SyncChangeKind.Modified ? $" (was {FormatBytes(c.RemoteSizeBytes)})" : "";
                sb.AppendLine($"- {icon} `{c.RelativePath}` → {FormatBytes(c.LocalSizeBytes)}{delta}");
            }

            var remaining = plan.Changes.Count(c => c.Kind is SyncChangeKind.New or SyncChangeKind.Modified) - interesting.Count;
            if (remaining > 0) sb.AppendLine($"\n…and {remaining} more file(s).");

            return sb.ToString();
        }
    }
}