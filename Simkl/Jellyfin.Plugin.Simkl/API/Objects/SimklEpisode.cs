using System;
using System.Text.Json.Serialization;
using MediaBrowser.Model.Dto;
#pragma warning disable SA1300

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class SimklEpisode : SimklMediaObject
    {
        public SimklEpisode(BaseItemDto media)
        {
            Title = media?.SeriesName;
            Ids = media is not null ? new SimklIds(media.ProviderIds) : null;
            Year = media?.ProductionYear;
            Season = media?.ParentIndexNumber;
            Episode = media?.IndexNumber;
        }

        [JsonPropertyName("watched_at")]
        public DateTime? WatchedAt { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("season")]
        public int? Season { get; set; }

        [JsonPropertyName("episode")]
        public int? Episode { get; set; }

        [JsonPropertyName("multipart")]
        public bool? Multipart { get; set; }
    }
}
