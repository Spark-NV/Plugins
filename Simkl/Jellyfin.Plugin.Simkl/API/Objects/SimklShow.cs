using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class SimklShow : SimklMediaObject
    {
        public SimklShow()
        {
            Title = string.Empty;
            Seasons = Array.Empty<Season>();
        }

        public SimklShow(BaseItemDto mediaInfo)
        {
            Title = mediaInfo.SeriesName;
            Ids = new SimklShowIds(mediaInfo.ProviderIds);
            Year = mediaInfo.ProductionYear;
            Seasons = new[]
            {
                new Season
                {
                    Number = mediaInfo.ParentIndexNumber,
                    Episodes = new[]
                    {
                        new ShowEpisode
                        {
                            Number = mediaInfo.IndexNumber
                        }
                    }
                }
            };
        }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("seasons")]
        public IReadOnlyList<Season> Seasons { get; set; } = Array.Empty<Season>();
    }
}
