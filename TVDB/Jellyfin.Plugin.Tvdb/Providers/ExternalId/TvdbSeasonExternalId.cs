using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Tvdb.Providers.ExternalId
{
    public class TvdbSeasonExternalId : IExternalId
    {
        public string ProviderName => TvdbPlugin.ProviderName;

        public string Key => TvdbPlugin.ProviderId;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Season;

        public bool Supports(IHasProviderIds item) => item is Season;
    }
}
