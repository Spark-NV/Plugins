using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Tvdb.Providers.ExternalId
{
    public class TvdbSeriesExternalId : IExternalId
    {
        public string ProviderName => TvdbPlugin.ProviderName + " Numerical";

        public string Key => TvdbPlugin.ProviderId;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

        public bool Supports(IHasProviderIds item) => item is Series;
    }
}
