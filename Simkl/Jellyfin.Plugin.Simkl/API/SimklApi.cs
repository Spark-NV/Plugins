using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.API
{
    public class SimklApi
    {
        private readonly ILogger<SimklApi> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private readonly JsonSerializerOptions _caseInsensitiveJsonSerializerOptions;

        public const string Baseurl = @"https://api.simkl.com";

        public const string RedirectUri = @"https://simkl.com/apps/jellyfin/connected/";

        public const string Apikey = @"c721b22482097722a84a20ccc579cf9d232be85b9befe7b7805484d0ddbc6781";

        public const string Secret = @"87893fc73cdbd2e51a7c63975c6f941ac1c6155c0e20ffa76b83202dd10a507e";

        public SimklApi(ILogger<SimklApi> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _jsonSerializerOptions = JsonDefaults.Options;
            _caseInsensitiveJsonSerializerOptions = new JsonSerializerOptions(_jsonSerializerOptions)
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<CodeResponse?> GetCode()
        {
            var uri = $"/oauth/pin?client_id={Apikey}&redirect={RedirectUri}";
            return await Get<CodeResponse>(uri);
        }

        public async Task<CodeStatusResponse?> GetCodeStatus(string userCode)
        {
            var uri = $"/oauth/pin/{userCode}?client_id={Apikey}";
            return await Get<CodeStatusResponse>(uri);
        }

        public async Task<UserSettings?> GetUserSettings(string userToken)
        {
            try
            {
                return await Post<UserSettings, object>("/users/settings/", userToken);
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new UserSettings { Error = "user_token_failed" };
            }
        }

        public async Task<PlanToWatchResponse?> GetListByStatus(string userToken, string status = "plantowatch")
        {
            try
            {
                var validStatuses = new[] { "plantowatch", "watching", "completed", "hold", "dropped" };
                if (!validStatuses.Contains(status.ToLowerInvariant()))
                {
                    _logger.LogWarning("Invalid status {Status}, defaulting to plantowatch", status);
                    status = "plantowatch";
                }

                var statusLower = status.ToLowerInvariant();
                var result = new PlanToWatchResponse { Movies = new List<SimklMovie>(), Shows = new List<SimklShow>() };

                var movieUri = $"/sync/all-items/movie/{statusLower}?extended=full";
                _logger.LogDebug("Requesting Simkl API for movies: {Uri}", movieUri);
                var movieListResponse = await Get<MovieListResponse>(movieUri, userToken);
                if (movieListResponse != null && movieListResponse.Movies != null)
                {
                    result.Movies = movieListResponse.Movies;
                    _logger.LogDebug("Retrieved {Count} movies from Simkl", movieListResponse.Movies.Count);
                }
                else
                {
                    _logger.LogDebug("No movies returned from Simkl API");
                }

                var tvUri = $"/sync/all-items/tv/{statusLower}?extended=full";
                _logger.LogDebug("Requesting Simkl API for TV shows: {Uri}", tvUri);
                var showListResponse = await Get<ShowListResponse>(tvUri, userToken);
                if (showListResponse != null && showListResponse.Shows != null)
                {
                    result.Shows = showListResponse.Shows;
                    _logger.LogDebug("Retrieved {Count} TV shows from Simkl", showListResponse.Shows.Count);
                }
                else
                {
                    _logger.LogDebug("No TV shows returned from Simkl API");
                }

                _logger.LogDebug("Simkl API response: {MovieCount} movies, {ShowCount} shows", result.Movies?.Count ?? 0, result.Shows?.Count ?? 0);
                return result;
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError(e, "Invalid user token {UserToken}", userToken);
                SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token " + userToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error retrieving list from Simkl API for status: {Status}", status);
                throw;
            }
        }

        [Obsolete("Use GetListByStatus instead")]
        public async Task<PlanToWatchResponse?> GetPlanToWatch(string userToken)
        {
            return await GetListByStatus(userToken, "plantowatch");
        }

        public async Task<(bool Success, BaseItemDto Item)> MarkAsWatched(BaseItemDto item, string userToken)
        {
            var history = CreateHistoryFromItem(item);
            var r = await SyncHistoryAsync(history, userToken);
            _logger.LogDebug("BaseItem: {@Item}", item);
            _logger.LogDebug("History: {@History}", history);
            _logger.LogDebug("Response: {@Response}", r);
            if (r != null && history.Movies.Count == r.Added.Movies
                && history.Shows.Count == r.Added.Shows
                && history.Episodes.Count == r.Added.Episodes)
            {
                return (true, item);
            }

            try
            {
                (history, item) = await GetHistoryFromFileName(item);
            }
            catch (InvalidDataException)
            {
                _logger.LogDebug("Couldn't scrobble using full path, trying using only filename");
                (history, item) = await GetHistoryFromFileName(item, false);
            }

            r = await SyncHistoryAsync(history, userToken);
            return r == null
                ? (false, item)
                : (history.Movies.Count == r.Added.Movies && history.Shows.Count == r.Added.Shows, item);
        }

        private async Task<SearchFileResponse?> GetFromFile(string filename)
        {
            var f = new SimklFile { File = filename };
            _logger.LogInformation("Posting: {@File}", f);
            return await Post<SearchFileResponse, SimklFile>("/search/file/", null, f);
        }

        private async Task<(SimklHistory history, BaseItemDto item)> GetHistoryFromFileName(BaseItemDto item, bool fullpath = true)
        {
            var fname = fullpath ? item.Path : Path.GetFileName(item.Path);
            var mo = await GetFromFile(fname);
            if (mo == null)
            {
                throw new InvalidDataException("Search file response is null");
            }

            var history = new SimklHistory();
            if (mo.Movie != null &&
                (item.IsMovie == true || item.Type == BaseItemKind.Movie))
            {
                if (!string.Equals(mo.Type, "movie", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("type != movie (" + mo.Type + ")");
                }

                item.Name = mo.Movie.Title;
                item.ProductionYear = mo.Movie.Year;
                history.Movies.Add(mo.Movie);
            }
            else if (mo.Episode != null
                     && mo.Show != null
                     && (item.IsSeries == true || item.Type == BaseItemKind.Episode))
            {
                if (!string.Equals(mo.Type, "episode", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("type != episode (" + mo.Type + ")");
                }

                item.Name = mo.Episode.Title;
                item.SeriesName = mo.Show.Title;
                item.IndexNumber = mo.Episode.Episode;
                item.ParentIndexNumber = mo.Episode.Season;
                item.ProductionYear = mo.Show.Year;
                history.Episodes.Add(mo.Episode);
            }

            return (history, item);
        }

        private static HttpRequestMessage GetOptions(string? userToken = null)
        {
            var requestMessage = new HttpRequestMessage();
            requestMessage.Headers.TryAddWithoutValidation("simkl-api-key", Apikey);
            if (!string.IsNullOrEmpty(userToken))
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            }

            return requestMessage;
        }

        private static SimklHistory CreateHistoryFromItem(BaseItemDto item)
        {
            var history = new SimklHistory();

            if (item.IsMovie == true || item.Type == BaseItemKind.Movie)
            {
                history.Movies.Add(new SimklMovie(item));
            }
            else if (item.IsSeries == true || (item.Type == BaseItemKind.Series))
            {
                history.Shows.Add(new SimklShow(item));
            }
            else if (item.Type == BaseItemKind.Episode)
            {
                history.Episodes.Add(new SimklEpisode(item));
            }

            return history;
        }

        private async Task<SyncHistoryResponse?> SyncHistoryAsync(SimklHistory history, string userToken)
        {
            try
            {
                _logger.LogInformation("Syncing History");
                return await Post<SyncHistoryResponse, SimklHistory>("/sync/history", userToken, history);
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError(e, "Invalid user token {UserToken}, deleting", userToken);
                SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token " + userToken);
            }
        }

        private async Task<T?> Get<T>(string url, string? userToken = null)
        {
            using var options = GetOptions(userToken);
            options.RequestUri = new Uri(Baseurl + url);
            options.Method = HttpMethod.Get;
            var responseMessage = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(options);

            if (!responseMessage.IsSuccessStatusCode)
            {
                _logger.LogError("API request failed with status {StatusCode}: {Url}", responseMessage.StatusCode, url);
                return default;
            }

            var content = await responseMessage.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("API response is empty for: {Url}", url);
                return default;
            }

            _logger.LogDebug("API response content (first 500 chars): {Content}", content.Length > 500 ? content.Substring(0, 500) : content);

            try
            {
                var result = await responseMessage.Content.ReadFromJsonAsync<T>(_jsonSerializerOptions);
                if (result == null)
                {
                    _logger.LogWarning("JSON deserialization returned null for: {Url}. Content: {Content}", url, content.Length > 500 ? content.Substring(0, 500) : content);
                }

                return result;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize JSON response for: {Url}. Content: {Content}", url, content.Length > 500 ? content.Substring(0, 500) : content);
                throw;
            }
        }

        private async Task<T1?> Post<T1, T2>(string url, string? userToken = null, T2? data = null)
         where T2 : class
        {
            using var options = GetOptions(userToken);
            options.RequestUri = new Uri(Baseurl + url);
            options.Method = HttpMethod.Post;

            if (data != null)
            {
                options.Content = new StringContent(
                    JsonSerializer.Serialize(data, _jsonSerializerOptions),
                    Encoding.UTF8,
                    MediaTypeNames.Application.Json);
            }

            var responseMessage = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(options);

            return await responseMessage.Content.ReadFromJsonAsync<T1>(_caseInsensitiveJsonSerializerOptions);
        }
    }
}