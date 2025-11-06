using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    public class ShowListResponse
    {
        [JsonPropertyName("shows")]
        public List<SimklShowItem> ShowItems { get; set; } = new List<SimklShowItem>();

        public List<SimklShow> Shows => ShowItems
            .Where(item => item.Show != null)
            .Select(item => item.Show!)
            .ToList();
    }
}
