using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Tvdb.Providers.ExternalId
{
    public class TvdbCollectionsExternalId : IExternalId
    {
        public string ProviderName => TvdbPlugin.ProviderName;

        public string Key => TvdbPlugin.CollectionProviderId;

        public ExternalIdMediaType? Type => ExternalIdMediaType.BoxSet;

        public bool Supports(IHasProviderIds item)
        {
            return item is Movie || item is Series;
        }
    }
}
