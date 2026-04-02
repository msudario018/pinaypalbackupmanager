using System;
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

                var notes = target.NotesMarkdown;
                if (string.IsNullOrWhiteSpace(notes))
                {
                    notes = StripHtml(target.NotesHTML);
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

        private static string StripHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            var noTags = Regex.Replace(html, "<.*?>", string.Empty);
            return System.Net.WebUtility.HtmlDecode(noTags).Trim();
        }
    }
}
