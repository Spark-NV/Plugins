using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Tvdb.Sdk;

namespace Jellyfin.Plugin.Tvdb.Providers
{
    public class TvdbMovieImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TvdbMovieImageProvider> _logger;
        private readonly TvdbClientManager _tvdbClientManager;

        public TvdbMovieImageProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<TvdbMovieImageProvider> logger,
            TvdbClientManager tvdbClientManager)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _tvdbClientManager = tvdbClientManager;
        }

        public string Name => TvdbPlugin.ProviderName;

        public bool Supports(BaseItem item)
        {
            return item is Movie;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
            yield return ImageType.Banner;
            yield return ImageType.Backdrop;
            yield return ImageType.Logo;
            yield return ImageType.Art;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            if (!item.IsSupported())
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var languages = await _tvdbClientManager.GetLanguagesAsync(cancellationToken)
                .ConfigureAwait(false);
            var languageLookup = languages
                .ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);

            var artworkTypes = await _tvdbClientManager.GetArtworkTypeAsync(cancellationToken)
                .ConfigureAwait(false);
            var movieArtworkTypeLookup = artworkTypes
                .Where(t => string.Equals(t.RecordType, "movie", StringComparison.OrdinalIgnoreCase))
                .Where(t => t.Id.HasValue)
                .ToDictionary(t => t.Id!.Value);

            var movieTvdbId = item.GetTvdbId();
            var movieArtworks = await GetMovieArtworks(movieTvdbId, cancellationToken)
                .ConfigureAwait(false);

            var remoteImages = new List<RemoteImageInfo>();
            foreach (var artwork in movieArtworks)
            {
            var artworkType = artwork.Type is null ? null : movieArtworkTypeLookup.GetValueOrDefault(artwork.Type!.Value);
            var imageType = artworkType.GetImageType();
            var artworkLanguage = artwork.Language is null ? null : languageLookup.GetValueOrDefault(artwork.Language);

            remoteImages.AddIfNotNull(artwork.CreateImageInfo(Name, imageType, artworkLanguage));
            }

            return remoteImages.OrderByLanguageDescending(item.GetPreferredMetadataLanguage());
        }

        private async Task<IReadOnlyList<ArtworkBaseRecord>> GetMovieArtworks(int movieTvdbId, CancellationToken cancellationToken)
        {
            var movieInfo = await _tvdbClientManager.GetMovieExtendedByIdAsync(movieTvdbId, cancellationToken)
                .ConfigureAwait(false);
            return movieInfo?.Artworks ?? Array.Empty<ArtworkBaseRecord>();
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
        }
    }
}
