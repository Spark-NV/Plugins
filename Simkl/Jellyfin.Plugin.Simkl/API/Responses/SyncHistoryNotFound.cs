using System;
using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    public class SyncHistoryNotFound
    {
        public SimklMovie[] Movies { get; set; } = Array.Empty<SimklMovie>();

        public SimklShow[] Shows { get; set; } = Array.Empty<SimklShow>();

        public SimklEpisode[] Episodes { get; set; } = Array.Empty<SimklEpisode>();
    }
}
