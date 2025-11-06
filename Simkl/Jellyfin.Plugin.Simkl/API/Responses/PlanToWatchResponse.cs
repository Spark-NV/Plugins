using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    public class PlanToWatchResponse
    {
        [JsonPropertyName("movies")]
        public List<SimklMovie> Movies { get; set; } = new List<SimklMovie>();

        [JsonPropertyName("shows")]
        public List<SimklShow> Shows { get; set; } = new List<SimklShow>();
    }
}
