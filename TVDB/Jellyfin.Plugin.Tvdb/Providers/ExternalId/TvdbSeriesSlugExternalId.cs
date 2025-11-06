using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Tvdb.Providers.ExternalId
{
    public class TvdbSeriesSlugExternalId : IExternalId
    {
        public string ProviderName => TvdbPlugin.ProviderName + " Slug";

        public string Key => TvdbPlugin.SlugProviderId;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

        public bool Supports(IHasProviderIds item) => item is Series;
    }
}
