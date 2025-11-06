using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class Season
    {
        [JsonPropertyName("number")]
        public int? Number { get; set; }

        [JsonPropertyName("episodes")]
        public IReadOnlyList<ShowEpisode> Episodes { get; set; } = Array.Empty<ShowEpisode>();
    }
}
