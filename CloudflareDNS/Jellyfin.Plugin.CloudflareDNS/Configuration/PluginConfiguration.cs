using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CloudflareDNS.Configuration
{

    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Active { get; set; }
        public string Hostname { get; set; }
        public string ApiToken { get; set; }

        public PluginConfiguration()
        {
			Active = true;
			Hostname = "";
			ApiToken = "";
        }
    }
}
