using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Tvdb.Configuration;
using Jellyfin.Plugin.Tvdb.SeasonClient;
using MediaBrowser.Common;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Tvdb.Sdk;

using Action = Tvdb.Sdk.Action;
using Type = Tvdb.Sdk.Type;

namespace Jellyfin.Plugin.Tvdb;

public class TvdbClientManager : IDisposable
{
    private const string TvdbHttpClient = "TvdbHttpClient";
    private static readonly SemaphoreSlim _tokenUpdateLock = new SemaphoreSlim(1, 1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly MemoryCache _memoryCache;
    private readonly SdkClientSettings _sdkClientSettings;

    private DateTime _tokenUpdatedAt;

    public TvdbClientManager(IApplicationHost applicationHost, ILocalizationManager localizationManager)
    {
        _serviceProvider = ConfigureService(applicationHost);
        _httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        _sdkClientSettings = _serviceProvider.GetRequiredService<SdkClientSettings>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _tokenUpdatedAt = DateTime.MinValue;

        TvdbCultureInfo.SetCultures(localizationManager.GetCultures().ToArray());
        TvdbCultureInfo.SetCountries(localizationManager.GetCountries().ToArray());
    }

    private static string? UserPin => TvdbPlugin.Instance?.Configuration.SubscriberPIN;

    private static int CacheDurationInHours => TvdbPlugin.Instance?.Configuration.CacheDurationInHours ?? 1;

    private static int CacheDurationInDays => TvdbPlugin.Instance?.Configuration.CacheDurationInDays ?? 7;

    private async Task LoginAsync()
    {
        var loginClient = _serviceProvider.GetRequiredService<ILoginClient>();

        if (IsTokenInvalid())
        {
            await _tokenUpdateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsTokenInvalid())
                {
                    var loginResponse = await loginClient.LoginAsync(new Body
                    {
                        Apikey = PluginConfiguration.ProjectApiKey,
                        Pin = UserPin
                    }).ConfigureAwait(false);

                    _tokenUpdatedAt = DateTime.UtcNow;
                    _sdkClientSettings.AccessToken = loginResponse.Data.Token;
                }
            }
            finally
            {
                _tokenUpdateLock.Release();
            }
        }

        return;

        bool IsTokenInvalid() =>
            _tokenUpdatedAt == DateTime.MinValue
            || string.IsNullOrEmpty(_sdkClientSettings.AccessToken)
            || _tokenUpdatedAt < DateTime.UtcNow.Subtract(TimeSpan.FromDays(25));
    }

    public async Task<IReadOnlyList<SearchResult>> GetMovieByNameAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbMovieSearch_{name}";
        if (_memoryCache.TryGetValue(key, out IReadOnlyList<SearchResult>? movies)
                       && movies is not null)
        {
            return movies;
        }

        var searchClient = _serviceProvider.GetRequiredService<ISearchClient>();
        await LoginAsync().ConfigureAwait(false);
        var searchResult = await searchClient.GetSearchResultsAsync(query: name, type: "movie", limit: 5, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, searchResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return searchResult.Data;
    }

    public async Task<IReadOnlyList<SearchByRemoteIdResult>> GetMovieByRemoteIdAsync(
        string remoteId,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbMovieRemoteId_{remoteId}";
        if (_memoryCache.TryGetValue(key, out IReadOnlyList<SearchByRemoteIdResult>? movies)
                                  && movies is not null)
        {
            return movies;
        }

        var searchClient = _serviceProvider.GetRequiredService<ISearchClient>();
        await LoginAsync().ConfigureAwait(false);
        var searchResult = await searchClient.GetSearchResultsByRemoteIdAsync(remoteId: remoteId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var filteredMovies = searchResult.Data?.Where(x => x.Movie?.Id is not null).ToList();
        if (filteredMovies is not null)
        {
            _memoryCache.Set(key, filteredMovies, TimeSpan.FromHours(CacheDurationInHours));
            return filteredMovies;
        }

        _memoryCache.Set(key, Array.Empty<SearchByRemoteIdResult>(), TimeSpan.FromHours(CacheDurationInHours));
        return Array.Empty<SearchByRemoteIdResult>();
    }

    public async Task<MovieExtendedRecord> GetMovieExtendedByIdAsync(
        int tvdbId,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbMovie_{tvdbId.ToString(CultureInfo.InvariantCulture)}";
        if (_memoryCache.TryGetValue(key, out MovieExtendedRecord? movie)
                       && movie is not null)
        {
            return movie;
        }

        var movieClient = _serviceProvider.GetRequiredService<IMoviesClient>();
        await LoginAsync().ConfigureAwait(false);
        var movieResult = await movieClient.GetMovieExtendedAsync(id: tvdbId, meta: Meta2.Translations, @short: false,  cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, movieResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return movieResult.Data;
    }

    public async Task<IReadOnlyList<SearchResult>> GetSeriesByNameAsync(
        string name,
        string language,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbSeriesSearch_{name}";
        if (_memoryCache.TryGetValue(key, out IReadOnlyList<SearchResult>? series)
            && series is not null)
        {
            return series;
        }

        var searchClient = _serviceProvider.GetRequiredService<ISearchClient>();
        await LoginAsync().ConfigureAwait(false);
        var searchResult = await searchClient.GetSearchResultsAsync(query: name, type: "series", limit: 5, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, searchResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return searchResult.Data;
    }

    public async Task<SeriesBaseRecord> GetSeriesByIdAsync(
        int tvdbId,
        string language,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbSeries_{tvdbId.ToString(CultureInfo.InvariantCulture)}";
        if (_memoryCache.TryGetValue(key, out SeriesBaseRecord? series)
            && series is not null)
        {
            return series;
        }

        var seriesClient = _serviceProvider.GetRequiredService<ISeriesClient>();
        await LoginAsync().ConfigureAwait(false);
        var seriesResult = await seriesClient.GetSeriesBaseAsync(id: tvdbId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, seriesResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return seriesResult.Data;
    }

    public async Task<SeriesExtendedRecord> GetSeriesExtendedByIdAsync(
        int tvdbId,
        string language,
        CancellationToken cancellationToken,
        Meta4? meta = null,
        bool? small = null)
    {
        var key = $"TvdbSeriesExtended_{tvdbId.ToString(CultureInfo.InvariantCulture)}_{meta}_{small}";
        if (_memoryCache.TryGetValue(key, out SeriesExtendedRecord? series)
            && series is not null)
        {
            return series;
        }

        var seriesClient = _serviceProvider.GetRequiredService<ISeriesClient>();
        await LoginAsync().ConfigureAwait(false);
        var seriesResult = await seriesClient.GetSeriesExtendedAsync(id: tvdbId, meta: meta, @short: small, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, seriesResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return seriesResult.Data;
    }

    public async Task<Data2> GetSeriesEpisodesAsync(
        int tvdbId,
        string language,
        string seasonType,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbSeriesEpisodes_{tvdbId.ToString(CultureInfo.InvariantCulture)}_{seasonType}";
        if (_memoryCache.TryGetValue(key, out Data2? series)
            && series is not null)
        {
            return series;
        }

        var seriesClient = _serviceProvider.GetRequiredService<ISeriesClient>();
        await LoginAsync().ConfigureAwait(false);
        var seriesResult = await seriesClient.GetSeriesEpisodesAsync(id: tvdbId, season_type: seasonType, cancellationToken: cancellationToken, page: 0)
            .ConfigureAwait(false);
        _memoryCache.Set(key, seriesResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return seriesResult.Data;
    }

    public async Task<CustomSeasonExtendedRecord> GetSeasonByIdAsync(
        int seasonTvdbId,
        string language,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbSeason_{seasonTvdbId.ToString(CultureInfo.InvariantCulture)}";
        if (_memoryCache.TryGetValue(key, out CustomSeasonExtendedRecord? season)
            && season is not null)
        {
            return season;
        }

        var seasonClient = _serviceProvider.GetRequiredService<IExtendedSeasonClient>();
        await LoginAsync().ConfigureAwait(false);
        var seasonResult = await seasonClient.GetSeasonExtendedWithTranslationsAsync(id: seasonTvdbId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, seasonResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return seasonResult.Data;
    }

    public async Task<EpisodeExtendedRecord> GetEpisodesAsync(
        int episodeTvdbId,
        string language,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbEpisode_{episodeTvdbId.ToString(CultureInfo.InvariantCulture)}";
        if (_memoryCache.TryGetValue(key, out EpisodeExtendedRecord? episode)
            && episode is not null)
        {
            return episode;
        }

        var episodeClient = _serviceProvider.GetRequiredService<IEpisodesClient>();
        await LoginAsync().ConfigureAwait(false);
        var episodeResult = await episodeClient.GetEpisodeExtendedAsync(id: episodeTvdbId, meta: Meta.Translations, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, episodeResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return episodeResult.Data;
    }

    public async Task<IReadOnlyList<SearchByRemoteIdResult>> GetSeriesByRemoteIdAsync(
        string remoteId,
        string language,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbSeriesRemoteId_{remoteId}";
        if (_memoryCache.TryGetValue(key, out IReadOnlyList<SearchByRemoteIdResult>? series)
            && series is not null)
        {
            return series;
        }

        var searchClient = _serviceProvider.GetRequiredService<ISearchClient>();
        await LoginAsync().ConfigureAwait(false);
        var searchResult = await searchClient.GetSearchResultsByRemoteIdAsync(remoteId: remoteId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, searchResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return searchResult.Data;
    }

    public async Task<IReadOnlyList<SearchResult>> GetActorByNameAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbPeopleSearch_{name}";
        if (_memoryCache.TryGetValue(key, out IReadOnlyList<SearchResult>? people)
            && people is not null)
        {
            return people;
        }

        var searchClient = _serviceProvider.GetRequiredService<ISearchClient>();
        await LoginAsync().ConfigureAwait(false);
        var searchResult = await searchClient.GetSearchResultsAsync(query: name, type: "person", limit: 5, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, searchResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return searchResult.Data;
    }

    public async Task<IReadOnlyList<SearchByRemoteIdResult>> GetActorByRemoteIdAsync(
        string remoteId,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbPeopleRemoteId_{remoteId}";
        if (_memoryCache.TryGetValue(key, out IReadOnlyList<SearchByRemoteIdResult>? people)
            && people is not null)
        {
            return people;
        }

        var searchClient = _serviceProvider.GetRequiredService<ISearchClient>();
        await LoginAsync().ConfigureAwait(false);
        var searchResult = await searchClient.GetSearchResultsByRemoteIdAsync(remoteId: remoteId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, searchResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return searchResult.Data;
    }

    public async Task<PeopleExtendedRecord> GetActorExtendedByIdAsync(
        int tvdbId,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbPeople_{tvdbId.ToString(CultureInfo.InvariantCulture)}";
        if (_memoryCache.TryGetValue(key, out PeopleExtendedRecord? people)
            && people is not null)
        {
            return people;
        }

        var peopleClient = _serviceProvider.GetRequiredService<IPeopleClient>();
        await LoginAsync().ConfigureAwait(false);
        var peopleResult = await peopleClient.GetPeopleExtendedAsync(id: tvdbId, meta: Meta3.Translations, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, peopleResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return peopleResult.Data;
    }

    public async Task<ArtworkExtendedRecord> GetImageAsync(
        int imageTvdbId,
        string language,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbArtwork_{imageTvdbId.ToString(CultureInfo.InvariantCulture)}";
        if (_memoryCache.TryGetValue(key, out ArtworkExtendedRecord? artwork)
            && artwork is not null)
        {
            return artwork;
        }

        var artworkClient = _serviceProvider.GetRequiredService<IArtworkClient>();
        await LoginAsync().ConfigureAwait(false);
        var artworkResult = await artworkClient.GetArtworkExtendedAsync(id: imageTvdbId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, artworkResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return artworkResult.Data;
    }

    public async Task<SeriesExtendedRecord> GetSeriesImagesAsync(
        int tvdbId,
        string language,
        CancellationToken cancellationToken)
    {
        var key = $"TvdbSeriesArtwork_{tvdbId.ToString(CultureInfo.InvariantCulture)}";
        if (_memoryCache.TryGetValue(key, out SeriesExtendedRecord? series)
            && series is not null)
        {
            return series;
        }

        var seriesClient = _serviceProvider.GetRequiredService<ISeriesClient>();
        await LoginAsync().ConfigureAwait(false);
        var seriesResult = await seriesClient.GetSeriesArtworksAsync(id: tvdbId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, seriesResult.Data, TimeSpan.FromHours(CacheDurationInHours));
        return seriesResult.Data;
    }

    public async Task<IReadOnlyList<Language>> GetLanguagesAsync(CancellationToken cancellationToken)
    {
        var key = "TvdbLanguages";
        if (_memoryCache.TryGetValue(key, out IReadOnlyList<Language>? languages)
            && languages is not null)
        {
            return languages;
        }

        var languagesClient = _serviceProvider.GetRequiredService<ILanguagesClient>();
        await LoginAsync().ConfigureAwait(false);
        var languagesResult = await languagesClient.GetAllLanguagesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, languagesResult.Data, TimeSpan.FromDays(CacheDurationInDays));
        return languagesResult.Data;
    }

    public async Task<IReadOnlyList<ArtworkType>> GetArtworkTypeAsync(CancellationToken cancellationToken)
    {
        var key = "TvdbArtworkTypes";
        if (_memoryCache.TryGetValue(key, out IReadOnlyList<ArtworkType>? artworkTypes)
            && artworkTypes is not null)
        {
            return artworkTypes;
        }

        var artworkTypesClient = _serviceProvider.GetRequiredService<IArtwork_TypesClient>();
        await LoginAsync().ConfigureAwait(false);
        var artworkTypesResult = await artworkTypesClient.GetAllArtworkTypesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, artworkTypesResult.Data, TimeSpan.FromDays(CacheDurationInDays));
        return artworkTypesResult.Data;
    }

    public async Task<IReadOnlyList<Country>> GetCountriesAsync(CancellationToken cancellationToken)
    {
        var key = "TvdbCountries";
        if (_memoryCache.TryGetValue(key, out IReadOnlyList<Country>? countries)
                       && countries is not null)
        {
            return countries;
        }

        var countriesClient = _serviceProvider.GetRequiredService<ICountriesClient>();
        await LoginAsync().ConfigureAwait(false);
        var countriesResult = await countriesClient.GetAllCountriesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _memoryCache.Set(key, countriesResult.Data, TimeSpan.FromDays(CacheDurationInDays));
        return countriesResult.Data;
    }

    public async Task<string?> GetEpisodeTvdbId(
        EpisodeInfo searchInfo,
        string language,
        CancellationToken cancellationToken)
    {
        var seriesClient = _serviceProvider.GetRequiredService<ISeriesClient>();
        await LoginAsync().ConfigureAwait(false);
        if (!searchInfo.SeriesProviderIds.TryGetValue(TvdbPlugin.ProviderId, out var seriesTvdbIdString))
        {
            return null;
        }

        int seriesTvdbId = int.Parse(seriesTvdbIdString, CultureInfo.InvariantCulture);
        int? episodeNumber = null;
        int? seasonNumber = null;
        string? airDate = null;
        bool special = false;
        string? key = null;
        if (searchInfo.IndexNumber.HasValue)
        {
            switch (searchInfo.SeriesDisplayOrder)
            {
                case "regional":
                case "alternate":
                case "altdvd":
                case "dvd":
                case "alttwo":
                    episodeNumber = searchInfo.IndexNumber.Value;
                    seasonNumber = searchInfo.ParentIndexNumber ?? 1;
                    break;
                case "absolute":
                    if (searchInfo.ParentIndexNumber == 0) // check if special
                    {
                        special = true;
                        seasonNumber = 0;
                    }
                    else
                    {
                        seasonNumber = 1; // absolute order is always season 1
                    }

                    episodeNumber = searchInfo.IndexNumber.Value;
                    break;
                default:
                    episodeNumber = searchInfo.IndexNumber.Value;
                    seasonNumber = searchInfo.ParentIndexNumber ?? 1;
                    break;
            }

            key = $"FindTvdbEpisodeId_{seriesTvdbIdString}_{seasonNumber.Value.ToString(CultureInfo.InvariantCulture)}_{episodeNumber.Value.ToString(CultureInfo.InvariantCulture)}_{searchInfo.SeriesDisplayOrder}";
        }
        else if (searchInfo.PremiereDate.HasValue)
        {
            airDate = searchInfo.PremiereDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            key = $"FindTvdbEpisodeId_{seriesTvdbIdString}_{airDate}";
        }

        if (key != null && _memoryCache.TryGetValue(key, out string? episodeTvdbId))
        {
            return episodeTvdbId;
        }

        Response56 seriesResponse;
        if (!special)
        {
            switch (searchInfo.SeriesDisplayOrder)
            {
                case "regional":
                case "alternate":
                case "altdvd":
                case "dvd":
                case "absolute":
                case "alttwo":
                    seriesResponse = await seriesClient.GetSeriesEpisodesAsync(page: 0, id: seriesTvdbId, season_type: searchInfo.SeriesDisplayOrder, season: seasonNumber, episodeNumber: episodeNumber, airDate: airDate, cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    seriesResponse = await seriesClient.GetSeriesEpisodesAsync(page: 0, id: seriesTvdbId, season_type: "official", season: seasonNumber, episodeNumber: episodeNumber, airDate: airDate, cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        else
        {
            seriesResponse = await seriesClient.GetSeriesEpisodesAsync(page: 0, id: seriesTvdbId, season_type: "official", season: seasonNumber, episodeNumber: episodeNumber, airDate: airDate, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        Data2 seriesData = seriesResponse.Data;

        if (seriesData?.Episodes == null || seriesData.Episodes.Count == 0)
        {
            return null;
        }
        else
        {
            var tvdbId = seriesData.Episodes[0].Id?.ToString(CultureInfo.InvariantCulture);
            if (key != null)
            {
                _memoryCache.Set(key, tvdbId, TimeSpan.FromHours(CacheDurationInHours));
            }

            return tvdbId;
        }
    }

    public async Task<IReadOnlyList<EntityUpdate>> GetUpdates(
        double fromTime,
        CancellationToken cancellationToken,
        Type? type = null,
        Action? action = null)
    {
        var updatesClient = _serviceProvider.GetRequiredService<IUpdatesClient>();
        await LoginAsync().ConfigureAwait(false);
        var updatesResult = await updatesClient.UpdatesAsync(since: fromTime, type: type, action: action, cancellationToken: cancellationToken).ConfigureAwait(false);
        var updates = updatesResult.Data.ToList();

        int page = 1;
        while (updatesResult.Links.Next != null)
        {
            updatesResult = await updatesClient.UpdatesAsync(since: fromTime, type: type, action: action, page: page, cancellationToken: cancellationToken).ConfigureAwait(false);
            updates.AddRange(updatesResult.Data);
            page++;
        }

        return updates;
    }

    public bool PurgeCache()
    {
        if (_memoryCache is MemoryCache memoryCache)
        {
            memoryCache.Clear();
            return true;
        }
        else
        {
            return false;
        }
    }

    private ServiceProvider ConfigureService(IApplicationHost applicationHost)
    {
        var productHeader = ProductInfoHeaderValue.Parse(applicationHost.ApplicationUserAgent);

        var assembly = typeof(TvdbPlugin).Assembly.GetName();
        var pluginHeader = new ProductInfoHeaderValue(
            assembly.Name!.Replace(' ', '-').Replace('.', '-'),
            assembly.Version!.ToString(3));

        var contactHeader = new ProductInfoHeaderValue($"({applicationHost.ApplicationUserAgentAddress})");

        var services = new ServiceCollection();

        services.AddSingleton<SdkClientSettings>();
        services.AddHttpClient(TvdbHttpClient, c =>
            {
                c.DefaultRequestHeaders.UserAgent.Add(productHeader);
                c.DefaultRequestHeaders.UserAgent.Add(pluginHeader);
                c.DefaultRequestHeaders.UserAgent.Add(contactHeader);
            })
            .ConfigurePrimaryHttpMessageHandler(_ => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8
            });

        services.AddTransient<ILoginClient>(_ => new LoginClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<ISearchClient>(_ => new SearchClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<ISeriesClient>(_ => new SeriesClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<IExtendedSeasonClient>(_ => new ExtendedSeasonClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<IEpisodesClient>(_ => new EpisodesClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<IPeopleClient>(_ => new PeopleClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<IArtworkClient>(_ => new ArtworkClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<IArtwork_TypesClient>(_ => new Artwork_TypesClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<ILanguagesClient>(_ => new LanguagesClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<ICountriesClient>(_ => new CountriesClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<IUpdatesClient>(_ => new UpdatesClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));
        services.AddTransient<IMoviesClient>(_ => new MoviesClient(_sdkClientSettings, _httpClientFactory.CreateClient(TvdbHttpClient)));

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
       Dispose(true);
       GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _memoryCache?.Dispose();
        }
    }
}
