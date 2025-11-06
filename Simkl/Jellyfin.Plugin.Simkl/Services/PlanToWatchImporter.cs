using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    public class PlanToWatchImporter
    {
        private readonly ILogger<PlanToWatchImporter> _logger;
        private readonly SimklApi _simklApi;
        private readonly ILibraryManager _libraryManager;

        public PlanToWatchImporter(ILogger<PlanToWatchImporter> logger, SimklApi simklApi, ILibraryManager libraryManager)
        {
            _logger = logger;
            _simklApi = simklApi;
            _libraryManager = libraryManager;
        }

        public async Task<ImportResult> ImportPlanToWatch(UserConfig userConfig)
        {
            var result = new ImportResult();

            if (string.IsNullOrEmpty(userConfig.UserToken))
            {
                result.Error = "User token is not set. Please log in first.";
                return result;
            }

            try
            {
                var listStatus = string.IsNullOrEmpty(userConfig.ImportListStatus) ? "plantowatch" : userConfig.ImportListStatus;
                _logger.LogInformation("Fetching {ListStatus} list from Simkl for user", listStatus);
                var planToWatch = await _simklApi.GetListByStatus(userConfig.UserToken, listStatus);
                if (planToWatch == null)
                {
                    result.Error = $"Failed to retrieve {listStatus} list from Simkl. The list may be empty or the API response was invalid.";
                    _logger.LogError("Failed to retrieve {ListStatus} list from Simkl - response was null", listStatus);
                    return result;
                }

                if ((planToWatch.Movies == null || planToWatch.Movies.Count == 0) &&
                    (planToWatch.Shows == null || planToWatch.Shows.Count == 0))
                {
                    _logger.LogInformation("Retrieved {ListStatus} list from Simkl but it is empty (no movies or shows)", listStatus);
                    result.Success = true;
                    result.Message = $"The {listStatus} list from Simkl is empty. No items to import.";
                    return result;
                }

                _logger.LogInformation(
                    "Retrieved {MovieCount} movies and {ShowCount} shows from Simkl",
                    planToWatch.Movies?.Count ?? 0,
                    planToWatch.Shows?.Count ?? 0);

                string? movieStubPath = null;

                if (!string.IsNullOrEmpty(userConfig.MovieStubFilePath))
                {
                    if (File.Exists(userConfig.MovieStubFilePath))
                    {
                        movieStubPath = userConfig.MovieStubFilePath;
                        _logger.LogInformation("Using configured movie stub file: {Path}", movieStubPath);
                    }
                    else
                    {
                        _logger.LogWarning("Configured movie stub file not found: {Path}", userConfig.MovieStubFilePath);
                    }
                }

                if (movieStubPath == null)
                {
                    var pluginPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

                    if (pluginPath != null)
                    {
                        var path1 = Path.Combine(pluginPath, "movie_stub.mp4");
                        if (File.Exists(path1))
                        {
                            movieStubPath = path1;
                        }
                        else
                        {
                            var path2 = Path.Combine(pluginPath, "..", "..", "..", "..", "movie_stub.mp4");
                            path2 = Path.GetFullPath(path2);
                            if (File.Exists(path2))
                            {
                                movieStubPath = path2;
                            }
                        }
                    }

                    if (movieStubPath == null)
                    {
                        var path3 = Path.Combine(Directory.GetCurrentDirectory(), "movie_stub.mp4");
                        if (File.Exists(path3))
                        {
                            movieStubPath = path3;
                        }
                    }

                    if (movieStubPath == null || !File.Exists(movieStubPath))
                    {
                        _logger.LogWarning("Movie stub file not found. Searched in plugin directory and current working directory.");
                    }
                }

                string? moviesLibraryPath = null;
                if (userConfig.MoviesLibraryId != Guid.Empty)
                {
                    var moviesLibraryIdString = userConfig.MoviesLibraryId.ToString();
                    var allLibraries = _libraryManager.GetVirtualFolders().ToList();
                    _logger.LogDebug("Found {Count} virtual folders", allLibraries.Count);
                    foreach (var vf in allLibraries)
                    {
                        _logger.LogDebug("Virtual folder: Name={Name}, ItemId={ItemId}", vf.Name, vf.ItemId);
                    }

                    var moviesLibrary = allLibraries
                        .FirstOrDefault(
                            vf => vf.ItemId == moviesLibraryIdString
                                || (Guid.TryParse(vf.ItemId, out var guid) && guid == userConfig.MoviesLibraryId));

                    if (moviesLibrary != null)
                    {
                        _logger.LogDebug(
                            "Found movies library: {Name}, LibraryOptions is null: {IsNull}",
                            moviesLibrary.Name,
                            moviesLibrary.LibraryOptions == null);

                        if (moviesLibrary.LibraryOptions?.PathInfos != null)
                        {
                            var pathInfos = moviesLibrary.LibraryOptions.PathInfos.ToList();
                            _logger.LogDebug("Found {Count} path infos for movies library", pathInfos.Count);
                            foreach (var pathInfo in pathInfos)
                            {
                                _logger.LogDebug("Path info: {Path}", pathInfo.Path);
                            }

                            moviesLibraryPath = pathInfos.FirstOrDefault()?.Path;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Movies library with ID {LibraryId} not found in virtual folders", moviesLibraryIdString);
                    }

                    _logger.LogInformation(
                        "Movies library ID {LibraryId} resolved to path: {Path}",
                        moviesLibraryIdString,
                        moviesLibraryPath ?? "null");
                    if (string.IsNullOrEmpty(moviesLibraryPath))
                    {
                        _logger.LogWarning("Movies library ID {LibraryId} could not be resolved to a path", moviesLibraryIdString);
                    }
                }
#pragma warning disable CS0618
                else if (!string.IsNullOrEmpty(userConfig.MoviesLibraryPath))
                {
                    moviesLibraryPath = userConfig.MoviesLibraryPath;
                }
#pragma warning restore CS0618

                string? tvShowsLibraryPath = null;
                if (userConfig.TvShowsLibraryId != Guid.Empty)
                {
                    var tvShowsLibraryIdString = userConfig.TvShowsLibraryId.ToString();
                    var allLibraries = _libraryManager.GetVirtualFolders().ToList();

                    var tvShowsLibrary = allLibraries
                        .FirstOrDefault(
                            vf => vf.ItemId == tvShowsLibraryIdString
                                || (Guid.TryParse(vf.ItemId, out var guid) && guid == userConfig.TvShowsLibraryId));

                    if (tvShowsLibrary != null)
                    {
                        _logger.LogDebug(
                            "Found TV shows library: {Name}, LibraryOptions is null: {IsNull}",
                            tvShowsLibrary.Name,
                            tvShowsLibrary.LibraryOptions == null);

                        if (tvShowsLibrary.LibraryOptions?.PathInfos != null)
                        {
                            var pathInfos = tvShowsLibrary.LibraryOptions.PathInfos.ToList();
                            _logger.LogDebug("Found {Count} path infos for TV shows library", pathInfos.Count);
                            foreach (var pathInfo in pathInfos)
                            {
                                _logger.LogDebug("Path info: {Path}", pathInfo.Path);
                            }

                            tvShowsLibraryPath = pathInfos.FirstOrDefault()?.Path;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("TV Shows library with ID {LibraryId} not found in virtual folders", tvShowsLibraryIdString);
                    }

                    _logger.LogInformation(
                        "TV Shows library ID {LibraryId} resolved to path: {Path}",
                        tvShowsLibraryIdString,
                        tvShowsLibraryPath ?? "null");
                    if (string.IsNullOrEmpty(tvShowsLibraryPath))
                    {
                        _logger.LogWarning("TV Shows library ID {LibraryId} could not be resolved to a path", tvShowsLibraryIdString);
                    }
                }
#pragma warning disable CS0618
                else if (!string.IsNullOrEmpty(userConfig.TvShowsLibraryPath))
                {
                    tvShowsLibraryPath = userConfig.TvShowsLibraryPath;
                }
#pragma warning restore CS0618

                if (string.IsNullOrEmpty(moviesLibraryPath) && string.IsNullOrEmpty(tvShowsLibraryPath))
                {
                    result.Error = "No library paths configured. Please select at least one library (Movies or TV Shows).";
                    _logger.LogError("Import failed: No library paths configured");
                    return result;
                }

                if (!string.IsNullOrEmpty(moviesLibraryPath) && planToWatch.Movies != null && planToWatch.Movies.Count > 0)
                {
                    foreach (var movie in planToWatch.Movies)
                    {
                        try
                        {
                            var folderName = FormatMovieFolderName(movie);
                            var movieFolderPath = Path.Combine(moviesLibraryPath, folderName);

                            if (!Directory.Exists(movieFolderPath))
                            {
                                Directory.CreateDirectory(movieFolderPath);
                                result.MoviesCreated++;
                                _logger.LogInformation("Created movie folder: {Path}", movieFolderPath);
                            }

                            if (File.Exists(movieStubPath))
                            {
                                var fileName = FormatMovieFileName(movie);
                                var targetFile = Path.Combine(movieFolderPath, fileName);
                                if (!File.Exists(targetFile))
                                {
                                    File.Copy(movieStubPath, targetFile);
                                    result.MoviesFilesCopied++;
                                    _logger.LogInformation("Copied stub file to: {Path}", targetFile);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing movie: {Title}", movie.Title);
                            result.MoviesErrors++;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(tvShowsLibraryPath) && planToWatch.Shows != null && planToWatch.Shows.Count > 0)
                {
                    foreach (var show in planToWatch.Shows)
                    {
                        try
                        {
                            var folderName = FormatShowFolderName(show);
                            var showFolderPath = Path.Combine(tvShowsLibraryPath, folderName);

                            if (!Directory.Exists(showFolderPath))
                            {
                                Directory.CreateDirectory(showFolderPath);
                                result.ShowsCreated++;
                                _logger.LogInformation("Created TV show folder: {Path}", showFolderPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing TV show: {Title}", show.Title);
                            result.ShowsErrors++;
                        }
                    }
                }

                if (result.MoviesCreated == 0 && result.ShowsCreated == 0 && result.MoviesErrors == 0 && result.ShowsErrors == 0)
                {
                    _logger.LogWarning(
                        "Import completed but no folders were created. Movies path: {MoviesPath}, TV Shows path: {TvShowsPath}",
                        moviesLibraryPath ?? "null",
                        tvShowsLibraryPath ?? "null");
                }

                result.Success = true;
                _logger.LogInformation(
                    "Import completed successfully. Created {Movies} movie folders, {Shows} TV show folders",
                    result.MoviesCreated,
                    result.ShowsCreated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing plan to watch list: {Message}", ex.Message);
                result.Error = ex.Message;
            }

            return result;
        }

        private static string FormatMovieFolderName(API.Objects.SimklMovie movie)
        {
            var name = SanitizeFolderName(movie.Title ?? "Unknown Movie");

            if (movie.Year.HasValue)
            {
                name = $"{name} ({movie.Year.Value})";
            }

            var tmdbId = movie.Ids?.Tmdb;
            if (!string.IsNullOrEmpty(tmdbId))
            {
                name = $"{name} [tmdbid-{tmdbId}]";
            }

            return name;
        }

        private static string FormatMovieFileName(API.Objects.SimklMovie movie)
        {
            var name = SanitizeFolderName(movie.Title ?? "Unknown Movie");

            if (movie.Year.HasValue)
            {
                name = $"{name} ({movie.Year.Value})";
            }

            var tmdbId = movie.Ids?.Tmdb;
            if (!string.IsNullOrEmpty(tmdbId))
            {
                name = $"{name} [tmdbid-{tmdbId}]";
            }

            return $"{name}.mkv";
        }

        private static string FormatShowFolderName(API.Objects.SimklShow show)
        {
            var name = SanitizeFolderName(show.Title ?? "Unknown Show");

            if (show.Year.HasValue)
            {
                name = $"{name} ({show.Year.Value})";
            }

            var tvdbId = show.Ids?.Tvdb;
            if (!string.IsNullOrEmpty(tvdbId))
            {
                name = $"{name} [tvdbid-{tvdbId}]";
            }

            return name;
        }

        private static string SanitizeFolderName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        public class ImportResult
        {
            public bool Success { get; set; }

            public string? Error { get; set; }

            public string? Message { get; set; }

            public int MoviesCreated { get; set; }

            public int MoviesFilesCopied { get; set; }

            public int MoviesErrors { get; set; }

            public int ShowsCreated { get; set; }

            public int ShowsErrors { get; set; }
        }
    }
}