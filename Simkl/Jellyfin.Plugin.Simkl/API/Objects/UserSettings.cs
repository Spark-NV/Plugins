using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class UserSettings
    {
        [JsonPropertyName("user")]
        public User? User { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
