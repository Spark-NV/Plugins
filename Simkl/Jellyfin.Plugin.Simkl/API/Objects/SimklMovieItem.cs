using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class SimklMovieItem
    {
        [JsonPropertyName("movie")]
        public SimklMovie? Movie { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("added_to_watchlist_at")]
        public DateTime? AddedToWatchlistAt { get; set; }

        [JsonPropertyName("last_watched_at")]
        public DateTime? LastWatchedAt { get; set; }
    }
}
