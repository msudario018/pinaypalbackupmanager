using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Cached external availability check for the website being protected by this
    /// product. It intentionally measures the public HTTPS endpoint, not FTP or
    /// the local backup folder.
    /// </summary>
    public static class WebsiteMonitoringService
    {
        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static WebsiteStatus _last = WebsiteStatus.Unknown();

        static WebsiteMonitoringService()
        {
            Client.DefaultRequestHeaders.UserAgent.ParseAdd("PinayPalBackupManager/1.0 WebsiteMonitor");
        }

        public static async Task<WebsiteStatus> GetStatusAsync(bool force = false)
        {
            if (!force && DateTime.UtcNow - _last.CheckedAt < TimeSpan.FromSeconds(60))
            {
                return _last.Clone();
            }

            await Gate.WaitAsync();
            try
            {
                if (!force && DateTime.UtcNow - _last.CheckedAt < TimeSpan.FromSeconds(60))
                {
                    return _last.Clone();
                }

                var previous = _last;
                var now = DateTime.UtcNow;
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, WebsiteStatus.WebsiteUrl);
                    using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    stopwatch.Stop();
                    var online = (int)response.StatusCode is >= 200 and < 400;
                    _last = new WebsiteStatus
                    {
                        Url = WebsiteStatus.WebsiteUrl,
                        IsOnline = online,
                        StatusCode = (int)response.StatusCode,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        Error = online ? null : $"Website returned HTTP {(int)response.StatusCode}.",
                        CheckedAt = now,
                        ConsecutiveFailures = online ? 0 : previous.ConsecutiveFailures + 1,
                        ChangedAt = previous.IsOnline == online ? previous.ChangedAt : now
                    };
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _last = new WebsiteStatus
                    {
                        Url = WebsiteStatus.WebsiteUrl,
                        IsOnline = false,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        Error = ex.Message,
                        CheckedAt = now,
                        ConsecutiveFailures = previous.ConsecutiveFailures + 1,
                        ChangedAt = previous.IsOnline ? now : previous.ChangedAt
                    };
                }

                if (previous.CheckedAt != DateTime.MinValue && previous.IsOnline != _last.IsOnline)
                {
                    var state = _last.IsOnline ? "recovered" : "is unreachable";
                    LogService.WriteSystemLog($"[WebsiteMonitor] pinaypal.net {state}. {_last.Summary}", _last.IsOnline ? "Information" : "Warning", "WEBSITE");
                    NotificationService.ShowBackupToast(
                        _last.IsOnline ? "Website Recovered" : "Website Alert",
                        $"pinaypal.net {state}. {_last.Summary}",
                        _last.IsOnline ? "Success" : "Warning");
                }

                return _last.Clone();
            }
            finally
            {
                Gate.Release();
            }
        }
    }

    public sealed class WebsiteStatus
    {
        public const string WebsiteUrl = "https://pinaypal.net";
        public string Url { get; set; } = WebsiteUrl;
        public bool IsOnline { get; set; }
        public int? StatusCode { get; set; }
        public long ResponseTimeMs { get; set; }
        public string? Error { get; set; }
        public DateTime CheckedAt { get; set; } = DateTime.MinValue;
        public DateTime ChangedAt { get; set; } = DateTime.MinValue;
        public int ConsecutiveFailures { get; set; }
        public string Summary => IsOnline
            ? $"HTTP {StatusCode} in {ResponseTimeMs} ms"
            : Error ?? "Connection failed.";

        public WebsiteStatus Clone() => (WebsiteStatus)MemberwiseClone();
        public static WebsiteStatus Unknown() => new() { Error = "Not checked yet." };
    }
}
