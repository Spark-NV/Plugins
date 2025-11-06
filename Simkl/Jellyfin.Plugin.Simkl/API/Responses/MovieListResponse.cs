using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    public class MovieListResponse
    {
        [JsonPropertyName("movies")]
        public List<SimklMovieItem> MovieItems { get; set; } = new List<SimklMovieItem>();

        public List<SimklMovie> Movies => MovieItems
            .Where(item => item.Movie != null)
            .Select(item => item.Movie!)
            .ToList();
    }
}
