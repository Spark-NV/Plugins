using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Tvdb.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Tvdb
{
    public class TvdbPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public const string ProviderName = "TheTVDB";

        public const string ProviderId = "Tvdb";

        public const string CollectionProviderId = "TvdbCollection";

        public const string SlugProviderId = "TvdbSlug";

        public TvdbPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static TvdbPlugin? Instance { get; private set; }

        public override string Name => "TheTVDB";

        public override Guid Id => new Guid("406f38ec-4d4c-4ccc-847d-1838c3aeea0b");

        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.config.html"
            };
        }
    }
}
