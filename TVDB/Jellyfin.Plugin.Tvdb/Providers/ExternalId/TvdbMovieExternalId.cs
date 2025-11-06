using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Tvdb.Providers.ExternalId
{
    public class TvdbMovieExternalId : IExternalId
    {
        public string ProviderName => TvdbPlugin.ProviderName + " Numerical";

        public string Key => TvdbPlugin.ProviderId;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

        public bool Supports(IHasProviderIds item) => item is Movie;
    }
}
