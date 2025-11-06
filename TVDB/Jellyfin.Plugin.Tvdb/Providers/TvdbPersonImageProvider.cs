using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Tvdb.Sdk;

namespace Jellyfin.Plugin.Tvdb.Providers
{
    public class TvdbPersonImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TvdbPersonImageProvider> _logger;
        private readonly TvdbClientManager _tvdbClientManager;

        public TvdbPersonImageProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<TvdbPersonImageProvider> logger,
            TvdbClientManager tvdbClientManager)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _tvdbClientManager = tvdbClientManager;
        }

        public string Name => TvdbPlugin.ProviderName;

        public bool Supports(BaseItem item) => item is Person;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            if (!item.TryGetProviderId(MetadataProvider.Tvdb, out var personTvdbId))
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var personTvdbIdInt = int.Parse(personTvdbId!, CultureInfo.InvariantCulture);
            try
            {
                var personResult = await _tvdbClientManager.GetActorExtendedByIdAsync(personTvdbIdInt, cancellationToken).ConfigureAwait(false);
                if (personResult.Image is null)
                {
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                return
                [
                    new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Primary,
                        Url = personResult.Image,
                    },
                ];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve actor {ActorName} with {ActorTvdbId}", item.Name, personTvdbId);
                return Enumerable.Empty<RemoteImageInfo>();
            }
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
        }
    }
}
