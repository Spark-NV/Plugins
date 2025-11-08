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
    public class TmdbApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly string _accessToken;
        private const string BaseUrl = "https://api.themoviedb.org/3";
        
        private const int DelayBetweenRequestsMs = 750;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly object _delayLock = new object();

        public TmdbApiClient(string accessToken, ILogger logger)
        {
            _accessToken = accessToken;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            _logger.LogInformation("[TMDB API] Using v4 access token authentication (Bearer token)");
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
            {
                await Task.Delay(waitTime);
            }
        }

        public async Task<TmdbMovieDetails?> GetMovieDetailsAsync(int movieId, string language = "en-US")
        {
            try
            {
                await WaitForRateLimitAsync();
                
                var url = $"{BaseUrl}/movie/{movieId}?language={language}";
                
                _logger.LogInformation("[TMDB API] GET Request URL: {Url}", url);
                _logger.LogInformation("[TMDB API] Access Token Length: {Length}", _accessToken?.Length ?? 0);
                _logger.LogInformation("[TMDB API] Fetching movie details for TMDB ID: {MovieId}", movieId);

                var response = await _httpClient.GetAsync(url);
                _logger.LogInformation("[TMDB API] Response Status: {StatusCode} {ReasonPhrase}", (int)response.StatusCode, response.ReasonPhrase);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[TMDB API] Error Response Body: {ErrorContent}", errorContent);
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _logger.LogError("[TMDB API] 401 Unauthorized - This usually means the access token is invalid or missing. Please check your TMDB access token in the plugin settings.");
                    }
                    else if (response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        _logger.LogError("[TMDB API] 429 Too Many Requests - Rate limit exceeded. This shouldn't happen with our rate limiter, but waiting 10 seconds and retrying...");
                        await Task.Delay(TimeSpan.FromSeconds(10));
                    }
                }
                
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("[TMDB API] Response Body Length: {Length} characters", json.Length);
                
                if (json.Contains("belongs_to_collection", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[TMDB API] Found 'belongs_to_collection' field in JSON response");
                    var collectionStart = json.IndexOf("\"belongs_to_collection\"", StringComparison.OrdinalIgnoreCase);
                    if (collectionStart >= 0)
                    {
                        var collectionEnd = json.IndexOf("}", collectionStart + 50);
                        if (collectionEnd > collectionStart)
                        {
                            var collectionJson = json.Substring(collectionStart, Math.Min(collectionEnd - collectionStart + 1, 200));
                            _logger.LogInformation("[TMDB API] Collection JSON snippet: {Snippet}", collectionJson);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("[TMDB API] 'belongs_to_collection' field NOT found in JSON response");
                }
                
                _logger.LogDebug("[TMDB API] Response Body: {Json}", json);

                var movieDetails = JsonSerializer.Deserialize<TmdbMovieDetails>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (movieDetails != null)
                {
                    _logger.LogInformation("[TMDB API] Successfully parsed movie: {Title} (ID: {Id})", movieDetails.Title, movieDetails.Id);
                    if (movieDetails.BelongsToCollection != null)
                    {
                        _logger.LogInformation("[TMDB API] Movie belongs to collection: {CollectionName} (ID: {CollectionId})", 
                            movieDetails.BelongsToCollection.Name, movieDetails.BelongsToCollection.Id);
                    }
                    else
                    {
                        _logger.LogInformation("[TMDB API] Movie does not belong to any collection (BelongsToCollection is null)");
                    }
                }
                else
                {
                    _logger.LogWarning("[TMDB API] Failed to deserialize movie details from response");
                }

                return movieDetails;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[TMDB API] HTTP error fetching movie details for TMDB ID {MovieId}: {Message}", movieId, ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TMDB API] Unexpected error fetching movie details for TMDB ID {MovieId}: {Message}", movieId, ex.Message);
                return null;
            }
        }

        public async Task<TmdbCollectionDetails?> GetCollectionDetailsAsync(int collectionId, string language = "en-US")
        {
            try
            {
                await WaitForRateLimitAsync();
                
                var url = $"{BaseUrl}/collection/{collectionId}?language={language}";
                
                _logger.LogInformation("[TMDB API] GET Request URL: {Url}", url);
                _logger.LogInformation("[TMDB API] Access Token Length: {Length}", _accessToken?.Length ?? 0);
                _logger.LogInformation("[TMDB API] Fetching collection details for TMDB Collection ID: {CollectionId}", collectionId);

                var response = await _httpClient.GetAsync(url);
                _logger.LogInformation("[TMDB API] Response Status: {StatusCode} {ReasonPhrase}", (int)response.StatusCode, response.ReasonPhrase);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[TMDB API] Error Response Body: {ErrorContent}", errorContent);
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _logger.LogError("[TMDB API] 401 Unauthorized - This usually means the access token is invalid or missing. Please check your TMDB access token in the plugin settings.");
                    }
                    else if (response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        _logger.LogError("[TMDB API] 429 Too Many Requests - Rate limit exceeded. This shouldn't happen with our rate limiter, but waiting 10 seconds and retrying...");
                        await Task.Delay(TimeSpan.FromSeconds(10));
                    }
                }
                
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
                    {
                        _logger.LogInformation("[TMDB API] Collection contains {Count} movies", collectionDetails.Parts.Count);
                        foreach (var part in collectionDetails.Parts)
                        {
                            _logger.LogInformation("[TMDB API]   - {Title} (ID: {Id}, Release: {ReleaseDate})", 
                                part.Title, part.Id, part.ReleaseDate ?? "Unknown");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[TMDB API] Collection has no parts/movies");
                    }
                }
                else
                {
                    _logger.LogWarning("[TMDB API] Failed to deserialize collection details from response");
                }

                return collectionDetails;
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

