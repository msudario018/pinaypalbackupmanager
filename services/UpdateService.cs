using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MsBox.Avalonia.Enums;
using Velopack;
using Velopack.Sources;
using Avalonia.Controls;
using PinayPalBackupManager.UI.UserControls;

namespace PinayPalBackupManager.Services
{
    public static class UpdateService
    {
        private const string RepoUrl = "https://github.com/msudario018/pinaypalbackupmanager";
        private const string GithubApiLatestRelease = "https://api.github.com/repos/msudario018/pinaypalbackupmanager/releases/latest";
        private static readonly HttpClient _httpClient = new HttpClient();

        static UpdateService()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PinayPalBackupManager-Updater/3.7.1");
                _httpClient.Timeout = TimeSpan.FromSeconds(15);
            }
            catch { }
        }

        public static async Task CheckForUpdatesWithUiAsync(bool silentIfNone = false)
        {
            try
            {
                UpdateManager? mgr = null;
                bool isInstalled = false;

                try
                {
                    mgr = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));
                    isInstalled = mgr.IsInstalled;
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[UpdateService] UpdateManager initialization note: {ex.Message}", "Information", "SYSTEM");
                }

                if (isInstalled && mgr != null)
                {
                    var update = await mgr.CheckForUpdatesAsync();
                    if (update == null)
                    {
                        if (!silentIfNone)
                        {
                            await ShowSimpleDialogAsync("You are up to date.", "Updates", Icon.Info);
                        }
                        return;
                    }

                    var target = update.TargetFullRelease;
                    var version = target.Version?.ToString() ?? "(unknown)";

                    var notes = GetChangelogFromLocalFile();
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

                    await RunInstalledUpdateWorkflowAsync(mgr, update, version, notes);
                }
                else
                {
                    // Portable or unmanaged installation: query GitHub releases API directly
                    var (hasUpdate, remoteVersion, releaseUrl, releaseNotes) = await CheckGithubReleaseDirectAsync();
                    if (!hasUpdate)
                    {
                        if (!silentIfNone)
                        {
                            await ShowSimpleDialogAsync("You are up to date.", "Updates", Icon.Info);
                        }
                        return;
                    }

                    var notes = GetChangelogFromLocalFile();
                    if (string.IsNullOrWhiteSpace(notes))
                    {
                        notes = releaseNotes;
                    }
                    if (string.IsNullOrWhiteSpace(notes))
                    {
                        notes = "(No release notes provided)";
                    }

                    await RunPortableUpdateWorkflowAsync(remoteVersion, notes, releaseUrl);
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[UpdateService] Update check failed: {ex}", "Warning", "SYSTEM");
                if (!silentIfNone)
                {
                    await ShowSimpleDialogAsync($"Update check failed: {ex.Message}", "Updates", Icon.Error);
                }
                else
                {
                    NotificationService.ShowBackupToast("Updates", "Update check failed.", "Warning");
                }
            }
        }

        private static async Task<(bool hasUpdate, string remoteVersion, string releaseUrl, string notes)> CheckGithubReleaseDirectAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(GithubApiLatestRelease);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
                var htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? RepoUrl : RepoUrl;
                var bodyNotes = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";

                var cleanRemote = tagName.TrimStart('v', 'V');
                var currentVer = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(3, 7, 0);

                if (Version.TryParse(cleanRemote, out var remoteVer))
                {
                    if (remoteVer > currentVer)
                    {
                        return (true, tagName, htmlUrl, bodyNotes);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(cleanRemote))
                {
                    var currentStr = $"{currentVer.Major}.{currentVer.Minor}.{currentVer.Build}";
                    if (!string.Equals(cleanRemote, currentStr, StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, tagName, htmlUrl, bodyNotes);
                    }
                }

                return (false, "", "", "");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[UpdateService] Direct GitHub check failed: {ex.Message}", "Warning", "SYSTEM");
                return (false, "", "", "");
            }
        }

        private static async Task RunInstalledUpdateWorkflowAsync(UpdateManager mgr, UpdateInfo update, string version, string changelog)
        {
            var dialog = new UpdateAvailableDialog(version, changelog, isInstalled: true);
            var window = new Window
            {
                Title = "Update Available",
                Content = dialog,
                Width = 520,
                Height = 410,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = Avalonia.Media.Brushes.Transparent
            };

            bool installStarted = false;

            dialog.OnYes += async (sender, e) =>
            {
                if (installStarted) return;
                installStarted = true;

                try
                {
                    dialog.ShowProgressMode();
                    NotificationService.ShowBackupToast("Updates", "Downloading update package...", "Info");

                    await mgr.DownloadUpdatesAsync(update, progress =>
                    {
                        dialog.SetDownloadProgress(progress, $"Downloading update ({progress}%)... Please wait");
                    });

                    dialog.SetInstallingMode();
                    NotificationService.ShowBackupToast("Updates", "Installing update and restarting...", "Info");

                    await Task.Delay(500);

                    // Cleanly stop child daemons and listener to release all locks
                    try { CloudflareTunnelService.StopQuickTunnel(); } catch { }
                    try { FileDownloadService.Stop(); } catch { }

                    mgr.ApplyUpdatesAndRestart(update);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[UpdateService] Error applying update: {ex.Message}", "Error", "SYSTEM");
                    await ShowSimpleDialogAsync($"Failed to install update: {ex.Message}", "Update Error", Icon.Error);
                    window.Close();
                }
            };

            dialog.OnNo += (sender, e) =>
            {
                window.Close();
            };

            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow != null && mainWindow.IsVisible && mainWindow.WindowState != Avalonia.Controls.WindowState.Minimized)
            {
                await window.ShowDialog(mainWindow);
            }
            else
            {
                window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen;
                window.Show();
            }
        }

        private static async Task RunPortableUpdateWorkflowAsync(string remoteVersion, string changelog, string releaseUrl)
        {
            var dialog = new UpdateAvailableDialog(remoteVersion, changelog, isInstalled: false);
            var window = new Window
            {
                Title = "Update Available",
                Content = dialog,
                Width = 520,
                Height = 410,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = Avalonia.Media.Brushes.Transparent
            };

            dialog.OnYes += (sender, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = releaseUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[UpdateService] Failed to launch browser: {ex.Message}", "Warning", "SYSTEM");
                }
                window.Close();
            };

            dialog.OnNo += (sender, e) =>
            {
                window.Close();
            };

            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow != null && mainWindow.IsVisible && mainWindow.WindowState != Avalonia.Controls.WindowState.Minimized)
            {
                await window.ShowDialog(mainWindow);
            }
            else
            {
                window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen;
                window.Show();
            }
        }

        private static async Task ShowSimpleDialogAsync(string message, string title, Icon icon)
        {
            await NotificationService.ShowMessageBoxAsync(message, title, ButtonEnum.Ok, icon);
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
                    return ExtractLatestChangelog(content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Failed to read changelog: {ex.Message}");
            }
            
            return string.Empty;
        }

        private static System.Timers.Timer? _backgroundUpdateTimer;
        private static readonly object _timerLock = new();

        public static void StartPeriodicBackgroundChecks()
        {
            lock (_timerLock)
            {
                if (_backgroundUpdateTimer != null) return;

                // Initial delayed check after 45 seconds to let startup and network stabilize
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(45000);
                        if (UpdatePreferences.LoadAutoCheckOnStartup())
                        {
                            await CheckForUpdatesWithUiAsync(silentIfNone: true);
                        }
                    }
                    catch { }
                });

                // Periodic check every 4 hours (14,400,000 ms)
                _backgroundUpdateTimer = new System.Timers.Timer(TimeSpan.FromHours(4).TotalMilliseconds);
                _backgroundUpdateTimer.AutoReset = true;
                _backgroundUpdateTimer.Elapsed += async (s, e) =>
                {
                    try
                    {
                        if (UpdatePreferences.LoadAutoCheckOnStartup())
                        {
                            await CheckForUpdatesWithUiAsync(silentIfNone: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.WriteSystemLog($"[UpdateService] Periodic check error: {ex.Message}", "Warning", "SYSTEM");
                    }
                };
                _backgroundUpdateTimer.Start();
                LogService.WriteSystemLog("[UpdateService] Periodic 4-hour background update watchdog started.", "Information", "SYSTEM");
            }
        }

        private static string ExtractLatestChangelog(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var start = -1;
            
            // Find first version header (e.g., "## v3.7.1", "## 3.7.1", or "## [3.7.1]")
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("## ") && !line.StartsWith("### "))
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
                if (i != start && line.Trim().StartsWith("## ") && !line.Trim().StartsWith("### ")) break;
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
