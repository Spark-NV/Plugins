using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    public class SearchFileResponse
    {
        public string? Type { get; set; }

        public SimklEpisode? Episode { get; set; }

        public SimklMovie? Movie { get; set; }

        public SimklShow? Show { get; set; }
    }
}
