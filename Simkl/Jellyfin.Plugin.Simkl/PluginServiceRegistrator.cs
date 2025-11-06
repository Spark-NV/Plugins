using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Simkl
{
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<SimklApi>();
            serviceCollection.AddSingleton<PlanToWatchImporter>();
            serviceCollection.AddSingleton<ImportSimklListTask>();
        }
    }
}
