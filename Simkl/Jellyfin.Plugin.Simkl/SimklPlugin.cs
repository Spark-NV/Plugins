using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Simkl
{
    public class SimklPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public SimklPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static SimklPlugin? Instance { get; private set; }

        public override Guid Id => new Guid("ce05df96-ddc3-43a2-915c-536f1ad61556");

        public override string Name => "Simkl";

        public override string Description => "Scrobble your watched Movies, TV Shows and Anime to Simkl and share your progress with friends!";

        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            };
        }
    }
}