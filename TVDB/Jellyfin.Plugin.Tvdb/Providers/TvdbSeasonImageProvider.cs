using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Providers;

using Microsoft.Extensions.Logging;

using Tvdb.Sdk;

namespace Jellyfin.Plugin.Tvdb.Providers;

public class TvdbSeasonImageProvider : IRemoteImageProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TvdbSeasonImageProvider> _logger;
    private readonly TvdbClientManager _tvdbClientManager;

    public TvdbSeasonImageProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<TvdbSeasonImageProvider> logger,
        TvdbClientManager tvdbClientManager)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _tvdbClientManager = tvdbClientManager;
    }

    public string Name => TvdbPlugin.ProviderName;

    public bool Supports(BaseItem item)
    {
        return item is Season;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
        yield return ImageType.Banner;
        yield return ImageType.Backdrop;
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var season = (Season)item;
        var series = season.Series;

        if (!series.IsSupported() || season.IndexNumber is null)
        {
            return Enumerable.Empty<RemoteImageInfo>();
        }

        var languages = await _tvdbClientManager.GetLanguagesAsync(cancellationToken)
            .ConfigureAwait(false);
        var languageLookup = languages
            .ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);

        var artworkTypes = await _tvdbClientManager.GetArtworkTypeAsync(cancellationToken)
            .ConfigureAwait(false);
        var seasonArtworkTypeLookup = artworkTypes
            .Where(t => string.Equals(t.RecordType, "season", StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Id.HasValue)
            .ToDictionary(t => t.Id!.Value);

        var seriesTvdbId = series.GetTvdbId();
        var seasonNumber = season.IndexNumber.Value;
        var displayOrder = season.Series.DisplayOrder;

        if (string.IsNullOrEmpty(displayOrder))
        {
            displayOrder = "official";
        }

        var seasonArtworks = await GetSeasonArtworks(seriesTvdbId, seasonNumber, displayOrder, cancellationToken)
            .ConfigureAwait(false);

        var remoteImages = new List<RemoteImageInfo>();
        foreach (var artwork in seasonArtworks)
        {
            var artworkType = artwork.Type is null ? null : seasonArtworkTypeLookup.GetValueOrDefault(artwork.Type!.Value);
            var imageType = artworkType.GetImageType();
            var artworkLanguage = artwork.Language is null ? null : languageLookup.GetValueOrDefault(artwork.Language);

            remoteImages.AddIfNotNull(artwork.CreateImageInfo(Name, imageType, artworkLanguage));
        }

        return remoteImages.OrderByLanguageDescending(item.GetPreferredMetadataLanguage());
    }

    private async Task<IReadOnlyList<ArtworkBaseRecord>> GetSeasonArtworks(int seriesTvdbId, int seasonNumber, string displayOrder, CancellationToken cancellationToken)
    {
        try
        {
            var seriesInfo = await _tvdbClientManager.GetSeriesExtendedByIdAsync(seriesTvdbId, string.Empty, cancellationToken, small: true)
                .ConfigureAwait(false);
            var seasonBaseList = seriesInfo.Seasons.Where(s => s.Number == seasonNumber && (s.Type.Type == displayOrder || s.Type.Type == "official" )).OrderBy(s => s.Type.Type == "official");

            var seasonTvdbId = seasonBaseList?.FirstOrDefault()?.Id;
            var seasonInfo = await _tvdbClientManager.GetSeasonByIdAsync(seasonTvdbId ?? 0, string.Empty, cancellationToken)
                .ConfigureAwait(false);

            if ((seasonInfo.Artwork is null || seasonInfo.Artwork.Count == 0)
                && !string.Equals(displayOrder, "official", StringComparison.OrdinalIgnoreCase))
            {
               seasonTvdbId = seasonBaseList?.Skip(1).FirstOrDefault()?.Id;
               seasonInfo = await _tvdbClientManager.GetSeasonByIdAsync(seasonTvdbId ?? 0, string.Empty, cancellationToken)
                 .ConfigureAwait(false);
            }

            return seasonInfo.Artwork ?? Enumerable.Empty<ArtworkBaseRecord>().ToList();
        }
        catch (Exception ex) when (
            (ex is SeriesException seriesEx && seriesEx.InnerException is JsonException)
            || (ex is SeasonsException seasonEx && seasonEx.InnerException is JsonException))
        {
            _logger.LogError(ex, "Failed to retrieve season images for series {TvDbId}", seriesTvdbId);
            return Array.Empty<ArtworkBaseRecord>();
        }
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
    }
}
