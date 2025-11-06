using Jellyfin.Plugin.Tvdb.Providers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Tvdb
{
    public class TvdbPluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<TvdbClientManager>();
            serviceCollection.AddHostedService<TvdbMissingEpisodeProvider>();
        }
    }
}
