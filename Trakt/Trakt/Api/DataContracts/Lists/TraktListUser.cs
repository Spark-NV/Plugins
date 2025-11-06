namespace Trakt.Api.DataContracts.Lists;

/// <summary>
/// Trakt list user data contract.
/// </summary>
public class TraktListUser
{
    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is private.
    /// </summary>
    public bool? Private { get; set; }

    /// <summary>
    /// Gets or sets the user name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is a VIP.
    /// </summary>
    public bool? Vip { get; set; }

    /// <summary>
    /// Gets or sets the user IDs.
    /// </summary>
    public TraktListUserIds Ids { get; set; }
}
