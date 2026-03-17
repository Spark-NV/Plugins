using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoCollections
{
    /// <summary>
    /// Thrown when both TMDB Read Access Token and API Key (v3) returned 401 Unauthorized.
    /// </summary>
    public class TmdbBothCredentialsUnauthorizedException : Exception
    {
        public TmdbBothCredentialsUnauthorizedException(string message) : base(message) { }
    }

    public class TmdbApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly string? _readAccessToken;
        private readonly string? _apiKey;
        private const string BaseUrl = "https://api.themoviedb.org/3";

        /// <summary>True = use Bearer (Read Access Token); false = use api_key query param.</summary>
        private bool _useReadToken;
        private bool _hasSwitchedDueTo401;

        private const int DelayBetweenRequestsMs = 750;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly object _delayLock = new object();

        /// <summary>
        /// Create a TMDB API client. When both credentials are set, tries Read Access Token first; on 401, switches to API Key for the rest of the run.
        /// </summary>
        public TmdbApiClient(string? readAccessToken, string? apiKey, ILogger logger)
        {
            _readAccessToken = string.IsNullOrWhiteSpace(readAccessToken) ? null : readAccessToken.Trim();
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            // When both are set, prefer Read Access Token first; on 401 we switch to API Key for the run.
            _useReadToken = !string.IsNullOrEmpty(_readAccessToken);
            _hasSwitchedDueTo401 = false;

            if (!string.IsNullOrEmpty(_readAccessToken) && !string.IsNullOrEmpty(_apiKey))
                _logger.LogInformation("[TMDB API] Both credentials set; using Read Access Token first, will switch to API Key on 401 if needed.");
            else if (!string.IsNullOrEmpty(_readAccessToken))
                _logger.LogInformation("[TMDB API] Using Read Access Token (Bearer)");
            else if (!string.IsNullOrEmpty(_apiKey))
                _logger.LogInformation("[TMDB API] Using API Key (v3)");

            _logger.LogInformation("[TMDB API] Rate limiting: {DelayMs}ms delay between requests (ensures <40 requests per 10 seconds)",
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
                    _logger.LogDebug("[TMDB API] Waiting {WaitMs}ms before next request to respect rate limits",
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
                await Task.Delay(waitTime);
        }

        private string BuildUrl(string pathAndQuery, bool useApiKeyInUrl)
        {
            if (!useApiKeyInUrl || string.IsNullOrEmpty(_apiKey))
                return pathAndQuery;
            var separator = pathAndQuery.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return pathAndQuery + separator + "api_key=" + Uri.EscapeDataString(_apiKey);
        }

        /// <summary>
        /// Sends a GET request with current auth. On 401, retries once with the other credential if available; if that also returns 401, throws <see cref="TmdbBothCredentialsUnauthorizedException"/>.
        /// </summary>
        private async Task<HttpResponseMessage> SendRequestAsync(string pathAndQuery)
        {
            await WaitForRateLimitAsync();

            var url = BuildUrl(pathAndQuery, useApiKeyInUrl: !_useReadToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (_useReadToken && !string.IsNullOrEmpty(_readAccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _readAccessToken);

            var response = await _httpClient.SendAsync(request);
            _logger.LogInformation("[TMDB API] Response Status: {StatusCode} {ReasonPhrase}", (int)response.StatusCode, response.ReasonPhrase);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[TMDB API] 401 Unauthorized with current credential. Response: {ErrorContent}", errorContent);

                bool canSwitch = !_hasSwitchedDueTo401 &&
                    ((_useReadToken && !string.IsNullOrEmpty(_apiKey)) || (!_useReadToken && !string.IsNullOrEmpty(_readAccessToken)));

                if (canSwitch)
                {
                    _hasSwitchedDueTo401 = true;
                    _useReadToken = !_useReadToken;
                    _logger.LogInformation("[TMDB API] Switching to {Auth} for remainder of run.", _useReadToken ? "Read Access Token (Bearer)" : "API Key (v3)");

                    var retryUrl = BuildUrl(pathAndQuery, useApiKeyInUrl: !_useReadToken);
                    using var retryRequest = new HttpRequestMessage(HttpMethod.Get, retryUrl);
                    if (_useReadToken && !string.IsNullOrEmpty(_readAccessToken))
                        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _readAccessToken);

                    var retryResponse = await _httpClient.SendAsync(retryRequest);
                    _logger.LogInformation("[TMDB API] Retry Response Status: {StatusCode} {ReasonPhrase}", (int)retryResponse.StatusCode, retryResponse.ReasonPhrase);

                    if (retryResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _logger.LogError("[TMDB API] Both credentials returned 401 Unauthorized. Stopping TMDB collection run.");
                        throw new TmdbBothCredentialsUnauthorizedException("Both TMDB Read Access Token and API Key (v3) are unauthorized. Please check your credentials in the plugin settings.");
                    }
                    return retryResponse;
                }

                _logger.LogError("[TMDB API] Both credentials returned 401 Unauthorized (or only one credential was set). Stopping TMDB collection run.");
                throw new TmdbBothCredentialsUnauthorizedException("TMDB credential is unauthorized. Please check your Read Access Token or API Key (v3) in the plugin settings.");
            }

            if (response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                _logger.LogWarning("[TMDB API] 429 Too Many Requests. Waiting 10 seconds before retrying same request...");
                await Task.Delay(TimeSpan.FromSeconds(10));
                return await SendRequestAsync(pathAndQuery);
            }

            return response;
        }

        public async Task<TmdbMovieDetails?> GetMovieDetailsAsync(int movieId, string language = "en-US")
        {
            try
            {
                var pathAndQuery = $"{BaseUrl}/movie/{movieId}?language={language}";
                _logger.LogInformation("[TMDB API] GET Request: movie/{MovieId}", movieId);

                var response = await SendRequestAsync(pathAndQuery);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("[TMDB API] Response Body Length: {Length} characters", json.Length);
                _logger.LogDebug("[TMDB API] Response Body: {Json}", json);

                var movieDetails = JsonSerializer.Deserialize<TmdbMovieDetails>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (movieDetails != null)
                {
                    _logger.LogInformation("[TMDB API] Successfully parsed movie: {Title} (ID: {Id})", movieDetails.Title, movieDetails.Id);
                    if (movieDetails.BelongsToCollection != null)
                        _logger.LogInformation("[TMDB API] Movie belongs to collection: {CollectionName} (ID: {CollectionId})",
                            movieDetails.BelongsToCollection.Name, movieDetails.BelongsToCollection.Id);
                    else
                        _logger.LogInformation("[TMDB API] Movie does not belong to any collection (BelongsToCollection is null)");
                }
                else
                    _logger.LogWarning("[TMDB API] Failed to deserialize movie details from response");

                return movieDetails;
            }
            catch (TmdbBothCredentialsUnauthorizedException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[TMDB API] HTTP error fetching movie details for TMDB ID {MovieId}: {Message}", movieId, ex.Message);
                throw;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "[TMDB API] Request timed out for TMDB ID {MovieId}: {Message}", movieId, ex.Message);
                throw;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError(ex, "[TMDB API] Request cancelled for TMDB ID {MovieId}: {Message}", movieId, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TMDB API] Unexpected error fetching movie details for TMDB ID {MovieId}: {Message}", movieId, ex.Message);
                throw;
            }
        }

        public async Task<TmdbCollectionDetails?> GetCollectionDetailsAsync(int collectionId, string language = "en-US")
        {
            try
            {
                var pathAndQuery = $"{BaseUrl}/collection/{collectionId}?language={language}";
                _logger.LogInformation("[TMDB API] GET Request: collection/{CollectionId}", collectionId);

                var response = await SendRequestAsync(pathAndQuery);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("[TMDB API] Response Body Length: {Length} characters", json.Length);
                _logger.LogDebug("[TMDB API] Response Body: {Json}", json);

                var collectionDetails = JsonSerializer.Deserialize<TmdbCollectionDetails>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (collectionDetails != null)
                {
                    _logger.LogInformation("[TMDB API] Successfully parsed collection: {Name} (ID: {Id})", collectionDetails.Name, collectionDetails.Id);
                    if (collectionDetails.Parts != null)
                        _logger.LogInformation("[TMDB API] Collection contains {Count} movies", collectionDetails.Parts.Count);
                    else
                        _logger.LogWarning("[TMDB API] Collection has no parts/movies");
                }
                else
                    _logger.LogWarning("[TMDB API] Failed to deserialize collection details from response");

                return collectionDetails;
            }
            catch (TmdbBothCredentialsUnauthorizedException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[TMDB API] HTTP error fetching collection details for TMDB ID {CollectionId}: {Message}", collectionId, ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TMDB API] Unexpected error fetching collection details for TMDB ID {CollectionId}: {Message}", collectionId, ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class TmdbMovieDetails
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        
        [JsonPropertyName("belongs_to_collection")]
        public TmdbCollectionInfo? BelongsToCollection { get; set; }
    }

    public class TmdbCollectionInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }
        
        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; set; }
    }

    public class TmdbCollectionDetails
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("overview")]
        public string? Overview { get; set; }
        
        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }
        
        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; set; }
        
        [JsonPropertyName("parts")]
        public List<TmdbCollectionPart>? Parts { get; set; }
    }

    public class TmdbCollectionPart
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        
        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }
        
        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }
    }
}

