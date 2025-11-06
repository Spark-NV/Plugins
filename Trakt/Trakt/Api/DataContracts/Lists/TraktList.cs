using System.Text.Json.Serialization;

namespace Trakt.Api.DataContracts.Lists;

/// <summary>
/// Trakt list data contract.
/// </summary>
public class TraktList
{
    /// <summary>
    /// Gets or sets the list name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the list description.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the list privacy.
    /// </summary>
    public string Privacy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the list allows comments.
    /// </summary>
    public bool? DisplayNumbers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the list allows comments.
    /// </summary>
    public bool? AllowComments { get; set; }

    /// <summary>
    /// Gets or sets the sort by value.
    /// </summary>
    public string SortBy { get; set; }

    /// <summary>
    /// Gets or sets the sort how value.
    /// </summary>
    public string SortHow { get; set; }

    /// <summary>
    /// Gets or sets the list creation date.
    /// </summary>
    public string CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the list last update date.
    /// </summary>
    public string UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the item count.
    /// </summary>
    [JsonPropertyName("item_count")]
    public int? ItemCount { get; set; }

    /// <summary>
    /// Gets or sets the comment count.
    /// </summary>
    public int? CommentCount { get; set; }

    /// <summary>
    /// Gets or sets the likes count.
    /// </summary>
    public int? Likes { get; set; }

    /// <summary>
    /// Gets or sets the list IDs.
    /// </summary>
    [JsonPropertyName("ids")]
    public TraktListIds Ids { get; set; }

    /// <summary>
    /// Gets or sets the user who owns the list.
    /// </summary>
    public TraktListUser User { get; set; }
}
