using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class SimklShowIds : SimklIds
    {
        public SimklShowIds()
        {
        }

        public SimklShowIds(Dictionary<string, string> providerMovieIds)
            : base(providerMovieIds)
        {
        }

        [JsonPropertyName("mal")]
        public int? Mal { get; set; }

        [JsonPropertyName("hulu")]
        public int? Hulu { get; set; }

        [JsonPropertyName("crunchyroll")]
        public int? Crunchyroll { get; set; }

        [JsonPropertyName("zap2It")]
        public string? Zap2It { get; set; }
    }
}
