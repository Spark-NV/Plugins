using System.Text.Json.Serialization;

namespace Trakt.Api.DataContracts.Lists;

/// <summary>
/// Trakt list IDs data contract.
/// </summary>
public class TraktListIds
{
    /// <summary>
    /// Gets or sets the Trakt ID.
    /// </summary>
    [JsonPropertyName("trakt")]
    public int? Trakt { get; set; }

    /// <summary>
    /// Gets or sets the slug.
    /// </summary>
    [JsonPropertyName("slug")]
    public string Slug { get; set; }
}
