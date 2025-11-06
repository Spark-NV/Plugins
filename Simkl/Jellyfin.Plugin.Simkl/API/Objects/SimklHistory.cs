#pragma warning disable CA2227

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class SimklHistory
    {
        public SimklHistory()
        {
            Movies = new List<SimklMovie>();
            Shows = new List<SimklShow>();
            Episodes = new List<SimklEpisode>();
        }

        [JsonPropertyName("movies")]
        public List<SimklMovie> Movies { get; set; }

        [JsonPropertyName("shows")]
        public List<SimklShow> Shows { get; set; }

        [JsonPropertyName("episodes")]
        public List<SimklEpisode> Episodes { get; set; }
    }
}
