using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Tvdb.Providers.ExternalId
{
    public class TvdbPersonExternalId : IExternalId
    {
        public string ProviderName => TvdbPlugin.ProviderName;

        public string Key => TvdbPlugin.ProviderId;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

        public bool Supports(IHasProviderIds item) => item is Person;
    }
}
