using System;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Simkl.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            UserConfigs = Array.Empty<UserConfig>();
        }

        public UserConfig[] UserConfigs { get; set; }

        public UserConfig? GetByGuid(Guid id)
        {
            return UserConfigs.FirstOrDefault(c => c.Id == id);
        }

        public void DeleteUserToken(string userToken)
        {
            foreach (var config in UserConfigs)
            {
                if (config.UserToken == userToken)
                {
                    config.UserToken = string.Empty;
                }
            }

            SimklPlugin.Instance?.SaveConfiguration();
        }
    }
}
