using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    public class MailchimpService
    {
        private readonly HttpClient _client;
        private readonly string _apiKey;
        private readonly string _audienceId;
        private readonly string _dataCenter;

        public MailchimpService(string apiKey, string audienceId)
        {
            _apiKey = apiKey;
            _audienceId = audienceId;
            _dataCenter = apiKey.Split('-')[1];

            _client = new HttpClient();
            var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"anystring:{_apiKey}"));
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        }

        public async Task<string> RunSpecificTaskAsync(string taskType, string folderPath)
        {
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
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                
                // For simplicity, we'll save the raw JSON or a placeholder CSV.
                // In a real app, you'd use a JSON parser like Newtonsoft.Json or System.Text.Json.
                string filePath = Path.Combine(folderPath, $"{taskType}_{timestamp}.json");
                File.WriteAllText(filePath, content);
                
                return $"SUCCESS: {taskType} data exported to {filePath}";
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }
    }
}
