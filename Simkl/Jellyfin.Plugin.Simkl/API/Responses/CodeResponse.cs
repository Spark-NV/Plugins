using System.Text.Json.Serialization;
#pragma warning disable SA1300

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    public class CodeResponse
    {
        public string? Result { get; set; }

        [JsonPropertyName("device_code")]
        public string? DeviceCode { get; set; }

        [JsonPropertyName("user_code")]
        public string? UserCode { get; set; }

        [JsonPropertyName("verification_url")]
        public string? VerificationUrl { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        public int? Interval { get; set; }
    }
}
