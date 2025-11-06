using System;
using System.Text.Json.Serialization;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class SimklMovie : SimklMediaObject
    {
        public SimklMovie()
        {
        }

        public SimklMovie(BaseItemDto item)
        {
            Title = item.OriginalTitle;
            Year = item.ProductionYear;
            Ids = new SimklMovieIds(item.ProviderIds);
            WatchedAt = DateTime.UtcNow;
        }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("watched_at")]
        public DateTime? WatchedAt { get; set; }
    }
}
