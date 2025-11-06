using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class SimklFile
    {
        [JsonPropertyName("file")]
        public string? File { get; set; }

        [JsonPropertyName("part")]
        public int? Part { get; set; }

        [JsonPropertyName("hash")]
        public string? Hash { get; set; }
    }
}
