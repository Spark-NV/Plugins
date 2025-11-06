using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Tvdb.Providers.ExternalId
{
    public class TvdbMovieSlugExternalId : IExternalId
    {
        public string ProviderName => TvdbPlugin.ProviderName + " Slug";

        public string Key => TvdbPlugin.SlugProviderId;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

        public bool Supports(IHasProviderIds item) => item is Movie;
    }
}
