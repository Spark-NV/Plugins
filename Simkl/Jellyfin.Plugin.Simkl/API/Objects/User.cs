using System.Text.Json.Serialization;

#pragma warning disable SA1300

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class User
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
