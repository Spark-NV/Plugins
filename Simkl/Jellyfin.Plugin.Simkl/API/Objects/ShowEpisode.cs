using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class ShowEpisode
    {
        [JsonPropertyName("number")]
        public int? Number { get; set; }
    }
}