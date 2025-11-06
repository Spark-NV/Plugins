using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class SimklIds
    {
        public SimklIds()
        {
        }

        public SimklIds(Dictionary<string, string> providerIds)
        {
            foreach (var (key, value) in providerIds)
            {
                if (key.Equals(nameof(Simkl), StringComparison.OrdinalIgnoreCase))
                {
                    Simkl = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
                else if (key.Equals(nameof(Anidb), StringComparison.OrdinalIgnoreCase))
                {
                    Anidb = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
                else if (key.Equals(nameof(Imdb), StringComparison.OrdinalIgnoreCase))
                {
                    Imdb = value;
                }
                else if (key.Equals(nameof(Tvdb), StringComparison.OrdinalIgnoreCase))
                {
                    Tvdb = value;
                }
                else if (key.Equals(nameof(Slug), StringComparison.OrdinalIgnoreCase))
                {
                    Slug = value;
                }
                else if (key.Equals(nameof(Netflix), StringComparison.OrdinalIgnoreCase))
                {
                    Netflix = value;
                }
                else if (key.Equals(nameof(Tmdb), StringComparison.OrdinalIgnoreCase))
                {
                    Tmdb = value;
                }
            }
        }

        [JsonPropertyName("simkl")]
        public int? Simkl { get; set; }

        [JsonPropertyName("imdb")]
        public string? Imdb { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("netflix")]
        public string? Netflix { get; set; }

        [JsonPropertyName("tmdb")]
        public string? Tmdb { get; set; }

        [JsonPropertyName("tvdb")]
        public string? Tvdb { get; set; }

        [JsonPropertyName("anidb")]
        public int? Anidb { get; set; }
    }
}
