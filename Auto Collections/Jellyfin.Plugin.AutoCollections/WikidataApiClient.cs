#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoCollections
{
    /// <summary>
    /// Client for querying the Wikidata SPARQL API to find movie franchise/sequel collections.
    /// </summary>
    public class WikidataApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private const string SparqlEndpoint = "https://query.wikidata.org/sparql";
        private const string UserAgent = "Jellyfin.Plugin.AutoCollections/1.0 (https://github.com/jellyfin/jellyfin)";

        private const int DelayBetweenRequestsMs = 500;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly object _delayLock = new object();

        public WikidataApiClient(ILogger logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/sparql-results+json");

            _logger.LogInformation("[Wikidata API] Rate limiting: {DelayMs}ms delay between requests (respects Wikidata guidelines)",
                DelayBetweenRequestsMs);
        }

        private async Task WaitForRateLimitAsync()
        {
            TimeSpan waitTime;
            lock (_delayLock)
            {
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                waitTime = TimeSpan.FromMilliseconds(DelayBetweenRequestsMs) - timeSinceLastRequest;

                if (waitTime.TotalMilliseconds > 0)
                {
                    _logger.LogDebug("[Wikidata API] Waiting {WaitMs}ms before next request",
                        (int)waitTime.TotalMilliseconds);
                    _lastRequestTime = DateTime.UtcNow + waitTime;
                }
                else
                {
                    _lastRequestTime = DateTime.UtcNow;
                    waitTime = TimeSpan.Zero;
                }
            }

            if (waitTime.TotalMilliseconds > 0)
            {
                await Task.Delay(waitTime);
            }
        }

        /// <summary>
        /// Resolves a movie's IMDb ID to all its Wikidata series (P179 entries) and returns each series QID, label, and member IMDb IDs.
        /// A movie can belong to multiple series (e.g. Iron Man: MCU, Iron Man trilogy, Phase One, Infinity Saga).
        /// Uses P345 (IMDb ID), P179 (part of the series), P527 (has parts), and P1445 (fictional universe) to find franchise members.
        /// </summary>
        /// <param name="imdbId">IMDb ID (e.g. "tt1234567" or "tt0123456").</param>
        /// <returns>List of series results (one per P179 entry); empty if none found.</returns>
        public async Task<IReadOnlyList<WikidataSeriesResult>> GetSeriesByImdbIdAsync(string imdbId)
        {
            if (string.IsNullOrWhiteSpace(imdbId))
            {
                _logger.LogWarning("[Wikidata API] Empty IMDb ID provided");
                return Array.Empty<WikidataSeriesResult>();
            }

            var normalizedImdbId = imdbId.Trim();
            if (!normalizedImdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
            {
                normalizedImdbId = "tt" + normalizedImdbId;
            }

            try
            {
                await WaitForRateLimitAsync();

                // SPARQL: P179 (part of the series) on movie -> find its collection.
                // p:P179/ps:P179 gets ALL P179 values regardless of rank (wdt:P179 only returns preferred).
                // Branch 1: P527 (has parts) on series -> members. Type filter: feature film only.
                // Branch 2: P1445 (fictional universe) on series -> members. Type filter: feature film only.
                // Branch 3: fallback - siblings that share P179. No type filter (trust P179 relationship).
                // Q11424 = feature film - filters out TV, shorts, games on branches 1 & 2.
                var query = $@"
SELECT DISTINCT ?series ?seriesLabel ?imdbId WHERE {{
  ?movie wdt:P345 ""{EscapeSparqlString(normalizedImdbId)}"" .
  ?movie p:P179/ps:P179 ?series .
  ?series rdfs:label ?seriesLabel .
  FILTER(LANG(?seriesLabel) = ""en"")
  {{
    {{ ?series wdt:P527 ?member . ?member wdt:P31/wdt:P279* wd:Q11424 . }}
    UNION
    {{ ?series wdt:P1445 ?member . ?member wdt:P31/wdt:P279* wd:Q11424 . }}
    UNION
    {{ ?member p:P179/ps:P179 ?series . }}
  }}
  ?member wdt:P345 ?imdbId .
  FILTER(STRSTARTS(?imdbId, ""tt""))
}}
ORDER BY ?series ?imdbId
LIMIT 1000";

                var url = SparqlEndpoint + "?query=" + Uri.EscapeDataString(query) + "&format=json";
                _logger.LogInformation("[Wikidata API] GET Request: resolving IMDb {ImdbId} to series", normalizedImdbId);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[Wikidata API] Request failed: {StatusCode} {ReasonPhrase}",
                        (int)response.StatusCode, response.ReasonPhrase);
                    throw new HttpRequestException($"[Wikidata API] Request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("[Wikidata API] Response length: {Length} characters", json.Length);

                var results = ParseSparqlJsonResponse(json, normalizedImdbId);
                if (results.Count > 0)
                {
                    _logger.LogInformation("[Wikidata API] Resolved IMDb {ImdbId} to {SeriesCount} series: {SeriesNames}",
                        normalizedImdbId, results.Count, string.Join(", ", results.Select(r => r.CollectionName)));
                    foreach (var r in results)
                    {
                        if (r.ImdbIds.Count >= 1000)
                        {
                            _logger.LogWarning("[Wikidata API] Series {SeriesQid} hit 1000-item limit; some franchise members may be omitted", r.SeriesQid);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("[Wikidata API] No series found for IMDb {ImdbId}", normalizedImdbId);
                }

                return results;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[Wikidata API] HTTP error for IMDb {ImdbId}: {Message}", normalizedImdbId, ex.Message);
                throw;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "[Wikidata API] Request timed out for IMDb {ImdbId}: {Message}", normalizedImdbId, ex.Message);
                throw;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError(ex, "[Wikidata API] Request cancelled for IMDb {ImdbId}: {Message}", normalizedImdbId, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Wikidata API] Unexpected error for IMDb {ImdbId}: {Message}", normalizedImdbId, ex.Message);
                throw;
            }
        }

        private static string EscapeSparqlString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private IReadOnlyList<WikidataSeriesResult> ParseSparqlJsonResponse(string json, string triggerImdbId)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("results", out var resultsElement) ||
                    !resultsElement.TryGetProperty("bindings", out var bindingsElement))
                {
                    _logger.LogWarning("[Wikidata API] Unexpected JSON structure: missing results.bindings");
                    return Array.Empty<WikidataSeriesResult>();
                }

                // Group bindings by series QID: each P179 entry is a distinct collection
                var bySeries = new Dictionary<string, (string Label, HashSet<string> ImdbIds)>(StringComparer.OrdinalIgnoreCase);

                foreach (var binding in bindingsElement.EnumerateArray())
                {
                    string? seriesQid = null;
                    string? seriesLabel = null;
                    string? imdbId = null;

                    if (binding.TryGetProperty("series", out var seriesVar))
                    {
                        var uri = seriesVar.GetProperty("value").GetString();
                        if (!string.IsNullOrEmpty(uri))
                        {
                            seriesQid = ExtractQidFromUri(uri);
                        }
                    }

                    if (binding.TryGetProperty("seriesLabel", out var labelVar))
                    {
                        seriesLabel = labelVar.GetProperty("value").GetString();
                    }

                    if (binding.TryGetProperty("imdbId", out var imdbVar))
                    {
                        var id = imdbVar.GetProperty("value").GetString();
                        if (!string.IsNullOrEmpty(id))
                        {
                            var trimmed = id.Trim();
                            if (trimmed.Length >= 9 && trimmed.StartsWith("tt", StringComparison.OrdinalIgnoreCase) &&
                                trimmed.Substring(2).All(char.IsDigit))
                            {
                                imdbId = trimmed;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(seriesQid) || string.IsNullOrEmpty(seriesLabel) || string.IsNullOrEmpty(imdbId))
                    {
                        continue;
                    }

                    if (!bySeries.TryGetValue(seriesQid, out var entry))
                    {
                        entry = (seriesLabel, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                        bySeries[seriesQid] = entry;
                    }
                    entry.ImdbIds.Add(imdbId);
                }

                var results = new List<WikidataSeriesResult>();
                foreach (var kvp in bySeries)
                {
                    var (label, imdbIds) = kvp.Value;
                    if (imdbIds.Count > 0)
                    {
                        results.Add(new WikidataSeriesResult
                        {
                            SeriesQid = kvp.Key,
                            CollectionName = label,
                            ImdbIds = imdbIds.ToList()
                        });
                    }
                }

                return results;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "[Wikidata API] Failed to parse SPARQL JSON response: {Message}", ex.Message);
                throw;
            }
        }

        private static string ExtractQidFromUri(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return string.Empty;
            }

            var lastSlash = uri.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < uri.Length - 1)
            {
                return uri[(lastSlash + 1)..];
            }

            return uri;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class WikidataSeriesResult
    {
        public string SeriesQid { get; set; } = string.Empty;
        public string CollectionName { get; set; } = string.Empty;
        public List<string> ImdbIds { get; set; } = new();
    }
}
