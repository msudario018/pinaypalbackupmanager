using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public class MailchimpService : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _apiKey;
        private readonly string _audienceId;
        private readonly string _dataCenter;
        private bool _disposed;

        public MailchimpService(string apiKey, string audienceId)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be null or empty", nameof(apiKey));
            
            if (string.IsNullOrWhiteSpace(audienceId))
                throw new ArgumentException("Audience ID cannot be null or empty", nameof(audienceId));

            // Validate API key format and extract datacenter
            var keyParts = apiKey.Split('-');
            if (keyParts.Length < 2 || string.IsNullOrWhiteSpace(keyParts[1]))
                throw new ArgumentException("Invalid API key format. Expected format: 'key-datacenter' (e.g., 'abc123-us1')", nameof(apiKey));

            _apiKey = apiKey;
            _audienceId = audienceId;
            _dataCenter = keyParts[1];

            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromMinutes(5);
            var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"anystring:{_apiKey}"));
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            _client.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public async Task<string> RunSpecificTaskAsync(string taskType, string folderPath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MailchimpService));

            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Folder path cannot be null or empty", nameof(folderPath));

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string url = "";

            switch (taskType)
            {
                case "Members":
                    url = $"https://{_dataCenter}.api.mailchimp.com/3.0/lists/{_audienceId}/members?count=1000";
                    break;
                case "Campaigns":
                    url = $"https://{_dataCenter}.api.mailchimp.com/3.0/campaigns?count=1000";
                    break;
                case "Reports":
                    url = $"https://{_dataCenter}.api.mailchimp.com/3.0/reports?count=1000";
                    break;
                case "Merge_Fields":
                    url = $"https://{_dataCenter}.api.mailchimp.com/3.0/lists/{_audienceId}/merge-fields";
                    break;
                case "Tags":
                    url = $"https://{_dataCenter}.api.mailchimp.com/3.0/lists/{_audienceId}/tag-search?count=1000";
                    break;
            }

            if (string.IsNullOrEmpty(url)) return "Invalid Task Type";

            try
            {
                // Ensure directory exists
                Directory.CreateDirectory(folderPath);

                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                
                string filePath = Path.Combine(folderPath, $"{taskType}_{timestamp}.json");
                await File.WriteAllTextAsync(filePath, content);
                
                return $"SUCCESS: {taskType} data exported to {filePath}";
            }
            catch (HttpRequestException ex)
            {
                return $"ERROR: HTTP request failed - {ex.Message}";
            }
            catch (IOException ex)
            {
                return $"ERROR: File write failed - {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _client?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
