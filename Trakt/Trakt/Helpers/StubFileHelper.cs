using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Trakt.Api.DataContracts.BaseModel;
using Trakt.Api.DataContracts.Lists;

namespace Trakt.Helpers;

/// <summary>
/// Helper class for creating stub files from Trakt list items.
/// </summary>
public class StubFileHelper
{
    private readonly ILogger<StubFileHelper> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StubFileHelper"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public StubFileHelper(ILogger<StubFileHelper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates stub files for all items in a Trakt list.
    /// </summary>
    /// <param name="listItems">The list items to create stubs for.</param>
    /// <param name="movieBasePath">The base library path where movie stubs should be created.</param>
    /// <param name="tvBasePath">The base library path where TV show stubs should be created.</param>
    /// <returns>The number of stubs created.</returns>
    public int CreateStubFiles(IReadOnlyList<TraktListItem> listItems, string movieBasePath, string tvBasePath)
    {
        if (listItems == null || listItems.Count == 0)
        {
            _logger.LogWarning("No list items provided");
            return 0;
        }

        int created = 0;

        foreach (var item in listItems)
        {
            try
            {
                // Determine item type - use explicit type or infer from non-null properties
                string itemType = item.Type?.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(itemType))
                {
                    // Infer type from available properties
                    if (item.Movie != null)
                    {
                        itemType = "movie";
                    }
                    else if (item.Episode != null && item.Show != null)
                    {
                        itemType = "episode";
                    }
                    else if (item.Show != null)
                    {
                        itemType = "show";
                    }
                    else
                    {
                        _logger.LogWarning("Cannot determine list item type - all properties are null. Movie: {HasMovie}, Show: {HasShow}, Episode: {HasEpisode}", item.Movie != null, item.Show != null, item.Episode != null);
                        continue;
                    }

                    _logger.LogDebug("Inferred list item type as: {Type}", itemType);
                }

                switch (itemType)
                {
                    case "movie":
                        if (item.Movie != null)
                        {
                            if (!string.IsNullOrWhiteSpace(movieBasePath) && Directory.Exists(movieBasePath))
                            {
                                if (CreateMovieStub(item.Movie, movieBasePath))
                                {
                                    created++;
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Movie base path does not exist: {BasePath}", movieBasePath);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("List item type is 'movie' but Movie property is null");
                        }

                        break;
                    case "show":
                        if (item.Show != null)
                        {
                            if (!string.IsNullOrWhiteSpace(tvBasePath) && Directory.Exists(tvBasePath))
                            {
                                if (CreateShowStub(item.Show, tvBasePath))
                                {
                                    created++;
                                }
                            }
                            else
                            {
                                _logger.LogWarning("TV base path does not exist: {BasePath}", tvBasePath);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("List item type is 'show' but Show property is null");
                        }

                        break;
                    case "episode":
                        if (item.Show != null && item.Episode != null)
                        {
                            if (!string.IsNullOrWhiteSpace(tvBasePath) && Directory.Exists(tvBasePath))
                            {
                                if (CreateEpisodeStub(item.Show, item.Episode, tvBasePath))
                                {
                                    created++;
                                }
                            }
                            else
                            {
                                _logger.LogWarning("TV base path does not exist: {BasePath}", tvBasePath);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("List item type is 'episode' but Show or Episode property is null");
                        }

                        break;
                    default:
                        _logger.LogWarning("Unknown list item type: {Type}", itemType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating stub for item type {Type}", item.Type ?? "unknown");
            }
        }

        _logger.LogInformation("Created {Count} stub files", created);
        return created;
    }

    /// <summary>
    /// Creates a stub file for a movie.
    /// </summary>
    /// <param name="movie">The movie data.</param>
    /// <param name="basePath">The base library path.</param>
    /// <returns>True if the stub was created successfully.</returns>
    private bool CreateMovieStub(TraktMovie movie, string basePath)
    {
        if (string.IsNullOrWhiteSpace(movie.Title))
        {
            _logger.LogWarning("Movie title is empty");
            return false;
        }

        var movieFolder = SanitizeFileName(movie.Title);
        if (movie.Year.HasValue && movie.Year.Value > 0)
        {
            movieFolder = $"{movieFolder} ({movie.Year.Value})";
        }

        var moviePath = Path.Combine(basePath, movieFolder);
        Directory.CreateDirectory(moviePath);

        var fileName = movieFolder + ".mp4";
        var filePath = Path.Combine(moviePath, fileName);

        if (File.Exists(filePath))
        {
            _logger.LogDebug("Stub file already exists: {FilePath}", filePath);
            return false;
        }

        // Create an empty stub file
        File.WriteAllText(filePath, string.Empty);
        _logger.LogDebug("Created movie stub: {FilePath}", filePath);
        return true;
    }

    /// <summary>
    /// Creates a stub file for a TV show (series-level stub).
    /// </summary>
    /// <param name="show">The show data.</param>
    /// <param name="basePath">The base library path.</param>
    /// <returns>True if the stub was created successfully.</returns>
    private bool CreateShowStub(TraktShow show, string basePath)
    {
        if (string.IsNullOrWhiteSpace(show.Title))
        {
            _logger.LogWarning("Show title is empty");
            return false;
        }

        var showFolder = SanitizeFileName(show.Title);
        if (show.Year.HasValue && show.Year.Value > 0)
        {
            showFolder = $"{showFolder} ({show.Year.Value})";
        }

        var showPath = Path.Combine(basePath, showFolder);
        Directory.CreateDirectory(showPath);

        // For TV shows, we create a folder structure but don't create episode files
        // The folder structure itself will be recognized by Jellyfin
        // Optionally create a placeholder file in the show folder
        var placeholderPath = Path.Combine(showPath, ".trakt-stub");
        if (!File.Exists(placeholderPath))
        {
            File.WriteAllText(placeholderPath, $"Trakt list stub for {show.Title}");
            _logger.LogDebug("Created show stub folder: {ShowPath}", showPath);
        }

        return true;
    }

    /// <summary>
    /// Creates a stub file for a TV episode.
    /// </summary>
    /// <param name="show">The show data.</param>
    /// <param name="episode">The episode data.</param>
    /// <param name="basePath">The base library path.</param>
    /// <returns>True if the stub was created successfully.</returns>
    private bool CreateEpisodeStub(TraktShow show, TraktEpisode episode, string basePath)
    {
        if (string.IsNullOrWhiteSpace(show.Title))
        {
            _logger.LogWarning("Show title is empty");
            return false;
        }

        if (episode.Season <= 0 || episode.Number <= 0)
        {
            _logger.LogWarning("Episode missing season or episode number");
            return false;
        }

        var showFolder = SanitizeFileName(show.Title);
        if (show.Year.HasValue && show.Year.Value > 0)
        {
            showFolder = $"{showFolder} ({show.Year.Value})";
        }

        var showPath = Path.Combine(basePath, showFolder);
        var seasonNumber = episode.Season;
        var seasonFolder = $"Season {seasonNumber:D2}";
        var seasonPath = Path.Combine(showPath, seasonFolder);
        Directory.CreateDirectory(seasonPath);

        var episodeNumber = episode.Number;
        var fileName = $"{SanitizeFileName(show.Title)} - S{seasonNumber:D2}E{episodeNumber:D2}";
        if (!string.IsNullOrWhiteSpace(episode.Title))
        {
            fileName += $" - {SanitizeFileName(episode.Title)}";
        }

        fileName += ".mp4";
        var filePath = Path.Combine(seasonPath, fileName);

        if (File.Exists(filePath))
        {
            _logger.LogDebug("Stub file already exists: {FilePath}", filePath);
            return false;
        }

        // Create an empty stub file
        File.WriteAllText(filePath, string.Empty);
        _logger.LogDebug("Created episode stub: {FilePath}", filePath);
        return true;
    }

    /// <summary>
    /// Sanitizes a file name by removing invalid characters.
    /// </summary>
    /// <param name="fileName">The file name to sanitize.</param>
    /// <returns>The sanitized file name.</returns>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Unknown";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(fileName.Length);

        foreach (var c in fileName)
        {
            if (invalidChars.Contains(c))
            {
                sanitized.Append(' ');
            }
            else
            {
                sanitized.Append(c);
            }
        }

        return sanitized.ToString().Trim();
    }
}
