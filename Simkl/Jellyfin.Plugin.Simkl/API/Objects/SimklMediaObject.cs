using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class SimklMediaObject
    {
        [JsonPropertyName("ids")]
        public SimklIds? Ids { get; set; }
    }
}
