using System.Text.Json.Serialization;
using Trakt.Api.DataContracts.BaseModel;

namespace Trakt.Api.DataContracts.Lists;

/// <summary>
/// Trakt list item data contract.
/// </summary>
public class TraktListItem
{
    /// <summary>
    /// Gets or sets the rank.
    /// </summary>
    [JsonPropertyName("rank")]
    public int? Rank { get; set; }

    /// <summary>
    /// Gets or sets the list date.
    /// </summary>
    [JsonPropertyName("listed_at")]
    public string ListedAt { get; set; }

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; }

    /// <summary>
    /// Gets or sets the type (movie, show, episode).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the movie data (if type is movie).
    /// </summary>
    [JsonPropertyName("movie")]
    public TraktMovie Movie { get; set; }

    /// <summary>
    /// Gets or sets the show data (if type is show or episode).
    /// </summary>
    [JsonPropertyName("show")]
    public TraktShow Show { get; set; }

    /// <summary>
    /// Gets or sets the episode data (if type is episode).
    /// </summary>
    [JsonPropertyName("episode")]
    public TraktEpisode Episode { get; set; }
}
