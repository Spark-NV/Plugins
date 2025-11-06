using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tvdb.Sdk;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using Season = MediaBrowser.Controller.Entities.TV.Season;
using Series = MediaBrowser.Controller.Entities.TV.Series;

namespace Jellyfin.Plugin.Tvdb.Providers
{
    public class TvdbMissingEpisodeProvider : IHostedService
    {
        public static readonly string ProviderName = "Missing Episode Fetcher";

        private readonly TvdbClientManager _tvdbClientManager;
        private readonly IBaseItemManager _baseItemManager;
        private readonly IProviderManager _providerManager;
        private readonly ILocalizationManager _localization;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<TvdbMissingEpisodeProvider> _logger;

        public TvdbMissingEpisodeProvider(
            TvdbClientManager tvdbClientManager,
            IBaseItemManager baseItemManager,
            IProviderManager providerManager,
            ILocalizationManager localization,
            ILibraryManager libraryManager,
            IFileSystem fileSystem,
            ILogger<TvdbMissingEpisodeProvider> logger)
        {
            _tvdbClientManager = tvdbClientManager;
            _baseItemManager = baseItemManager;
            _providerManager = providerManager;
            _localization = localization;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        private static bool IncludeMissingSpecials => TvdbPlugin.Instance?.Configuration.IncludeMissingSpecials ?? false;

        private static bool RemoveAllMissingEpisodesOnRefresh => TvdbPlugin.Instance?.Configuration.RemoveAllMissingEpisodesOnRefresh ?? false;

        private static bool CreateStubFilesForMissingEpisodes => TvdbPlugin.Instance?.Configuration.CreateStubFilesForMissingEpisodes ?? true;

        private static bool EpisodeExists(EpisodeBaseRecord episodeRecord, IReadOnlyList<Episode> existingEpisodes)
        {
            return existingEpisodes.Any(episode => EpisodeEquals(episode, episodeRecord));
        }

        private static bool EpisodeEquals(Episode episode, EpisodeBaseRecord otherEpisodeRecord)
        {
            return otherEpisodeRecord.Number.HasValue
                && episode.ContainsEpisodeNumber(otherEpisodeRecord.Number.Value)
                && episode.ParentIndexNumber == otherEpisodeRecord.SeasonNumber;
        }

        private bool IsEnabledForLibrary(BaseItem item)
        {
            Series? series = item switch
            {
                Episode episode => episode.Series,
                Season season => season.Series,
                _ => item as Series
            };

            if (series == null)
            {
                _logger.LogDebug("Given input is not in {@ValidTypes}: {Type}", new[] { nameof(Series), nameof(Season), nameof(Episode) }, item.GetType());
                return false;
            }

            var libraryOptions = _libraryManager.GetLibraryOptions(series);
            var typeOptions = libraryOptions.GetTypeOptions(series.GetType().Name);
            return _baseItemManager.IsMetadataFetcherEnabled(series, typeOptions, ProviderName);
        }

        private void OnProviderManagerRefreshComplete(object? sender, GenericEventArgs<BaseItem> genericEventArgs)
        {
            if (!IsEnabledForLibrary(genericEventArgs.Argument))
            {
                _logger.LogDebug("{ProviderName} not enabled for {InputName}", ProviderName, genericEventArgs.Argument.Name);
                return;
            }

            _logger.LogDebug("{MethodName}: Try Refreshing for Item {Name} {Type}", nameof(OnProviderManagerRefreshComplete), genericEventArgs.Argument.Name, genericEventArgs.Argument.GetType());
            if (genericEventArgs.Argument is Series series)
            {
                _logger.LogDebug("{MethodName}: Refreshing Series {SeriesName}", nameof(OnProviderManagerRefreshComplete), series.Name);
                HandleSeries(series).GetAwaiter().GetResult();
            }

            if (genericEventArgs.Argument is Season season)
            {
                _logger.LogDebug("{MethodName}: Refreshing {SeriesName} {SeasonName}", nameof(OnProviderManagerRefreshComplete), season.Series?.Name, season.Name);
                HandleSeason(season).GetAwaiter().GetResult();
            }
        }

        private async Task HandleSeries(Series series)
        {
            if (!series.HasTvdbId())
            {
                _logger.LogDebug("No TVDB Id available.");
                return;
            }

            var tvdbId = series.GetTvdbId();

            var children = series.Children.ToList();
            var existingSeasons = new List<Season>();

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is Season season && child.IndexNumber.HasValue)
                {
                    existingSeasons.Add(season);
                }
            }

            var allEpisodes = RemoveAllMissingEpisodesOnRefresh
                ? Array.Empty<EpisodeBaseRecord>()
                : await GetAllEpisodes(tvdbId, series.DisplayOrder, series.GetPreferredMetadataLanguage()).ConfigureAwait(false);

            if (!IncludeMissingSpecials)
            {
                allEpisodes = allEpisodes.Where(e => e.SeasonNumber != 0).ToList();
            }

            var allSeasons = allEpisodes
                .Where(ep => ep.SeasonNumber.HasValue)
                .Select(ep => ep.SeasonNumber!.Value)
                .Distinct()
                .ToList();

            if (CreateStubFilesForMissingEpisodes && !string.IsNullOrEmpty(series.Path))
            {
                await CreateStubFilesForAllEpisodesAsync(series, allEpisodes, existingSeasons, allSeasons, CancellationToken.None).ConfigureAwait(false);
            }

            var newSeasons = AddMissingSeasons(series, existingSeasons, allSeasons);

            existingSeasons.AddRange(newSeasons);

            foreach (var newSeason in existingSeasons)
            {
                await HandleSeason(newSeason, allEpisodes).ConfigureAwait(false);
            }

            var orphanedSeasons = existingSeasons
                .Where(season => !allSeasons.Contains(season.IndexNumber!.Value) && season.GetEpisodes().Count == 0)
                .ToList();

            DeleteVirtualItems(orphanedSeasons);
        }

        private async Task HandleSeason(Season season, IReadOnlyList<EpisodeBaseRecord>? allEpisodesRemote = null)
        {
            var series = season.Series;
            if (!series.HasTvdbId())
            {
                _logger.LogDebug("No TVDB Id available.");
                return;
            }

            var tvdbId = series.GetTvdbId();
            var allEpisodes = RemoveAllMissingEpisodesOnRefresh
                ? Array.Empty<EpisodeBaseRecord>()
                : allEpisodesRemote ??
                await GetAllEpisodes(tvdbId, series.DisplayOrder, season.GetPreferredMetadataLanguage())
                    .ConfigureAwait(false);
            if (allEpisodesRemote is null && !IncludeMissingSpecials)
            {
                allEpisodes = allEpisodes.Where(e => e.SeasonNumber != 0).ToList();
            }

            var seasonEpisodes = allEpisodes.Where(e => e.SeasonNumber == season.IndexNumber).ToList();
            var existingEpisodes = season.GetEpisodes().OfType<Episode>().ToHashSet();

            foreach (var episodeRecord in seasonEpisodes)
            {
                var foundEpisodes = existingEpisodes.Where(episode => EpisodeEquals(episode, episodeRecord)).ToList();
                if (foundEpisodes.Count != 0)
                {
                    var physicalEpisodes = foundEpisodes.Where(e => !e.IsVirtualItem);
                    if (physicalEpisodes.Any())
                    {
                        var virtualEpisodes = foundEpisodes.Where(e => e.IsVirtualItem).ToList();
                        DeleteVirtualItems(virtualEpisodes);
                        existingEpisodes.ExceptWith(virtualEpisodes);
                    }

                    continue;
                }

                AddVirtualEpisode(episodeRecord, season);
            }

            var orphanedEpisodes = existingEpisodes
                .Where(e => e.ParentIndexNumber == season.IndexNumber)
                .Where(e => e.IsVirtualItem)
                .Where(e => !seasonEpisodes.Any(episodeRecord => EpisodeEquals(e, episodeRecord)))
                .ToList();
            DeleteVirtualItems(orphanedEpisodes);
        }

        private void OnLibraryManagerItemUpdated(object? sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            _logger.LogDebug("{MethodName}: Refreshing Item {ItemName} [{Reason}]", nameof(OnLibraryManagerItemUpdated), itemChangeEventArgs.Item.Name, itemChangeEventArgs.UpdateReason);
            if (itemChangeEventArgs.Item.IsVirtualItem
                || !(itemChangeEventArgs.Item is Season || itemChangeEventArgs.Item is Episode))
            {
                _logger.LogDebug("Skip: Updated item is {ItemType}.", itemChangeEventArgs.Item.IsVirtualItem ? "Virtual" : "no Season or Episode");
                return;
            }

            if (!IsEnabledForLibrary(itemChangeEventArgs.Item))
            {
                _logger.LogDebug("{ProviderName} not enabled for {InputName}", ProviderName, itemChangeEventArgs.Item.Name);
                return;
            }

            var existingVirtualItems = GetVirtualItems(itemChangeEventArgs.Item, itemChangeEventArgs.Parent);
            DeleteVirtualItems(existingVirtualItems);
        }

        private IReadOnlyList<BaseItem> GetVirtualItems(BaseItem item, BaseItem? parent)
        {
            var query = new InternalItemsQuery
            {
                IsVirtualItem = true,
                IndexNumber = item.IndexNumber,
                ParentIndexNumber = item is Episode ? item.ParentIndexNumber : null,
                IncludeItemTypes = new[] { item.GetBaseItemKind() },
                Parent = parent,
                Recursive = true,
                GroupByPresentationUniqueKey = false,
                DtoOptions = new DtoOptions(true)
            };

            var existingVirtualItems = _libraryManager.GetItemList(query);
            return existingVirtualItems;
        }

        private void DeleteVirtualItems<T>(IReadOnlyList<T> existingVirtualItems)
            where T : BaseItem
        {
            var deleteOptions = new DeleteOptions
            {
                DeleteFileLocation = false
            };

            for (var i = 0; i < existingVirtualItems.Count; i++)
            {
                var currentItem = existingVirtualItems[i];
                _logger.LogDebug("Delete VirtualItem {Name} - S{Season:00}E{Episode:00}", currentItem.Name, currentItem.ParentIndexNumber, currentItem.IndexNumber);
                _libraryManager.DeleteItem(currentItem, deleteOptions);
            }
        }

        private void OnLibraryManagerItemRemoved(object? sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            _logger.LogDebug("{MethodName}: Refreshing {ItemName} [{Reason}]", nameof(OnLibraryManagerItemRemoved), itemChangeEventArgs.Item.Name, itemChangeEventArgs.UpdateReason);
            if (itemChangeEventArgs.Item.IsVirtualItem || !IsEnabledForLibrary(itemChangeEventArgs.Item))
            {
                _logger.LogDebug("Skip: {Message}.", itemChangeEventArgs.Item.IsVirtualItem ? "Updated item is Virtual" : "Update not enabled");
                return;
            }

            if (itemChangeEventArgs.Item is Season season)
            {
                var newSeason = AddVirtualSeason(season.IndexNumber!.Value, season.Series);
                HandleSeason(newSeason).GetAwaiter().GetResult();
            }
            else if (itemChangeEventArgs.Item is Episode episode)
            {
                if (!episode.Series.HasTvdbId())
                {
                    _logger.LogDebug("No TVDB Id available.");
                    return;
                }

                var tvdbId = episode.Series.GetTvdbId();
                var displayOrder = episode.Series.DisplayOrder;

                var episodeRecords = GetAllEpisodes(tvdbId, displayOrder, episode.GetPreferredMetadataLanguage()).GetAwaiter().GetResult();

                EpisodeBaseRecord? episodeRecord = null;
                if (episodeRecords.Count > 0)
                {
                    episodeRecord = episodeRecords.FirstOrDefault(e => EpisodeEquals(episode, e));
                }

                AddVirtualEpisode(episodeRecord, episode.Season);
            }
        }

        private async Task<IReadOnlyList<EpisodeBaseRecord>> GetAllEpisodes(int tvdbId, string displayOrder, string acceptedLanguage)
        {
            try
            {
                if (string.IsNullOrEmpty(displayOrder))
                {
                    displayOrder = "official";
                }

                var seriesInfo = await _tvdbClientManager.GetSeriesEpisodesAsync(tvdbId, acceptedLanguage, displayOrder, CancellationToken.None).ConfigureAwait(false);
                var allEpisodes = seriesInfo.Episodes;
                if (allEpisodes is null || !allEpisodes.Any())
                {
                    _logger.LogWarning("Unable to get episodes from TVDB: Episode Query returned null for TVDB Id: {TvdbId}", tvdbId);
                    return Array.Empty<EpisodeBaseRecord>();
                }

                _logger.LogDebug("{MethodName}: For TVDB Id '{TvdbId}' found #{Count} [{Episodes}]", nameof(GetAllEpisodes), tvdbId, allEpisodes.Count, string.Join(", ", allEpisodes.Select(e => $"S{e.SeasonNumber}E{e.Number}")));
                return allEpisodes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to get episodes from TVDB for Id '{TvdbId}'", tvdbId);
                return Array.Empty<EpisodeBaseRecord>();
            }
        }

        private IEnumerable<Season> AddMissingSeasons(Series series, List<Season> existingSeasons, IReadOnlyList<int> allSeasons)
        {
            var missingSeasons = allSeasons.Except(existingSeasons.Select(s => s.IndexNumber!.Value)).ToList();
            for (var i = 0; i < missingSeasons.Count; i++)
            {
                var season = missingSeasons[i];
                yield return AddVirtualSeason(season, series);
            }
        }

        private void AddMissingEpisodes(
            Dictionary<int, List<Episode>> existingEpisodes,
            IReadOnlyList<EpisodeBaseRecord> allEpisodeRecords,
            IReadOnlyList<Season> existingSeasons)
        {
            for (var i = 0; i < allEpisodeRecords.Count; i++)
            {
                var episodeRecord = allEpisodeRecords[i];

                if (episodeRecord.SeasonNumber.HasValue
                    && existingEpisodes.TryGetValue(episodeRecord.SeasonNumber.Value, out var episodes)
                    && EpisodeExists(episodeRecord, episodes))
                {
                    _logger.LogDebug("{MethodName}: Skip, already existing S{Season:00}E{Episode:00}", nameof(AddMissingEpisodes), episodeRecord.SeasonNumber, episodeRecord.Number);
                    continue;
                }

                var existingSeason = existingSeasons.First(season => season.IndexNumber.HasValue && season.IndexNumber.Value == episodeRecord.SeasonNumber);

                AddVirtualEpisode(episodeRecord, existingSeason);
            }
        }

        private Season AddVirtualSeason(int season, Series series)
        {
            string seasonName;
            if (season == 0)
            {
                seasonName = _libraryManager.GetLibraryOptions(series).SeasonZeroDisplayName;
            }
            else
            {
                seasonName = string.Format(
                    CultureInfo.InvariantCulture,
                    _localization.GetLocalizedString("NameSeasonNumber"),
                    season.ToString(CultureInfo.InvariantCulture));
            }

            _logger.LogDebug("Creating Season {SeasonName} entry for {SeriesName}", seasonName, series.Name);

            var newSeason = new Season
            {
                Name = seasonName,
                IndexNumber = season,
                Id = _libraryManager.GetNewItemId(
                    series.Id + season.ToString(CultureInfo.InvariantCulture) + seasonName,
                    typeof(Season)),
                IsVirtualItem = true,
                SeriesId = series.Id,
                SeriesName = series.Name,
                SeriesPresentationUniqueKey = series.GetPresentationUniqueKey()
            };

            series.AddChild(newSeason);
            _providerManager.QueueRefresh(newSeason.Id, new MetadataRefreshOptions(new DirectoryService(_fileSystem)), RefreshPriority.High);

            return newSeason;
        }

        private void AddVirtualEpisode(EpisodeBaseRecord? episode, Season? season)
        {
            if (episode?.SeasonNumber == null || season == null)
            {
                return;
            }

            string? stubFilePath = null;
            if (CreateStubFilesForMissingEpisodes && !string.IsNullOrEmpty(season.Series.Path))
            {
                stubFilePath = GetStubFilePath(episode, season);
                if (!string.IsNullOrEmpty(stubFilePath) && !File.Exists(stubFilePath))
                {
                    stubFilePath = null;
                }
            }

            var newEpisode = new Episode
            {
                Name = episode.Name,
                IndexNumber = episode.Number,
                ParentIndexNumber = episode.SeasonNumber,
                Id = _libraryManager.GetNewItemId(
                    $"{season.Series.Id}{episode.SeasonNumber}Episode {episode.Number}",
                    typeof(Episode)),
                IsVirtualItem = string.IsNullOrEmpty(stubFilePath), // Only virtual if no stub file exists
                Path = stubFilePath,
                SeasonId = season.Id,
                SeriesId = season.Series.Id,
                Overview = episode.Overview,
                SeriesName = season.Series.Name,
                SeriesPresentationUniqueKey = season.SeriesPresentationUniqueKey,
                SeasonName = season.Name,
                DateLastSaved = DateTime.UtcNow
            };

            if (string.IsNullOrEmpty(season.Series.DisplayOrder))
            {
                newEpisode.AirsBeforeEpisodeNumber = episode.AirsBeforeEpisode;
                newEpisode.AirsAfterSeasonNumber = episode.AirsAfterSeason;
                newEpisode.AirsBeforeSeasonNumber = episode.AirsBeforeSeason;
            }

            if (DateTime.TryParse(episode!.Aired, out var premiereDate))
            {
                newEpisode.PremiereDate = premiereDate;
            }

            newEpisode.PresentationUniqueKey = newEpisode.GetPresentationUniqueKey();
            newEpisode.SetTvdbId(episode.Id);

            _logger.LogDebug(
                "Creating {Type} episode {SeriesName} S{Season:00}E{Episode:00}",
                newEpisode.IsVirtualItem ? "virtual" : "stub file",
                season.Series.Name,
                episode.SeasonNumber,
                episode.Number);

            season.AddChild(newEpisode);
            _providerManager.QueueRefresh(newEpisode.Id, new MetadataRefreshOptions(new DirectoryService(_fileSystem)), RefreshPriority.High);
        }

        private string? GetEpisodeStubFilePath()
        {
            var config = TvdbPlugin.Instance?.Configuration;
            if (config == null)
            {
                return null;
            }

            string? stubPath = null;

            if (!string.IsNullOrEmpty(config.EpisodeStubFilePath))
            {
                if (File.Exists(config.EpisodeStubFilePath))
                {
                    stubPath = config.EpisodeStubFilePath;
                    _logger.LogDebug("Using configured episode stub file: {Path}", stubPath);
                }
                else
                {
                    _logger.LogWarning("Configured episode stub file not found: {Path}", config.EpisodeStubFilePath);
                }
            }

            if (stubPath == null)
            {
                var pluginPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

                if (pluginPath != null)
                {
                    var path1 = Path.Combine(pluginPath, "episode_stub.mp4");
                    if (File.Exists(path1))
                    {
                        stubPath = path1;
                    }
                    else
                    {
                        var path2 = Path.Combine(pluginPath, "..", "..", "..", "..", "episode_stub.mp4");
                        path2 = Path.GetFullPath(path2);
                        if (File.Exists(path2))
                        {
                            stubPath = path2;
                        }
                    }
                }

                if (stubPath == null)
                {
                    var path3 = Path.Combine(Directory.GetCurrentDirectory(), "episode_stub.mp4");
                    if (File.Exists(path3))
                    {
                        stubPath = path3;
                    }
                }

                if (stubPath == null || !File.Exists(stubPath))
                {
                    _logger.LogWarning("Episode stub file not found. Searched in plugin directory and current working directory.");
                }
            }

            return stubPath;
        }

        private async Task CreateStubFilesForAllEpisodesAsync(
            Series series,
            IReadOnlyList<EpisodeBaseRecord> allEpisodes,
            IReadOnlyList<Season> existingSeasons,
            IReadOnlyList<int> allSeasons,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("Creating stub files for all missing episodes in series {SeriesName}", series.Name);

            var stubFilePath = GetEpisodeStubFilePath();
            if (stubFilePath == null || !File.Exists(stubFilePath))
            {
                _logger.LogWarning("Cannot create stub files: episode stub file not found. Skipping stub file creation for series {SeriesName}", series.Name);
                return;
            }

            foreach (var seasonNumber in allSeasons)
            {
                var seasonEpisodes = allEpisodes
                    .Where(e => e.SeasonNumber == seasonNumber && e.Number.HasValue)
                    .ToList();

                var existingSeason = existingSeasons.FirstOrDefault(s => s.IndexNumber == seasonNumber);

                var existingEpisodeFiles = new HashSet<string>();
                if (existingSeason != null)
                {
                    foreach (var existingEpisode in existingSeason.GetEpisodes().OfType<Episode>())
                    {
                        if (!string.IsNullOrEmpty(existingEpisode.Path) && File.Exists(existingEpisode.Path))
                        {
                            existingEpisodeFiles.Add(existingEpisode.Path);
                        }
                    }
                }

                string seasonDirName;
                if (seasonNumber == 0)
                {
                    var libraryOptions = _libraryManager.GetLibraryOptions(series);
                    seasonDirName = libraryOptions.SeasonZeroDisplayName ?? "Specials";
                }
                else
                {
                    seasonDirName = string.Format(CultureInfo.InvariantCulture, _localization.GetLocalizedString("NameSeasonNumber"), seasonNumber.ToString(CultureInfo.InvariantCulture));
                }

                var seasonPath = Path.Combine(series.Path, seasonDirName);

                try
                {
                    await Task.Run(() => Directory.CreateDirectory(seasonPath), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create season directory: {SeasonPath}", seasonPath);
                    continue;
                }

                foreach (var episodeRecord in seasonEpisodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!episodeRecord.Number.HasValue)
                    {
                        continue;
                    }

                    var episodeNumber = episodeRecord.Number.Value;
                    var episodeName = episodeRecord.Name ?? "Episode";
                    var sanitizedEpisodeName = SanitizeFileName(episodeName);
                    var fileName = $"S{seasonNumber:D2}E{episodeNumber:D2} - {sanitizedEpisodeName}.mp4";
                    var filePath = Path.Combine(seasonPath, fileName);

                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length > 1024)
                        {
                            continue;
                        }
                    }

                    if (existingEpisodeFiles.Contains(filePath))
                    {
                        continue;
                    }

                    try
                    {
                        if (File.Exists(stubFilePath))
                        {
                            await Task.Run(() => File.Copy(stubFilePath, filePath, overwrite: true), cancellationToken).ConfigureAwait(false);
                            _logger.LogDebug("Copied stub file to: {FilePath}", filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to copy stub file to: {FilePath}", filePath);
                    }
                }
            }

            _logger.LogDebug("Finished creating stub files for series {SeriesName}", series.Name);
        }

        private string? GetStubFilePath(EpisodeBaseRecord episodeRecord, Season season)
        {
            var series = season.Series;
            if (series == null || string.IsNullOrEmpty(series.Path) || !episodeRecord.Number.HasValue || !episodeRecord.SeasonNumber.HasValue)
            {
                return null;
            }

            var seasonNumber = episodeRecord.SeasonNumber.Value;
            var episodeNumber = episodeRecord.Number.Value;

            string seasonDirName;
            if (seasonNumber == 0)
            {
                var libraryOptions = _libraryManager.GetLibraryOptions(series);
                seasonDirName = libraryOptions.SeasonZeroDisplayName ?? "Specials";
            }
            else
            {
                seasonDirName = string.Format(CultureInfo.InvariantCulture, _localization.GetLocalizedString("NameSeasonNumber"), seasonNumber.ToString(CultureInfo.InvariantCulture));
            }

            var episodeName = episodeRecord.Name ?? "Episode";
            var sanitizedEpisodeName = SanitizeFileName(episodeName);
            var fileName = $"S{seasonNumber:D2}E{episodeNumber:D2} - {sanitizedEpisodeName}.mp4";
            var filePath = Path.Combine(series.Path, seasonDirName, fileName);

            return filePath;
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "Episode";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder(fileName.Length);

            foreach (var c in fileName)
            {
                if (!invalidChars.Contains(c))
                {
                    sanitized.Append(c);
                }
                else
                {
                    sanitized.Append('_');
                }
            }

            var result = sanitized.ToString().TrimEnd('.', ' ');

            if (string.IsNullOrWhiteSpace(result))
            {
                return "Episode";
            }

            return result;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _providerManager.RefreshCompleted += OnProviderManagerRefreshComplete;
            _libraryManager.ItemUpdated += OnLibraryManagerItemUpdated;
            _libraryManager.ItemRemoved += OnLibraryManagerItemRemoved;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _providerManager.RefreshCompleted -= OnProviderManagerRefreshComplete;
            _libraryManager.ItemUpdated -= OnLibraryManagerItemUpdated;
            _libraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
            return Task.CompletedTask;
        }
    }
}
