using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MsBox.Avalonia.Enums;
using Velopack;
using Velopack.Sources;

namespace PinayPalBackupManager.Services
{
    public static class UpdateService
    {
        private const string RepoUrl = "https://github.com/msudario018/pinaypalbackupmanager";

        public static async Task CheckForUpdatesWithUiAsync(bool silentIfNone = false)
        {
            try
            {
                var mgr = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));
                var update = await mgr.CheckForUpdatesAsync();
                if (update == null)
                {
                    if (!silentIfNone)
                    {
                        await NotificationService.ShowMessageBoxAsync("You are up to date.", "Updates", ButtonEnum.Ok, Icon.Info);
                    }
                    return;
                }

                var target = update.TargetFullRelease;
                var version = target.Version?.ToString() ?? "(unknown)";

                // Try to get changelog from local CHANGELOG.md first
                var notes = GetChangelogFromLocalFile();
                
                // Fall back to Velopack release notes if local file not available
                if (string.IsNullOrWhiteSpace(notes))
                {
                    notes = target.NotesMarkdown;
                    if (string.IsNullOrWhiteSpace(notes))
                    {
                        notes = StripHtml(target.NotesHTML);
                    }
                }

                if (string.IsNullOrWhiteSpace(notes))
                {
                    notes = "(No release notes provided)";
                }

                var msg = $"Update available: v{version}\n\nWhat's New:\n{notes}\n\nInstall update now?";
                var ok = await NotificationService.ConfirmAsync(msg, "Update Available", Icon.Info);
                if (!ok) return;

                NotificationService.ShowBackupToast("Updates", "Downloading update...", "Info");
                await mgr.DownloadUpdatesAsync(update);

                NotificationService.ShowBackupToast("Updates", "Installing update...", "Info");
                mgr.ApplyUpdatesAndRestart(update);
            }
            catch (Exception ex)
            {
                if (!silentIfNone)
                {
                    await NotificationService.ShowMessageBoxAsync($"Update check failed: {ex.Message}", "Updates", ButtonEnum.Ok, Icon.Error);
                }
                else
                {
                    NotificationService.ShowBackupToast("Updates", "Update check failed.", "Warning");
                }
            }
        }

        private static string GetChangelogFromLocalFile()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
                var changelogPath = Path.Combine(baseDir, "CHANGELOG.md");
                
                if (File.Exists(changelogPath))
                {
                    var content = File.ReadAllText(changelogPath);
                    // Extract the first version section (latest release)
                    return ExtractLatestChangelog(content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Failed to read changelog: {ex.Message}");
            }
            
            return string.Empty;
        }

        private static string ExtractLatestChangelog(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var start = -1;
            
            // Find first version header (e.g., "## [2.6.13]")
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("## [") && line.Contains("]"))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0) return string.Empty;

            var sb = new System.Text.StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                var line = lines[i];
                // Stop at next version header
                if (i != start && line.StartsWith("## [")) break;
                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        private static string StripHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            var noTags = Regex.Replace(html, "<.*?>", string.Empty);
            return System.Net.WebUtility.HtmlDecode(noTags).Trim();
        }
    }
}
