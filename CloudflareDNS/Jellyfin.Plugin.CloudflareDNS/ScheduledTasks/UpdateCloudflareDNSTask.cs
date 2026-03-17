using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.CloudflareDNS
{
    public class UpdateCloudflareDNSTask : IScheduledTask
    {
        private readonly HttpClient _httpClient;

        public UpdateCloudflareDNSTask()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public string Key => "CloudflareDNSScheduledTask";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[] {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(6).Ticks
                }
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            string hostname = Plugin.Instance.Configuration.Hostname;
            string apiToken = Plugin.Instance.Configuration.ApiToken;
            bool active = Plugin.Instance.Configuration.Active;

            if (!active || string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(apiToken))
            {
                return;
            }

            try
            {
                string currentIp = await GetPublicIpAddressAsync();
                if (string.IsNullOrWhiteSpace(currentIp))
                {
                    return;
                }

                string domain = ExtractDomain(hostname);
                if (string.IsNullOrWhiteSpace(domain))
                {
                    return;
                }

                string zoneId = await GetZoneIdAsync(domain, apiToken);
                if (string.IsNullOrWhiteSpace(zoneId))
                {
                    return;
                }

                var dnsRecord = await GetDnsRecordAsync(zoneId, hostname, apiToken);
                if (dnsRecord == null)
                {
                    return;
                }

                if (dnsRecord.content == currentIp)
                {
                    return;
                }

                await UpdateDnsRecordAsync(zoneId, dnsRecord.id, hostname, currentIp, apiToken);
            }
            catch
            {
            }
        }

        private async Task<string> GetPublicIpAddressAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://api.ipify.org");
                return response.Trim();
            }
            catch
            {
                return null;
            }
        }

        private string ExtractDomain(string hostname)
        {
            var parts = hostname.Split('.');
            if (parts.Length >= 2)
            {
                return string.Join(".", parts[^2], parts[^1]);
            }
            return hostname;
        }

        private async Task<string> GetZoneIdAsync(string domain, string apiToken)
        {
            try
            {
                var url = $"https://api.cloudflare.com/client/v4/zones?name={domain}";
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                using (JsonDocument doc = JsonDocument.Parse(content))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("success", out var success) && success.GetBoolean())
                    {
                        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
                        {
                            return result[0].GetProperty("id").GetString();
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<DnsRecord> GetDnsRecordAsync(string zoneId, string hostname, string apiToken)
        {
            try
            {
                var url = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records?name={hostname}&type=A";
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                using (JsonDocument doc = JsonDocument.Parse(content))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("success", out var success) && success.GetBoolean())
                    {
                        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
                        {
                            var record = result[0];
                            return new DnsRecord
                            {
                                id = record.GetProperty("id").GetString(),
                                content = record.GetProperty("content").GetString()
                            };
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> UpdateDnsRecordAsync(string zoneId, string recordId, string hostname, string ipAddress, string apiToken)
        {
            try
            {
                var updateData = new
                {
                    type = "A",
                    name = hostname,
                    content = ipAddress,
                    ttl = 1
                };

                var json = JsonSerializer.Serialize(updateData);
                var url = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records/{recordId}";
                
                var request = new HttpRequestMessage(HttpMethod.Patch, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                using (JsonDocument doc = JsonDocument.Parse(content))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("success", out var success))
                    {
                        return success.GetBoolean();
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private class DnsRecord
        {
            public string id { get; set; }
            public string content { get; set; }
        }

        public string Name => "Update Cloudflare DNS Record";
        public string Category => "Cloudflare DNS";
        public string Description => "Updates Cloudflare DNS A record with current public IP address";
    }
}
