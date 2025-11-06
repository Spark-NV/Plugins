using System.Text.Json.Serialization;
#pragma warning disable SA1300

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    public class CodeStatusResponse
    {
        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
}
