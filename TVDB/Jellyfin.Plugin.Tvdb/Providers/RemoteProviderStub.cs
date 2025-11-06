using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Tvdb.Providers
{
    public class RemoteProviderStub : IMetadataProvider<Series>, IRemoteMetadataProvider
    {
        public string Name => TvdbMissingEpisodeProvider.ProviderName;
    }
}
