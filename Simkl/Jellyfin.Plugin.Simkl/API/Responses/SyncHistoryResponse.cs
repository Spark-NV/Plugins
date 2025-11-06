using System.Text.Json.Serialization;
#pragma warning disable SA1300

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    public class SyncHistoryResponse
    {
        public SyncHistoryResponseCount Added { get; set; } = new SyncHistoryResponseCount();

        [JsonPropertyName("not_found")]
        public SyncHistoryNotFound? NotFound { get; set; }
    }
}
