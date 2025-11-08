#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Providers;
using Jellyfin.Plugin.AutoCollections.Configuration;

namespace Jellyfin.Plugin.AutoCollections
{
    internal enum SortOrder
    {
        Ascending,
        Descending
    }
    
    public class AutoCollectionsManager : IDisposable
    {
        private readonly ICollectionManager _collectionManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IUserDataManager? _userDataManager;
        private readonly Timer _timer;
        private readonly ILogger<AutoCollectionsManager> _logger;
        private readonly string _pluginDirectory;

        public AutoCollectionsManager(IProviderManager providerManager, ICollectionManager collectionManager, ILibraryManager libraryManager, IUserDataManager userDataManager, ILogger<AutoCollectionsManager> logger, IApplicationPaths applicationPaths)
        {
            _providerManager = providerManager;
            _collectionManager = collectionManager;
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
            _logger = logger;
            _timer = new Timer(_ => OnTimerElapsed(), null, Timeout.Infinite, Timeout.Infinite);
            _pluginDirectory = Path.Combine(applicationPaths.DataPath, "Autocollections");
            Directory.CreateDirectory(_pluginDirectory);
        }

        public AutoCollectionsManager(IProviderManager providerManager, ICollectionManager collectionManager, ILibraryManager libraryManager, ILogger<AutoCollectionsManager> logger, IApplicationPaths applicationPaths)
        {
            _providerManager = providerManager;
            _collectionManager = collectionManager;
            _libraryManager = libraryManager;
            _userDataManager = null;
            _logger = logger;
            _timer = new Timer(_ => OnTimerElapsed(), null, Timeout.Infinite, Timeout.Infinite);
            _pluginDirectory = Path.Combine(applicationPaths.DataPath, "Autocollections");
            Directory.CreateDirectory(_pluginDirectory);
        }

        private IEnumerable<Series> GetSeriesFromLibrary(string term, Person? specificPerson = null)
        {
            IEnumerable<Series> results = Enumerable.Empty<Series>();
            
            if (specificPerson == null)
            {
                var byTags = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Series },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasTvdbId = true,
                    Tags = [term]
                }).OfType<Series>();

                var byGenres = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Series },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasTvdbId = true,
                    Genres = [term]
                }).OfType<Series>();
                
                results = byTags.Union(byGenres);
            }
            else
            {
                var personName = specificPerson.Name;
                
                var byActors = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Series },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasTvdbId = true,
                    Person = personName,
                    PersonTypes = new[] { "Actor" }
                }).OfType<Series>();

                var byDirectors = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Series },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasTvdbId = true,
                    Person = personName,
                    PersonTypes = new[] { "Director" }
                }).OfType<Series>();
                
                results = byActors.Union(byDirectors);
            }

            return results;
        }
        
        private IEnumerable<Series> GetSeriesFromLibraryWithAndMatching(string[] terms, Person? specificPerson = null)
        {
            if (terms.Length == 0)
                return Enumerable.Empty<Series>();
                
            var results = GetSeriesFromLibrary(terms[0], specificPerson).ToList();
            
            for (int i = 1; i < terms.Length && results.Any(); i++)
            {
                var matchingItems = GetSeriesFromLibrary(terms[i], specificPerson).ToList();
                results = results.Where(item => matchingItems.Any(m => m.Id == item.Id)).ToList();
            }
            
            return results;
        }

        private IEnumerable<Movie> GetMoviesFromLibrary(string term, Person? specificPerson = null)
        {
            IEnumerable<Movie> results = Enumerable.Empty<Movie>();
            
            if (specificPerson == null)
            {
                var byTagsImdb = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasImdbId = true,
                    Tags = [term]
                }).OfType<Movie>();

                var byTagsTmdb = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasTmdbId = true,
                    Tags = [term]
                }).OfType<Movie>();

                var byTags = byTagsImdb.Union(byTagsTmdb);

                var byGenresImdb = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasImdbId = true,
                    Genres = [term]
                }).OfType<Movie>();

                var byGenresTmdb = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasTmdbId = true,
                    Genres = [term]
                }).OfType<Movie>();
                
                var byGenres = byGenresImdb.Union(byGenresTmdb);
                
                results = byTags.Union(byGenres);
            }
            else
            {
                var personName = specificPerson.Name;
                
                var byActorsImdb = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasImdbId = true,
                    Person = personName,
                    PersonTypes = new[] { "Actor" }
                }).OfType<Movie>();

                var byActorsTmdb = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasTmdbId = true,
                    Person = personName,
                    PersonTypes = new[] { "Actor" }
                }).OfType<Movie>();

                var byActors = byActorsImdb.Union(byActorsTmdb);

                var byDirectorsImdb = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasImdbId = true,
                    Person = personName,
                    PersonTypes = new[] { "Director" }
                }).OfType<Movie>();

                var byDirectorsTmdb = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true,
                    HasTmdbId = true,
                    Person = personName,
                    PersonTypes = new[] { "Director" }
                }).OfType<Movie>();
                
                var byDirectors = byDirectorsImdb.Union(byDirectorsTmdb);
                
                results = byActors.Union(byDirectors);
            }

            return results;
        }
        
        private IEnumerable<Movie> GetMoviesFromLibraryWithAndMatching(string[] terms, Person? specificPerson = null)
        {
            if (terms.Length == 0)
                return Enumerable.Empty<Movie>();
                
            var results = GetMoviesFromLibrary(terms[0], specificPerson).ToList();
            
            for (int i = 1; i < terms.Length && results.Any(); i++)
            {
                var matchingItems = GetMoviesFromLibrary(terms[i], specificPerson).ToList();
                results = results.Where(item => matchingItems.Any(m => m.Id == item.Id)).ToList();
            }
            
            return results;
        }        
        
        private IEnumerable<Movie> GetMoviesFromLibraryByMatch(string matchString, bool caseSensitive, Configuration.MatchType matchType)
        {
            var allMovies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Movie>();
            
            StringComparison comparison = caseSensitive 
                ? StringComparison.Ordinal 
                : StringComparison.OrdinalIgnoreCase;
            
            return matchType switch
            {
                Configuration.MatchType.Title => allMovies.Where(movie => 
                    !string.IsNullOrEmpty(movie.Name) && movie.Name.Contains(matchString, comparison)),
                
                Configuration.MatchType.Genre => allMovies.Where(movie => 
                    movie.Genres != null && movie.Genres.Any(genre => 
                        !string.IsNullOrEmpty(genre) && genre.Contains(matchString, comparison))),
                
                Configuration.MatchType.Studio => allMovies.Where(movie => 
                    movie.Studios != null && movie.Studios.Any(studio => 
                        !string.IsNullOrEmpty(studio) && studio.Contains(matchString, comparison))),
                
                Configuration.MatchType.Actor => GetMoviesWithPerson(matchString, "Actor", caseSensitive),
                
                Configuration.MatchType.Director => GetMoviesWithPerson(matchString, "Director", caseSensitive),
                
                _ => allMovies.Where(movie => 
                    !string.IsNullOrEmpty(movie.Name) && movie.Name.Contains(matchString, comparison))
            };
        }
          private IEnumerable<Series> GetSeriesFromLibraryByMatch(string matchString, bool caseSensitive, Configuration.MatchType matchType)
                {
                    var allSeries = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Series },
                        IsVirtualItem = false,
                        Recursive = true
                    }).OfType<Series>();
                    
                    StringComparison comparison = caseSensitive 
                        ? StringComparison.Ordinal 
                        : StringComparison.OrdinalIgnoreCase;
                    return matchType switch
                    {
                        Configuration.MatchType.Title => allSeries.Where(series => 
                            series.Name != null && series.Name.Contains(matchString, comparison)),
                        
                        Configuration.MatchType.Genre => allSeries.Where(series => 
                            series.Genres != null && series.Genres.Any(genre => 
                                genre.Contains(matchString, comparison))),
                        
                        Configuration.MatchType.Studio => allSeries.Where(series => 
                            series.Studios != null && series.Studios.Any(studio => 
                                studio.Contains(matchString, comparison))),
                        
                        Configuration.MatchType.Actor => _libraryManager.GetItemList(new InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { BaseItemKind.Series },
                            IsVirtualItem = false,
                            Recursive = true,
                            Person = matchString,
                            PersonTypes = new[] { "Actor" }
                        }).OfType<Series>(),
                        
                        Configuration.MatchType.Director => _libraryManager.GetItemList(new InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { BaseItemKind.Series },
                            IsVirtualItem = false,
                            Recursive = true,
                            Person = matchString,
                            PersonTypes = new[] { "Director" }
                        }).OfType<Series>(),
                        
                        _ => allSeries.Where(series => 
                            series.Name != null && series.Name.Contains(matchString, comparison))
                    };
                }
        private IEnumerable<Movie> GetMoviesFromLibraryByTitleMatch(string titleMatch, bool caseSensitive)
        {
            return GetMoviesFromLibraryByMatch(titleMatch, caseSensitive, Configuration.MatchType.Title);
        }
        
        private IEnumerable<Series> GetSeriesFromLibraryByTitleMatch(string titleMatch, bool caseSensitive)
        {
            return GetSeriesFromLibraryByMatch(titleMatch, caseSensitive, Configuration.MatchType.Title);
        }

        private async Task RemoveUnwantedMediaItems(BoxSet collection, IEnumerable<BaseItem> wantedMediaItems)
        {
            var wantedItemIds = wantedMediaItems.Select(item => item.Id).ToHashSet();

            var currentChildren = collection.GetLinkedChildren().ToList();
            var childrenToRemove = currentChildren
                .Where(item => !wantedItemIds.Contains(item.Id))
                .ToList();

            if (childrenToRemove.Count > 0)
            {
                _logger.LogDebug("Removing {Count} items from collection '{CollectionName}':", 
                    childrenToRemove.Count, collection.Name);
                
                foreach (var item in childrenToRemove)
                {
                    _logger.LogDebug("  - Removing: '{Title}' (ID: {Id}) - no longer matches criteria", 
                        item.Name, item.Id);
                }
                
                await _collectionManager.RemoveFromCollectionAsync(
                    collection.Id, 
                    childrenToRemove.Select(i => i.Id).ToArray()
                ).ConfigureAwait(true);
            }
            else
            {
                _logger.LogDebug("No items to remove from collection '{CollectionName}'", collection.Name);
            }
        }

        private async Task AddWantedMediaItems(BoxSet collection, IEnumerable<BaseItem> wantedMediaItems)
        {
            var existingItemIds = collection.GetLinkedChildren()
                .Select(item => item.Id)
                .ToHashSet();            

            var itemsToAdd = wantedMediaItems
                .Where(item => !existingItemIds.Contains(item.Id))
                .OrderByDescending(item => item.ProductionYear)
                .ThenByDescending(item => item.PremiereDate ?? DateTime.MinValue)
                .ToList();

            if (itemsToAdd.Count > 0)
            {
                _logger.LogDebug("Adding {Count} new items to collection '{CollectionName}':", 
                    itemsToAdd.Count, collection.Name);
                
                foreach (var item in itemsToAdd)
                {
                    var itemType = item is Movie ? "Movie" : item is Series ? "Series" : "Item";
                    var year = item.ProductionYear?.ToString() ?? "Unknown year";
                    _logger.LogDebug("  + Adding {Type}: '{Title}' ({Year}) (ID: {Id})", 
                        itemType, item.Name, year, item.Id);
                }
                
                await _collectionManager.AddToCollectionAsync(
                    collection.Id, 
                    itemsToAdd.Select(i => i.Id).ToArray()
                ).ConfigureAwait(true);
            }
            else
            {
                _logger.LogDebug("No new items to add to collection '{CollectionName}' - all matching items already present", 
                    collection.Name);
            }
        }

        private async Task SortCollectionBy(BoxSet collection, SortOrder sortOrder)
        {
            var currentItems = collection.GetLinkedChildren().ToList();

            if (currentItems.Count <= 1)
            {
                return;
            }

            var sortedItems =
                sortOrder == SortOrder.Ascending
                    ? currentItems
                        .OrderBy(item => item.ProductionYear)
                        .ThenBy(item => item.PremiereDate ?? DateTime.MinValue)
                        .ToList()
                    : currentItems
                        .OrderByDescending(item => item.ProductionYear)
                        .ThenByDescending(item => item.PremiereDate ?? DateTime.MinValue)
                        .ToList();

            int firstDifferenceIndex = -1;
            for (int i = 0; i < currentItems.Count; i++)
            {
                if (currentItems[i].Id != sortedItems[i].Id)
                {
                    firstDifferenceIndex = i;
                    break;
                }
            }

            if (firstDifferenceIndex == -1)
            {
                _logger.LogDebug($"Collection {collection.Name} is already sorted");
                return;
            }

            var itemsToRemove = currentItems
                .Skip(firstDifferenceIndex)
                .Select(item => item.Id)
                .ToArray();

            if (itemsToRemove.Length > 0)
            {
                _logger.LogInformation(
                    $"Removing {itemsToRemove.Length} items from collection {collection.Name} for re-sorting"
                );
                await _collectionManager
                    .RemoveFromCollectionAsync(collection.Id, itemsToRemove)
                    .ConfigureAwait(true);
            }

            var itemsToAdd = sortedItems
                .Skip(firstDifferenceIndex)
                .Select(item => item.Id)
                .ToArray();

            if (itemsToAdd.Length > 0)
            {
                _logger.LogInformation(
                    $"Adding {itemsToAdd.Length} sorted items back to collection {collection.Name}"
                );
                await _collectionManager
                    .AddToCollectionAsync(collection.Id, itemsToAdd)
                    .ConfigureAwait(true);
            }
        }

        private void ValidateCollectionContent(BoxSet collection, IEnumerable<BaseItem> expectedItems)
        {
            var actualItems = collection.GetLinkedChildren().ToList();
            var expectedItemsList = expectedItems.ToList();
            
            var actualItemIds = actualItems.Select(i => i.Id).ToHashSet();
            var expectedItemIds = expectedItemsList.Select(i => i.Id).ToHashSet();
            
            var expectedCount = expectedItemIds.Count;
            var actualCount = actualItemIds.Count;
            var matchingCount = actualItemIds.Intersect(expectedItemIds).Count();
            var missingCount = expectedItemIds.Except(actualItemIds).Count();
            var extraCount = actualItemIds.Except(expectedItemIds).Count();
            
            _logger.LogInformation(
                "Collection '{CollectionName}' validation: Expected={Expected}, Actual={Actual}, Matching={Matching}, Missing={Missing}, Extra={Extra}",
                collection.Name, expectedCount, actualCount, matchingCount, missingCount, extraCount);
            
            if (missingCount > 0)
            {
                _logger.LogWarning("Collection '{CollectionName}' is missing {Count} expected items:", 
                    collection.Name, missingCount);
                
                var missingItems = expectedItemsList.Where(i => !actualItemIds.Contains(i.Id)).Take(10);
                foreach (var item in missingItems)
                {
                    var itemType = item is Movie ? "Movie" : item is Series ? "Series" : "Item";
                    var year = item.ProductionYear?.ToString() ?? "Unknown";
                    _logger.LogWarning("  - Missing {Type}: '{Title}' ({Year}) (ID: {Id})", 
                        itemType, item.Name, year, item.Id);
                }
                
                if (missingCount > 10)
                {
                    _logger.LogWarning("  ... and {Count} more missing items", missingCount - 10);
                }
            }
            
            if (extraCount > 0)
            {
                _logger.LogWarning("Collection '{CollectionName}' has {Count} unexpected items:", 
                    collection.Name, extraCount);
                
                var extraItems = actualItems.Where(i => !expectedItemIds.Contains(i.Id)).Take(10);
                foreach (var item in extraItems)
                {
                    var itemType = item is Movie ? "Movie" : item is Series ? "Series" : "Item";
                    var year = item.ProductionYear?.ToString() ?? "Unknown";
                    _logger.LogWarning("  - Extra {Type}: '{Title}' ({Year}) (ID: {Id})", 
                        itemType, item.Name, year, item.Id);
                }
                
                if (extraCount > 10)
                {
                    _logger.LogWarning("  ... and {Count} more extra items", extraCount - 10);
                }
            }
            
            if (missingCount == 0 && extraCount == 0 && actualCount == expectedCount)
            {
                _logger.LogInformation("✓ Collection '{CollectionName}' content validated successfully - all {Count} items match",
                    collection.Name, actualCount);
            }
            else
            {
                _logger.LogWarning("✗ Collection '{CollectionName}' content validation failed - discrepancies found",
                    collection.Name);
            }
        }

        private BoxSet? GetBoxSetByName(string name)
        {
            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                CollapseBoxSetItems = false,
                Recursive = true,
                Tags = new[] { "Autocollection" },
                Name = name,
            }).Select(b => b as BoxSet).FirstOrDefault();
        }

        public async Task ExecuteAutoCollectionsNoProgress()
        {
            _logger.LogInformation("Performing ExecuteAutoCollections");
            
            var titleMatchPairs = Plugin.Instance!.Configuration.TitleMatchPairs;
            _logger.LogInformation($"Starting execution of Auto collections for {titleMatchPairs.Count} title match pairs");

            foreach (var titleMatchPair in titleMatchPairs)
            {
                try
                {
                    _logger.LogInformation($"Processing Auto collection for title match: {titleMatchPair.TitleMatch}");
                    await ExecuteAutoCollectionsForTitleMatchPair(titleMatchPair);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing Auto collection for title match: {titleMatchPair.TitleMatch}");
                    continue;
                }
            }
            
            var expressionCollections = Plugin.Instance!.Configuration.ExpressionCollections;
            _logger.LogInformation($"Starting execution of Advanced collections for {expressionCollections.Count} expression collections");

            foreach (var expressionCollection in expressionCollections)
            {
                try
                {
                    _logger.LogInformation($"Processing Advanced collection: {expressionCollection.CollectionName}");
                    await ExecuteAutoCollectionsForExpressionCollection(expressionCollection);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing Advanced collection: {expressionCollection.CollectionName}");
                    continue;
                }
            }

            _logger.LogInformation("========================================");
            _logger.LogInformation("Checking TMDB Collection Configuration");
            _logger.LogInformation("========================================");
            var config = Plugin.Instance!.Configuration;
            _logger.LogInformation("TMDB Collections Enabled: {Enabled}", config.EnableTmdbCollections);
            _logger.LogInformation("TMDB API Key Present: {HasKey}", !string.IsNullOrWhiteSpace(config.TmdbApiKey));
            _logger.LogInformation("TMDB API Key Length: {Length}", config.TmdbApiKey?.Length ?? 0);
            
            if (config.EnableTmdbCollections && !string.IsNullOrWhiteSpace(config.TmdbApiKey))
            {
                try
                {
                    _logger.LogInformation("TMDB collections are enabled and API key is configured. Starting execution...");
                    await ExecuteTmdbCollectionsAsync(config.TmdbApiKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing TMDB collections: {Message}", ex.Message);
                    _logger.LogError(ex, "Stack trace: {StackTrace}", ex.StackTrace);
                }
            }
            else if (config.EnableTmdbCollections && string.IsNullOrWhiteSpace(config.TmdbApiKey))
            {
                _logger.LogWarning("TMDB collections are enabled but API key is not configured or is empty");
            }
            else if (!config.EnableTmdbCollections)
            {
                _logger.LogInformation("TMDB collections are disabled in configuration");
            }

            _logger.LogInformation("Completed execution of all Auto collections");
        }

        public async Task ExecuteAutoCollections(IProgress<double> progress, CancellationToken cancellationToken)
        {
            await ExecuteAutoCollectionsNoProgress();
        }

        private string GetCollectionName(TagTitlePair tagTitlePair)
        {
            if (!string.IsNullOrWhiteSpace(tagTitlePair.Title))
            {
                return tagTitlePair.Title;
            }
            
            string[] tags = tagTitlePair.GetTagsArray();
            if (tags.Length == 0)
                return "Auto Collection";
                
            string firstTag = tags[0];
            string capitalizedTag = firstTag.Length > 0
                ? char.ToUpper(firstTag[0]) + firstTag[1..]
                : firstTag;

            if (tagTitlePair.MatchingMode == TagMatchingMode.And && tags.Length > 1)
            {
                return $"{capitalizedTag} + {tags.Length - 1} more tags";
            }

            return $"{capitalizedTag} Auto Collection";
        }

        private async Task SetPhotoForCollection(BoxSet collection, Person? specificPerson = null)
        {
            try
            {
                if (specificPerson != null && specificPerson.ImageInfos != null)
                {
                    var personImageInfo = specificPerson.ImageInfos
                        .FirstOrDefault(i => i.Type == ImageType.Primary);

                    if (personImageInfo != null)
                    {
                        collection.SetImage(new ItemImageInfo
                        {
                            Path = personImageInfo.Path,
                            Type = ImageType.Primary
                        }, 0);

                        await _libraryManager.UpdateItemAsync(
                            collection,
                            collection.GetParent(),
                            ItemUpdateType.ImageUpdate,
                            CancellationToken.None);
                        _logger.LogInformation("Successfully set image for collection {CollectionName} from specified person {PersonName}",
                            collection.Name, specificPerson.Name);

                        return;
                    }
                }

                var query = new InternalItemsQuery
                {
                    Recursive = true
                };

                var items = collection.GetItems(query)
                    .Items
                    .ToList();

                _logger.LogDebug("Found {Count} items in collection {CollectionName}",
                    items.Count, collection.Name);

                if (specificPerson == null)
                {
                    string term = collection.Name;

                    var personQuery = new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Person },
                        Name = term
                    };

                    var person = _libraryManager.GetItemList(personQuery)
                        .FirstOrDefault(p =>
                            p.Name.Equals(term, StringComparison.OrdinalIgnoreCase) &&
                            p.ImageInfos != null &&
                            p.ImageInfos.Any(i => i.Type == ImageType.Primary)) as Person;

                    if (person != null && person.ImageInfos != null)
                    {
                        var personImageInfo = person.ImageInfos
                            .FirstOrDefault(i => i.Type == ImageType.Primary);

                        if (personImageInfo != null)
                        {
                            collection.SetImage(new ItemImageInfo
                            {
                                Path = personImageInfo.Path,
                                Type = ImageType.Primary
                            }, 0);

                            await _libraryManager.UpdateItemAsync(
                                collection,
                                collection.GetParent(),
                                ItemUpdateType.ImageUpdate,
                                CancellationToken.None);
                            _logger.LogInformation("Successfully set image for collection {CollectionName} from detected person {PersonName}",
                                collection.Name, person.Name);

                            return;
                        }
                    }
                }

                var mediaItemWithImage = items
                    .Where(item => item is Movie || item is Series)
                    .FirstOrDefault(item =>
                        item.ImageInfos != null &&
                        item.ImageInfos.Any(i => i.Type == ImageType.Primary));

                if (mediaItemWithImage != null)
                {
                    var imageInfo = mediaItemWithImage.ImageInfos
                        .First(i => i.Type == ImageType.Primary);

                    collection.SetImage(new ItemImageInfo
                    {
                        Path = imageInfo.Path,
                        Type = ImageType.Primary
                    }, 0);

                    await _libraryManager.UpdateItemAsync(
                        collection,
                        collection.GetParent(),
                        ItemUpdateType.ImageUpdate,
                        CancellationToken.None);
                    _logger.LogInformation("Successfully set image for collection {CollectionName} from {ItemName}",
                        collection.Name, mediaItemWithImage.Name);
                }
                else
                {
                    _logger.LogWarning("No items with images found in collection {CollectionName}. Items: {Items}",
                        collection.Name,
                        string.Join(", ", items.Select(i => i.Name)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting image for collection {CollectionName}",
                    collection.Name);
            }
        }

        private async Task ExecuteAutoCollectionsForTagTitlePair(TagTitlePair tagTitlePair)
        {
            _logger.LogInformation($"Performing ExecuteAutoCollections for tag: {tagTitlePair.Tag}");
            
            var collectionName = GetCollectionName(tagTitlePair);
            
            var collection = GetBoxSetByName(collectionName);
            bool isNewCollection = false;
            if (collection is null)
            {
                _logger.LogInformation("{Name} not found, creating.", collectionName);
                collection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
                {
                    Name = collectionName,
                    IsLocked = false
                });
                collection.Tags = new[] { "Autocollection" };
                isNewCollection = true;
            }
            collection.DisplayOrder = "Default";

            string[] tags = tagTitlePair.GetTagsArray();
            if (tags.Length == 0)
            {
                _logger.LogWarning("No tags found in tag-title pair for collection {CollectionName}", collectionName);
                return;
            }

            Person? specificPerson = null;
            foreach (var tag in tags)
            {
                var personQuery = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Person },
                    Name = tag
                };

                specificPerson = _libraryManager.GetItemList(personQuery)
                    .FirstOrDefault(p =>
                        p.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) &&
                        p.ImageInfos != null &&
                        p.ImageInfos.Any(i => i.Type == ImageType.Primary)) as Person;
                
                if (specificPerson != null)
                {
                    _logger.LogInformation("Found specific person {PersonName} matching tag {Tag}",
                        specificPerson.Name, tag);
                    break;
                }
            }

            var allMovies = new List<Movie>();
            var allSeries = new List<Series>();
            
            if (tagTitlePair.MatchingMode == TagMatchingMode.And)
            {
                _logger.LogInformation("Using AND matching mode for tags: {Tags}", string.Join(", ", tags));
                _logger.LogDebug("Searching for items that match ALL of: {Tags}", string.Join(", ", tags));
                
                allMovies = GetMoviesFromLibraryWithAndMatching(tags, specificPerson).ToList();
                allSeries = GetSeriesFromLibraryWithAndMatching(tags, specificPerson).ToList();
                
                _logger.LogDebug("AND matching found {MovieCount} movies and {SeriesCount} series", 
                    allMovies.Count, allSeries.Count);
            }
            else
            {
                _logger.LogInformation("Using OR matching mode for tags: {Tags}", string.Join(", ", tags));
                _logger.LogDebug("Searching for items that match ANY of: {Tags}", string.Join(", ", tags));
                
                foreach (var tag in tags)
                {
                    _logger.LogDebug("Searching for tag: '{Tag}'", tag);
                    var movies = GetMoviesFromLibrary(tag, specificPerson).ToList();
                    var series = GetSeriesFromLibrary(tag, specificPerson).ToList();
                    
                    _logger.LogDebug("  Found {MovieCount} movies and {SeriesCount} series for tag '{Tag}'", 
                        movies.Count, series.Count, tag);
                    
                    if (movies.Count > 0)
                    {
                        foreach (var movie in movies)
                        {
                            var year = movie.ProductionYear?.ToString() ?? "Unknown year";
                            _logger.LogDebug("    + Movie: '{Title}' ({Year})", movie.Name, year);
                        }
                    }
                    
                    if (series.Count > 0)
                    {
                        foreach (var s in series)
                        {
                            var year = s.ProductionYear?.ToString() ?? "Unknown year";
                            _logger.LogDebug("    + Series: '{Title}' ({Year})", s.Name, year);
                        }
                    }
                    
                    _logger.LogInformation($"Found {movies.Count} movies and {series.Count} series for tag: {tag}");
                    
                    allMovies.AddRange(movies);
                    allSeries.AddRange(series);
                }
                
                var originalMovieCount = allMovies.Count;
                var originalSeriesCount = allSeries.Count;
                
                allMovies = allMovies.Distinct().ToList();
                allSeries = allSeries.Distinct().ToList();
                
                var movieDupes = originalMovieCount - allMovies.Count;
                var seriesDupes = originalSeriesCount - allSeries.Count;
                
                if (movieDupes > 0 || seriesDupes > 0)
                {
                    _logger.LogDebug("Removed {MovieDupes} duplicate movies and {SeriesDupes} duplicate series from OR matching", 
                        movieDupes, seriesDupes);
                }
            }
            
            _logger.LogInformation($"Processing {allMovies.Count} movies and {allSeries.Count} series total for collection: {collectionName}");
            
            var mediaItems = DedupeMediaItems(allMovies.Cast<BaseItem>().Concat(allSeries.Cast<BaseItem>()).ToList());

            await RemoveUnwantedMediaItems(collection, mediaItems);
            await AddWantedMediaItems(collection, mediaItems);
            await SortCollectionBy(collection, SortOrder.Descending);
            
            var updatedCollection = _libraryManager.GetItemById(collection.Id) as BoxSet;

            if (updatedCollection != null)
            {
                ValidateCollectionContent(updatedCollection, mediaItems);
            }
            else
            {
                _logger.LogWarning("Could not re-fetch collection {CollectionName} for validation.", collection.Name);
            }
            
            if (isNewCollection)
            {
                _logger.LogInformation("Setting image for newly created collection: {CollectionName}", collectionName);
                await SetPhotoForCollection(collection, specificPerson);
            }
            else
            {
                _logger.LogInformation("Preserving existing image for collection: {CollectionName}", collectionName);
            }
        }

        private async Task ExecuteAutoCollectionsForTitleMatchPair(TitleMatchPair titleMatchPair)
        {            string matchTypeText = titleMatchPair.MatchType switch
            {
                Configuration.MatchType.Title => "title",
                Configuration.MatchType.Genre => "genre",
                Configuration.MatchType.Studio => "studio",
                Configuration.MatchType.Actor => "actor",
                Configuration.MatchType.Director => "director",
                _ => "title"
            };
            
            string mediaTypeText = titleMatchPair.MediaType switch
            {
                Configuration.MediaTypeFilter.Movies => "movies only",
                Configuration.MediaTypeFilter.Series => "shows only",
                Configuration.MediaTypeFilter.All => "all media",
                _ => "all media"
            };
            
            _logger.LogInformation($"Performing ExecuteAutoCollections for {matchTypeText} match: {titleMatchPair.TitleMatch} (Media filter: {mediaTypeText})");
            
            var collectionName = titleMatchPair.CollectionName;
            
            var collection = GetBoxSetByName(collectionName);
            bool isNewCollection = false;
            if (collection == null)
            {
                _logger.LogInformation("{Name} not found, creating.", collectionName);
                collection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
                {
                    Name = collectionName,
                    IsLocked = false
                });
                collection.Tags = new[] { "Autocollection" };
                isNewCollection = true;
            }
            collection.DisplayOrder = "Default";
              
            _logger.LogDebug("Title Match Collection '{CollectionName}' - Pattern: '{Pattern}', Match Type: {MatchType}, Case Sensitive: {CaseSensitive}", 
                collectionName, titleMatchPair.TitleMatch, titleMatchPair.MatchType, titleMatchPair.CaseSensitive);
            
            List<Movie> allMovies = new();
            List<Series> allSeries = new();
            
            switch (titleMatchPair.MediaType)
            {
                case Configuration.MediaTypeFilter.Movies:
                    _logger.LogDebug("Media filter: Movies only");
                    allMovies = GetMoviesFromLibraryByMatch(
                        titleMatchPair.TitleMatch, 
                        titleMatchPair.CaseSensitive, 
                        titleMatchPair.MatchType
                    ).ToList();
                    _logger.LogInformation($"Media filter: Movies only - found {allMovies.Count} matching items");
                    
                    foreach (var movie in allMovies)
                    {
                        var year = movie.ProductionYear?.ToString() ?? "Unknown year";
                        _logger.LogDebug("  + Movie: '{Title}' ({Year})", movie.Name, year);
                    }
                    break;
                    
                case Configuration.MediaTypeFilter.Series:
                    _logger.LogDebug("Media filter: Series only");
                    allSeries = GetSeriesFromLibraryByMatch(
                        titleMatchPair.TitleMatch, 
                        titleMatchPair.CaseSensitive, 
                        titleMatchPair.MatchType
                    ).ToList();
                    _logger.LogInformation($"Media filter: Series only - found {allSeries.Count} matching items");
                    
                    foreach (var series in allSeries)
                    {
                        var year = series.ProductionYear?.ToString() ?? "Unknown year";
                        _logger.LogDebug("  + Series: '{Title}' ({Year})", series.Name, year);
                    }
                    break;
                    
                case Configuration.MediaTypeFilter.All:
                default:
                    _logger.LogDebug("Media filter: All (movies and series)");
                    allMovies = GetMoviesFromLibraryByMatch(
                        titleMatchPair.TitleMatch, 
                        titleMatchPair.CaseSensitive, 
                        titleMatchPair.MatchType
                    ).ToList();
                    
                    allSeries = GetSeriesFromLibraryByMatch(
                        titleMatchPair.TitleMatch, 
                        titleMatchPair.CaseSensitive, 
                        titleMatchPair.MatchType
                    ).ToList();
                    _logger.LogInformation($"Media filter: All - found {allMovies.Count} movies and {allSeries.Count} series");
                    
                    foreach (var movie in allMovies)
                    {
                        var year = movie.ProductionYear?.ToString() ?? "Unknown year";
                        _logger.LogDebug("  + Movie: '{Title}' ({Year})", movie.Name, year);
                    }
                    
                    foreach (var series in allSeries)
                    {
                        var year = series.ProductionYear?.ToString() ?? "Unknown year";
                        _logger.LogDebug("  + Series: '{Title}' ({Year})", series.Name, year);
                    }
                    break;
            }
            
            _logger.LogInformation($"Found {allMovies.Count} movies and {allSeries.Count} series matching {matchTypeText} pattern '{titleMatchPair.TitleMatch}' for collection: {collectionName}");
            
            var mediaItems = DedupeMediaItems(allMovies.Cast<BaseItem>().Concat(allSeries.Cast<BaseItem>()).ToList());

            await RemoveUnwantedMediaItems(collection, mediaItems);
            await AddWantedMediaItems(collection, mediaItems);
            await SortCollectionBy(collection, SortOrder.Descending);
            
            var updatedCollection = _libraryManager.GetItemById(collection.Id) as BoxSet;

            if (updatedCollection != null)
            {
                ValidateCollectionContent(updatedCollection, mediaItems);
            }
            else
            {
                _logger.LogWarning("Could not re-fetch collection {CollectionName} for validation.", collection.Name);
            }
            
            if (isNewCollection && mediaItems.Count > 0)
            {
                _logger.LogInformation("Setting image for newly created collection: {CollectionName}", collectionName);
                await SetPhotoForCollection(collection, null);
            }
            else
            {
                _logger.LogInformation("Preserving existing image for collection: {CollectionName}", collectionName);
            }
        }

        private void OnTimerElapsed()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public Task RunAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        private IEnumerable<Movie> GetMoviesWithPerson(string personNameToMatch, string personType, bool caseSensitive)
        {
            StringComparison comparison = caseSensitive 
                ? StringComparison.Ordinal 
                : StringComparison.OrdinalIgnoreCase;

            var persons = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Person },
                Recursive = true
            }).Select(p => p as Person)
                .Where(p => p?.Name != null && p.Name.Contains(personNameToMatch, comparison))
                .ToList();
            
            _logger.LogInformation("Found {Count} {PersonType}s matching '{NameToMatch}'", 
                persons.Count, personType, personNameToMatch);
            
            if (!persons.Any())
            {
                return Enumerable.Empty<Movie>();
            }
            
            var result = new HashSet<Movie>();
            foreach (var person in persons)
            {
                if (person?.Name != null)
                {
                    var movies = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Movie },
                        IsVirtualItem = false,
                        Recursive = true,
                        Person = person.Name,
                        PersonTypes = new[] { personType }
                    }).Select(m => m as Movie);
                    
                    foreach (var movie in movies)
                    {
                        if (movie != null)
                        {
                            result.Add(movie);
                        }
                    }
                }
            }
            
            return result;
        }
        
        private IEnumerable<Series> GetSeriesWithPerson(string personNameToMatch, string personType, bool caseSensitive)
        {
            StringComparison comparison = caseSensitive 
                ? StringComparison.Ordinal 
                : StringComparison.OrdinalIgnoreCase;
                
            var persons = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Person },
                Recursive = true
            }).Select(p => p as Person)
                .Where(p => p?.Name != null && p.Name.Contains(personNameToMatch, comparison))
                .ToList();
            
            _logger.LogInformation("Found {Count} {PersonType}s matching '{NameToMatch}'", 
                persons.Count, personType, personNameToMatch);
            
            if (!persons.Any())
            {
                return Enumerable.Empty<Series>();
            }
            
            var result = new HashSet<Series>();
            foreach (var person in persons)
            {
                if (person?.Name != null)
                {
                    var series = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Series },
                        IsVirtualItem = false,
                        Recursive = true,
                        Person = person.Name,
                        PersonTypes = new[] { personType }
                    }).Select(s => s as Series);
                    
                    foreach (var s in series)
                    {
                        if (s != null)
                        {
                            result.Add(s);
                        }
                    }
                }
            }
            
            return result;
        }

        private bool EvaluateMovieCriteria(Movie movie, Configuration.CriteriaType criteriaType, string value, bool caseSensitive)
        {
            StringComparison comparison = caseSensitive 
                ? StringComparison.Ordinal 
                : StringComparison.OrdinalIgnoreCase;
                
            switch (criteriaType)
            {
                case Configuration.CriteriaType.Title:
                    return movie.Name?.Contains(value, comparison) == true;
                    
                case Configuration.CriteriaType.Genre:
                    return movie.Genres != null && 
                           movie.Genres.Any(g => g.Contains(value, comparison));
                    
                case Configuration.CriteriaType.Studio:
                    return movie.Studios != null && 
                           movie.Studios.Any(s => s.Contains(value, comparison));
                    
                case Configuration.CriteriaType.Actor:
                    var matchingActorMovies = GetMoviesWithPerson(value, "Actor", caseSensitive);
                    return matchingActorMovies.Any(m => m.Id == movie.Id);
                    
                case Configuration.CriteriaType.Director:
                    var matchingDirectorMovies = GetMoviesWithPerson(value, "Director", caseSensitive);
                    return matchingDirectorMovies.Any(m => m.Id == movie.Id);
                    
                case Configuration.CriteriaType.Movie:
                    return true;
                    
                case Configuration.CriteriaType.Show:
                    return false;
                    
                case Configuration.CriteriaType.Tag:
                    return movie.Tags != null && 
                           movie.Tags.Any(t => t.Contains(value, comparison));
                           
                case Configuration.CriteriaType.ParentalRating:
                    return !string.IsNullOrEmpty(movie.OfficialRating) && 
                           movie.OfficialRating.Contains(value, comparison);
                             case Configuration.CriteriaType.CommunityRating:
                    return CompareNumericValue(movie.CommunityRating, value);                case Configuration.CriteriaType.CriticsRating:
                    return CompareNumericValue(movie.CriticRating, value);
                           
                case Configuration.CriteriaType.AudioLanguage:
                    return movie.GetMediaStreams()
                           .Any(stream => 
                                stream.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio && 
                                !string.IsNullOrEmpty(stream.Language) && 
                                stream.Language.Contains(value, comparison));
                case Configuration.CriteriaType.Subtitle:
                    return movie.GetMediaStreams()
                           .Any(stream => 
                                stream.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle && 
                                !string.IsNullOrEmpty(stream.Language) && 
                                stream.Language.Contains(value, comparison));
                  case Configuration.CriteriaType.ProductionLocation:
                    return movie.ProductionLocations != null && 
                           movie.ProductionLocations.Any(l => l.Contains(value, comparison));
                           
                case Configuration.CriteriaType.Year:
                    if (movie.ProductionYear.HasValue)
                    {
                        return CompareNumericValue(movie.ProductionYear.Value, value);
                    }
                    return false;
                case Configuration.CriteriaType.CustomRating:
                    if (!string.IsNullOrWhiteSpace(movie.CustomRating))
                    {
                        if (value.StartsWith(">") || value.StartsWith("<") || value.StartsWith("=") || float.TryParse(value, out _))
                        {
                            if (float.TryParse(movie.CustomRating, out var actualNumeric))
                            {
                                return CompareNumericValue(actualNumeric, value);
                            }
                        }
                        return movie.CustomRating.Contains(value, comparison);
                    }
                    return false;

                case Configuration.CriteriaType.Filename:
                    return !string.IsNullOrEmpty(movie.Path) && movie.Path.Contains(value, comparison);
                    
                case Configuration.CriteriaType.ReleaseDate:
                    return CompareDateValue(movie.PremiereDate, value);
                    
                case Configuration.CriteriaType.AddedDate:
                    return CompareDateValue(movie.DateCreated, value);
                    
                case Configuration.CriteriaType.EpisodeAirDate:
                    return false;
                    
                case Configuration.CriteriaType.Unplayed:
                    return IsItemUnplayed(movie);
                    
                case Configuration.CriteriaType.Watched:
                    return !IsItemUnplayed(movie);
                    
                default:
                    return false;
            }
        }
        private bool EvaluateSeriesCriteria(Series series, Configuration.CriteriaType criteriaType, string value, bool caseSensitive)
        {
            StringComparison comparison = caseSensitive 
                ? StringComparison.Ordinal 
                : StringComparison.OrdinalIgnoreCase;
                
            switch (criteriaType)
            {
                case Configuration.CriteriaType.Title:
                    return series.Name?.Contains(value, comparison) == true;
                    
                case Configuration.CriteriaType.Genre:
                    return series.Genres != null && 
                           series.Genres.Any(g => g.Contains(value, comparison));
                    
                case Configuration.CriteriaType.Studio:
                    return series.Studios != null && 
                           series.Studios.Any(s => s.Contains(value, comparison));
                    
                case Configuration.CriteriaType.Actor:
                    var actorSeries = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Series },
                        IsVirtualItem = false,
                        Recursive = true,
                        Person = value,
                        PersonTypes = new[] { "Actor" }
                    }).OfType<Series>();
                    
                    return actorSeries.Any(s => s.Id == series.Id);
                    
                case Configuration.CriteriaType.Director:
                    var directorSeries = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Series },
                        IsVirtualItem = false,
                        Recursive = true,
                        Person = value,
                        PersonTypes = new[] { "Director" }
                    }).OfType<Series>();
                    
                    return directorSeries.Any(s => s.Id == series.Id);
                    
                case Configuration.CriteriaType.Movie:
                    return false;
                    
                case Configuration.CriteriaType.Show:
                    return true;
                
                case Configuration.CriteriaType.Tag:
                    return series.Tags != null && 
                           series.Tags.Any(t => t.Contains(value, comparison));
                           
                case Configuration.CriteriaType.ParentalRating:
                    return !string.IsNullOrEmpty(series.OfficialRating) && 
                           series.OfficialRating.Contains(value, comparison);
                             case Configuration.CriteriaType.CommunityRating:
                    return CompareNumericValue(series.CommunityRating, value);
                      case Configuration.CriteriaType.CriticsRating:
                    return CompareNumericValue(series.CriticRating, value);
                case Configuration.CriteriaType.AudioLanguage:
                    var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        AncestorIds = new[] { series.Id },
                        IncludeItemTypes = new[] { BaseItemKind.Episode },
                        Recursive = true
                    });
                    
                    foreach (var episode in episodes)
                    {
                        if (episode.GetMediaStreams()
                            .Any(stream => 
                                stream.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio && 
                                !string.IsNullOrEmpty(stream.Language) && 
                                stream.Language.Contains(value, comparison)))
                        {
                            return true;
                        }
                    }
                    return false;
                    
                case Configuration.CriteriaType.Subtitle:
                    var episodesForSubs = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        AncestorIds = new[] { series.Id },
                        IncludeItemTypes = new[] { BaseItemKind.Episode },
                        Recursive = true
                    });
                    
                    foreach (var episode in episodesForSubs)
                    {
                        if (episode.GetMediaStreams()
                            .Any(stream => 
                                stream.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle && 
                                !string.IsNullOrEmpty(stream.Language) && 
                                stream.Language.Contains(value, comparison)))
                        {
                            return true;                        }
                    }
                    return false;
                    
                case Configuration.CriteriaType.ProductionLocation:
                    return series.ProductionLocations != null && 
                           series.ProductionLocations.Any(l => l.Contains(value, comparison));
                           
                case Configuration.CriteriaType.Year:
                    if (series.ProductionYear.HasValue)
                    {
                        return CompareNumericValue(series.ProductionYear.Value, value);
                    }
                    return false;
                case Configuration.CriteriaType.CustomRating:
                    if (!string.IsNullOrWhiteSpace(series.CustomRating))
                    {
                        if (value.StartsWith(">") || value.StartsWith("<") || value.StartsWith("=") || float.TryParse(value, out _))
                        {
                            if (float.TryParse(series.CustomRating, out var actualNumeric))
                            {
                                return CompareNumericValue(actualNumeric, value);
                            }
                        }
                        return series.CustomRating.Contains(value, comparison);
                    }
                    return false;

                case Configuration.CriteriaType.Filename:
                    var episodesForFilename = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        AncestorIds = new[] { series.Id },
                        IncludeItemTypes = new[] { BaseItemKind.Episode },
                        Recursive = true
                    });

                    foreach (var episode in episodesForFilename)
                    {
                        if (!string.IsNullOrEmpty(episode.Path) && episode.Path.Contains(value, comparison))
                        {
                            return true;
                        }
                    }
                    return false;
                    
                case Configuration.CriteriaType.ReleaseDate:
                    return CompareDateValue(series.PremiereDate, value);
                    
                case Configuration.CriteriaType.AddedDate:
                    return CompareDateValue(series.DateCreated, value);
                    
                case Configuration.CriteriaType.EpisodeAirDate:
                    return CompareDateValue(GetMostRecentEpisodeAirDate(series), value);
                    
                case Configuration.CriteriaType.Unplayed:
                    return IsItemUnplayed(series);
                    
                case Configuration.CriteriaType.Watched:
                    return !IsItemUnplayed(series);
                    
                default:
                    return false;
            }
        }

        private async Task ExecuteAutoCollectionsForExpressionCollection(Configuration.ExpressionCollection expressionCollection)
        {
            _logger.LogInformation("Processing expression collection: {CollectionName}", expressionCollection.CollectionName);
            
            if (!expressionCollection.ParseExpression())
            {
                _logger.LogError("Failed to parse expression for collection {CollectionName}: {Errors}", 
                    expressionCollection.CollectionName, 
                    string.Join("; ", expressionCollection.ParseErrors));
                return;
            }
            
            var collectionName = expressionCollection.CollectionName;
            var collection = GetBoxSetByName(collectionName);
            bool isNewCollection = false;
            
            if (collection is null)
            {
                _logger.LogInformation("{Name} not found, creating.", collectionName);
                collection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
                {
                    Name = collectionName,
                    IsLocked = false
                });
                collection.Tags = new[] { "Autocollection" };
                isNewCollection = true;
            }
            collection.DisplayOrder = "Default";
            
            var allMovies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Movie>().ToList();
            
            var allSeries = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Series>().ToList();
            
            _logger.LogInformation("Found {MovieCount} movies and {SeriesCount} series to evaluate", 
                allMovies.Count, allSeries.Count);
            
            _logger.LogDebug("Expression collection '{CollectionName}' - Expression: {Expression}", 
                collectionName, expressionCollection.Expression);
            
            var matchingMovies = new List<Movie>();
            var matchingSeries = new List<Series>();
            
            if (expressionCollection.ParsedExpression != null)
            {
                _logger.LogDebug("Evaluating movies against expression...");
                
                matchingMovies = allMovies
                    .Where(movie => movie != null)
                    .Where(movie => 
                    {
                        var matches = expressionCollection.ParsedExpression.Evaluate(
                            (criteriaType, value) => EvaluateMovieCriteria(movie, criteriaType, value, expressionCollection.CaseSensitive)
                        );
                        
                        if (matches)
                        {
                            var year = movie.ProductionYear?.ToString() ?? "Unknown year";
                            _logger.LogDebug("  ✓ Movie matched: '{Title}' ({Year}) (ID: {Id})", 
                                movie.Name, year, movie.Id);
                        }
                        
                        return matches;
                    })
                    .ToList();
                
                _logger.LogDebug("Evaluating series against expression...");
                    
                matchingSeries = allSeries
                    .Where(series => series != null)
                    .Where(series => 
                    {
                        var matches = expressionCollection.ParsedExpression.Evaluate(
                            (criteriaType, value) => EvaluateSeriesCriteria(series, criteriaType, value, expressionCollection.CaseSensitive)
                        );
                        
                        if (matches)
                        {
                            var year = series.ProductionYear?.ToString() ?? "Unknown year";
                            _logger.LogDebug("  ✓ Series matched: '{Title}' ({Year}) (ID: {Id})", 
                                series.Name, year, series.Id);
                        }
                        
                        return matches;
                    })
                    .ToList();
            }
            
            _logger.LogInformation("Expression matched {MovieCount} movies and {SeriesCount} series", 
                matchingMovies.Count, matchingSeries.Count);
                
            var allMatchingItems = DedupeMediaItems(matchingMovies.Cast<BaseItem>().Concat(matchingSeries.Cast<BaseItem>()).ToList());         

            await RemoveUnwantedMediaItems(collection, allMatchingItems);
            await AddWantedMediaItems(collection, allMatchingItems);
            await SortCollectionBy(collection, SortOrder.Descending);

            var updatedCollection = _libraryManager.GetItemById(collection.Id) as BoxSet;

            if (updatedCollection != null)
            {
                ValidateCollectionContent(updatedCollection, allMatchingItems);
            }
            else
            {
                _logger.LogWarning("Could not re-fetch expression collection {CollectionName} for validation.", collection.Name);
            }
            
            if (isNewCollection && allMatchingItems.Count > 0)
            {
                await SetPhotoForCollection(collection);
            }
        }
        
        private async Task ExecuteTmdbCollectionsAsync(string apiKey)
        {
            _logger.LogInformation("========================================");
            _logger.LogInformation("Starting TMDB Collection Processing");
            _logger.LogInformation("========================================");
            _logger.LogInformation("TMDB API Key configured: {HasKey}", !string.IsNullOrWhiteSpace(apiKey));
            _logger.LogInformation("TMDB API Key: {ApiKey}", apiKey);
            _logger.LogInformation("TMDB API Key Length: {Length}", apiKey?.Length ?? 0);
            
            var cachePath = Path.Combine(_pluginDirectory, "tmdb_movie_collection_cache.json");
            var movieCache = LoadTmdbCache(cachePath);
            _logger.LogInformation("[TMDB] Loaded cache with {Count} previously checked movies", movieCache.Count);
            
            using var tmdbClient = new TmdbApiClient(apiKey, _logger);
            
            _logger.LogInformation("[TMDB] Querying library for all movies...");
            var allMovies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Movie>().ToList();
            
            _logger.LogInformation("[TMDB] Found {Count} total movies in library", allMovies.Count);
            
            var collectionMovies = new Dictionary<int, List<Movie>>();
            var collectionNames = new Dictionary<int, string>();
            
            int moviesWithTmdbId = 0;
            int moviesInCollections = 0;
            int moviesProcessed = 0;
            int cacheHits = 0;
            int apiCalls = 0;
            
            _logger.LogInformation("[TMDB] Processing {Count} movies to find collections...", allMovies.Count);
            
            foreach (var movie in allMovies)
            {
                moviesProcessed++;
                try
                {
                    if (movie.ProviderIds == null)
                    {
                        _logger.LogDebug("[TMDB] Movie '{MovieName}' has no ProviderIds", movie.Name);
                        continue;
                    }
                    
                    if (!movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) || string.IsNullOrWhiteSpace(tmdbIdStr))
                    {
                        _logger.LogDebug("[TMDB] Movie '{MovieName}' has no TMDB ID in ProviderIds. Available IDs: {ProviderIds}", 
                            movie.Name, string.Join(", ", movie.ProviderIds.Keys));
                        continue;
                    }
                    
                    moviesWithTmdbId++;
                    _logger.LogInformation("[TMDB] Processing movie {Index}/{Total}: '{MovieName}' (TMDB ID: {TmdbId})", 
                        moviesProcessed, allMovies.Count, movie.Name, tmdbIdStr);
                    
                    if (!int.TryParse(tmdbIdStr, out var tmdbId))
                    {
                        _logger.LogWarning("[TMDB] Invalid TMDB ID for movie '{MovieName}': {TmdbId}", movie.Name, tmdbIdStr);
                        continue;
                    }
                    
                    if (movieCache.ContainsKey(tmdbId))
                    {
                        cacheHits++;
                        _logger.LogInformation("[TMDB] Movie '{MovieName}' (TMDB ID: {TmdbId}) already checked - skipping", movie.Name, tmdbId);
                        continue;
                    }
                    
                    apiCalls++;
                    _logger.LogInformation("[TMDB] Movie '{MovieName}' not in cache, fetching from TMDB API (ID: {TmdbId})", movie.Name, tmdbId);
                    var movieDetails = await tmdbClient.GetMovieDetailsAsync(tmdbId);
                    
                    movieCache[tmdbId] = new TmdbMovieCacheEntry
                    {
                        TmdbId = tmdbId,
                        LastChecked = DateTime.UtcNow
                    };
                    _logger.LogInformation("[TMDB] Cached movie '{MovieName}' as checked (TMDB ID: {TmdbId})", movie.Name, tmdbId);
                    
                    if (movieDetails == null)
                    {
                        _logger.LogWarning("[TMDB] Could not fetch movie details for '{MovieName}' (TMDB ID: {TmdbId})", movie.Name, tmdbId);
                        continue;
                    }
                    
                    if (movieDetails.BelongsToCollection == null)
                    {
                        _logger.LogInformation("[TMDB] Movie '{MovieName}' does not belong to any collection", movie.Name);
                        continue;
                    }
                    
                    moviesInCollections++;
                    var collectionId = movieDetails.BelongsToCollection.Id;
                    var collectionName = movieDetails.BelongsToCollection.Name;
                    
                    if (string.IsNullOrWhiteSpace(collectionName))
                    {
                        collectionName = "TMDB Collection";
                    }
                    
                    _logger.LogInformation("[TMDB] Movie '{MovieName}' belongs to collection: '{CollectionName}' (ID: {CollectionId})", 
                        movie.Name, collectionName, collectionId);
                    
                    if (!collectionMovies.ContainsKey(collectionId))
                    {
                        collectionMovies[collectionId] = new List<Movie>();
                        collectionNames[collectionId] = collectionName;
                        _logger.LogInformation("[TMDB] New collection discovered: '{CollectionName}' (ID: {CollectionId})", collectionName, collectionId);
                    }
                    
                    collectionMovies[collectionId].Add(movie);
                    _logger.LogInformation("[TMDB] Added movie '{MovieName}' to collection '{CollectionName}'", movie.Name, collectionName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TMDB] Error processing movie '{MovieName}' for TMDB collections: {Message}", movie.Name, ex.Message);
                    continue;
                }
            }
            
            _logger.LogInformation("[TMDB] Processing Summary:");
            _logger.LogInformation("[TMDB]   - Total movies in library: {Total}", allMovies.Count);
            _logger.LogInformation("[TMDB]   - Movies with TMDB ID: {WithId}", moviesWithTmdbId);
            _logger.LogInformation("[TMDB]   - Movies in collections: {InCollections}", moviesInCollections);
            _logger.LogInformation("[TMDB]   - Cache hits (API calls skipped): {CacheHits}", cacheHits);
            _logger.LogInformation("[TMDB]   - API calls made: {ApiCalls}", apiCalls);
            _logger.LogInformation("[TMDB] Found {Count} unique TMDB collections", collectionMovies.Count);
            
            int collectionIndex = 0;
            foreach (var kvp in collectionMovies)
            {
                collectionIndex++;
                var collectionId = kvp.Key;
                var moviesInCollection = kvp.Value;
                var collectionName = collectionNames[collectionId];
                
                try
                {
                    _logger.LogInformation("[TMDB] ========================================");
                    _logger.LogInformation("[TMDB] Processing collection {Index}/{Total}: '{CollectionName}' (ID: {CollectionId})", 
                        collectionIndex, collectionMovies.Count, collectionName, collectionId);
                    _logger.LogInformation("[TMDB] Currently found {MovieCount} movies in library for this collection", moviesInCollection.Count);
                    
                    _logger.LogInformation("[TMDB] Fetching full collection details from TMDB API...");
                    var collectionDetails = await tmdbClient.GetCollectionDetailsAsync(collectionId);
                    if (collectionDetails == null)
                    {
                        _logger.LogWarning("[TMDB] Could not fetch collection details for TMDB collection ID {CollectionId}", collectionId);
                        _logger.LogInformation("[TMDB] Creating collection with {Count} movies we already found", moviesInCollection.Count);
                        await CreateOrUpdateTmdbCollectionAsync(collectionName, moviesInCollection);
                        continue;
                    }
                    
                    if (!string.IsNullOrWhiteSpace(collectionDetails.Name))
                    {
                        _logger.LogInformation("[TMDB] Using collection name from TMDB: '{CollectionName}'", collectionDetails.Name);
                        collectionName = collectionDetails.Name;
                    }
                    
                    var tmdbMovieIds = new HashSet<int>();
                    if (collectionDetails.Parts != null)
                    {
                        _logger.LogInformation("[TMDB] Collection contains {Count} movies according to TMDB", collectionDetails.Parts.Count);
                        foreach (var part in collectionDetails.Parts)
                        {
                            tmdbMovieIds.Add(part.Id);
                            _logger.LogDebug("[TMDB]   - TMDB Movie ID: {Id} ({Title})", part.Id, part.Title);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[TMDB] Collection details have no parts/movies list");
                    }
                    
                    _logger.LogInformation("[TMDB] Searching library for movies matching {Count} TMDB movie IDs...", tmdbMovieIds.Count);
                    var allMoviesInCollection = new List<Movie>();
                    int matchedCount = 0;
                    foreach (var movie in allMovies)
                    {
                        if (movie.ProviderIds != null && 
                            movie.ProviderIds.TryGetValue("Tmdb", out var movieTmdbIdStr) &&
                            int.TryParse(movieTmdbIdStr, out var movieTmdbId) &&
                            tmdbMovieIds.Contains(movieTmdbId))
                        {
                            allMoviesInCollection.Add(movie);
                            matchedCount++;
                            _logger.LogInformation("[TMDB]   ✓ Found match: '{MovieName}' (TMDB ID: {TmdbId})", movie.Name, movieTmdbId);
                        }
                    }
                    
                    _logger.LogInformation("[TMDB] Found {Count} movies in library for TMDB collection '{CollectionName}'", 
                        allMoviesInCollection.Count, collectionName);
                    _logger.LogInformation("[TMDB] Matched {Matched} out of {Total} TMDB collection movies", matchedCount, tmdbMovieIds.Count);
                    
                    _logger.LogInformation("[TMDB] Creating/updating collection '{CollectionName}' with {Count} movies...", 
                        collectionName, allMoviesInCollection.Count);
                    await CreateOrUpdateTmdbCollectionAsync(collectionName, allMoviesInCollection);
                    _logger.LogInformation("[TMDB] Successfully processed collection '{CollectionName}'", collectionName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TMDB] Error processing TMDB collection ID {CollectionId}: {Message}", collectionId, ex.Message);
                    _logger.LogError(ex, "[TMDB] Stack trace: {StackTrace}", ex.StackTrace);
                    continue;
                }
            }
            
            SaveTmdbCache(cachePath, movieCache);
            _logger.LogInformation("[TMDB] Saved cache with {Count} movies", movieCache.Count);
            
            _logger.LogInformation("========================================");
            _logger.LogInformation("Completed processing TMDB collections");
            _logger.LogInformation("Processed {Count} collections", collectionMovies.Count);
            _logger.LogInformation("========================================");
        }
        
        private async Task CreateOrUpdateTmdbCollectionAsync(string collectionName, List<Movie> movies)
        {
            _logger.LogInformation("[TMDB Collection] Creating/updating collection: '{CollectionName}'", collectionName);
            
            if (movies == null || movies.Count == 0)
            {
                _logger.LogWarning("[TMDB Collection] No movies to add to TMDB collection '{CollectionName}'", collectionName);
                return;
            }
            
            _logger.LogInformation("[TMDB Collection] Collection will contain {Count} movies:", movies.Count);
            foreach (var movie in movies)
            {
                _logger.LogInformation("[TMDB Collection]   - {Title} ({Year})", movie.Name, movie.ProductionYear?.ToString() ?? "Unknown");
            }
            
            _logger.LogInformation("[TMDB Collection] Checking if collection '{CollectionName}' already exists...", collectionName);
            var collection = GetBoxSetByName(collectionName);
            bool isNewCollection = false;
            
            if (collection == null)
            {
                _logger.LogInformation("[TMDB Collection] Collection does not exist, creating new collection: '{CollectionName}'", collectionName);
                collection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
                {
                    Name = collectionName,
                    IsLocked = false
                });
                collection.Tags = new[] { "Autocollection", "TMDB" };
                isNewCollection = true;
                _logger.LogInformation("[TMDB Collection] Created new collection with ID: {CollectionId}", collection.Id);
            }
            else
            {
                _logger.LogInformation("[TMDB Collection] Collection already exists with ID: {CollectionId}", collection.Id);
                var tags = collection.Tags?.ToList() ?? new List<string>();
                if (!tags.Contains("TMDB"))
                {
                    tags.Add("TMDB");
                    collection.Tags = tags.ToArray();
                    _logger.LogInformation("[TMDB Collection] Added TMDB tag to existing collection");
                }
            }
            
            collection.DisplayOrder = "Default";
            
            _logger.LogInformation("[TMDB Collection] Deduplicating {Count} movies...", movies.Count);
            var mediaItems = DedupeMediaItems(movies.Cast<BaseItem>().ToList());
            _logger.LogInformation("[TMDB Collection] After deduplication: {Count} movies", mediaItems.Count);
            
            _logger.LogInformation("[TMDB Collection] Removing unwanted items from collection...");
            await RemoveUnwantedMediaItems(collection, mediaItems);
            
            _logger.LogInformation("[TMDB Collection] Adding wanted items to collection...");
            await AddWantedMediaItems(collection, mediaItems);
            
            _logger.LogInformation("[TMDB Collection] Sorting collection...");
            await SortCollectionBy(collection, SortOrder.Descending);
            
            var updatedCollection = _libraryManager.GetItemById(collection.Id) as BoxSet;
            
            if (updatedCollection != null)
            {
                _logger.LogInformation("[TMDB Collection] Validating collection content...");
                ValidateCollectionContent(updatedCollection, mediaItems);
            }
            else
            {
                _logger.LogWarning("[TMDB Collection] Could not re-fetch collection for validation");
            }
            
            if (isNewCollection && mediaItems.Count > 0)
            {
                _logger.LogInformation("[TMDB Collection] Setting image for newly created collection: '{CollectionName}'", collectionName);
                await SetPhotoForCollection(collection, null);
            }
            
            _logger.LogInformation("[TMDB Collection] Successfully created/updated collection: '{CollectionName}'", collectionName);
        }
        
        private List<BaseItem> DedupeMediaItems(List<BaseItem> mediaItems)
        {
            _logger.LogDebug("Starting deduplication process for {Count} media items", mediaItems.Count);
            
            var withoutDateOrTitle = mediaItems
                .Where(i => !i.PremiereDate.HasValue || string.IsNullOrWhiteSpace(i.Name))
                .ToList();
                
            if (withoutDateOrTitle.Count > 0)
            {
                _logger.LogDebug("Found {Count} items without date or title - keeping all:", 
                    withoutDateOrTitle.Count);
                foreach (var item in withoutDateOrTitle)
                {
                    var reason = string.IsNullOrWhiteSpace(item.Name) ? "missing title" : "missing premiere date";
                    _logger.LogDebug("  - '{Title}' (ID: {Id}) - kept ({Reason})", 
                        item.Name ?? "Unknown", item.Id, reason);
                }
            }
            
            var itemsWithData = mediaItems
                .Where(i => !string.IsNullOrWhiteSpace(i.Name) && i.PremiereDate.HasValue)
                .ToList();
                
            var grouped = itemsWithData
                .GroupBy(i => new { Title = i.Name!.Trim().ToLowerInvariant(), Date = i.PremiereDate!.Value })
                .ToList();
                
            var uniqueItems = new List<BaseItem>();
            var duplicatesRemoved = 0;
            
            foreach (var group in grouped)
            {
                var items = group.ToList();
                var kept = items.First();
                uniqueItems.Add(kept);
                
                if (items.Count > 1)
                {
                    duplicatesRemoved += items.Count - 1;
                    var itemType = kept is Movie ? "Movie" : kept is Series ? "Series" : "Item";
                    _logger.LogDebug("Duplicate {Type} found - '{Title}' ({Date}):", 
                        itemType, kept.Name, kept.PremiereDate!.Value.ToShortDateString());
                    _logger.LogDebug("  ✓ Keeping: ID {Id} from '{Path}'", 
                        kept.Id, kept.Path ?? "Unknown path");
                    
                    foreach (var duplicate in items.Skip(1))
                    {
                        _logger.LogDebug("  ✗ Removing duplicate: ID {Id} from '{Path}'", 
                            duplicate.Id, duplicate.Path ?? "Unknown path");
                    }
                }
            }
            
            var result = uniqueItems.Concat(withoutDateOrTitle).ToList();
            
            _logger.LogDebug("Deduplication complete: {Original} items → {Final} items ({Removed} duplicates removed)", 
                mediaItems.Count, result.Count, duplicatesRemoved);
            
            return result;
        }
        
        private bool CompareNumericValue(float? actualValue, string targetValueString)
        {
            if (!actualValue.HasValue)
                return false;
                
            targetValueString = targetValueString.Trim();
                
            try
            {
                if (targetValueString.StartsWith(">="))
                {
                    if (float.TryParse(targetValueString.Substring(2), out float targetValue))
                        return actualValue >= targetValue;
                }
                else if (targetValueString.StartsWith("<="))
                {
                    if (float.TryParse(targetValueString.Substring(2), out float targetValue))
                        return actualValue <= targetValue;
                }
                else if (targetValueString.StartsWith(">"))
                {
                    if (float.TryParse(targetValueString.Substring(1), out float targetValue))
                        return actualValue > targetValue;
                }
                else if (targetValueString.StartsWith("<"))
                {
                    if (float.TryParse(targetValueString.Substring(1), out float targetValue))
                        return actualValue < targetValue;
                }
                else if (targetValueString.StartsWith("="))
                {
                    if (float.TryParse(targetValueString.Substring(1), out float targetValue))
                        return Math.Abs(actualValue.Value - targetValue) < 0.1f;
                }
                else if (float.TryParse(targetValueString, out float targetValue))
                {
                    return Math.Abs(actualValue.Value - targetValue) < 0.1f;
                }
            }
            catch (FormatException)
            {
                _logger.LogWarning($"Failed to parse '{targetValueString}' as a numeric value for comparison");
            }
            
            return false;
        }
        
        private bool CompareDateValue(DateTime? actualDate, string targetValueString)
        {
            if (!actualDate.HasValue)
                return false;
                
            targetValueString = targetValueString.Trim();
                
            try
            {
                string numberPart;
                string operatorPart;
                
                if (targetValueString.StartsWith(">="))
                {
                    operatorPart = ">=";
                    numberPart = targetValueString.Substring(2);
                }
                else if (targetValueString.StartsWith("<="))
                {
                    operatorPart = "<=";
                    numberPart = targetValueString.Substring(2);
                }
                else if (targetValueString.StartsWith(">"))
                {
                    operatorPart = ">";
                    numberPart = targetValueString.Substring(1);
                }
                else if (targetValueString.StartsWith("<"))
                {
                    operatorPart = "<";
                    numberPart = targetValueString.Substring(1);
                }
                else if (targetValueString.StartsWith("="))
                {
                    operatorPart = "=";
                    numberPart = targetValueString.Substring(1);
                }
                else
                {
                    operatorPart = ">";
                    numberPart = targetValueString;
                }
                
                if (!int.TryParse(numberPart, out int targetDays))
                    return false;
                    
                var now = DateTime.Now;
                var daysDifference = (now - actualDate.Value).TotalDays;
                
                switch (operatorPart)
                {
                    case ">=":
                        return daysDifference >= targetDays;
                    case "<=":
                        return daysDifference <= targetDays;
                    case ">":
                        return daysDifference > targetDays;
                    case "<":
                        return daysDifference < targetDays;
                    case "=":
                        return Math.Abs(daysDifference - targetDays) < 1.0;
                    default:
                        return false;
                }
            }
            catch (FormatException)
            {
                _logger.LogWarning($"Failed to parse '{targetValueString}' as a day value for date comparison");
            }
            
            return false;
        }
        
        private bool IsItemUnplayed(BaseItem item)
        {
            try
            {
                if (_userDataManager == null)
                {
                    _logger.LogWarning("UserDataManager not available, assuming item is unplayed");
                    return true;
                }

                var userDataKeys = item.GetUserDataKeys();
                if (userDataKeys != null && userDataKeys.Count > 0)
                {
                    return true;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking play state for item {ItemName}", item.Name);
                return true;
            }
        }
        
        private DateTime? GetMostRecentEpisodeAirDate(Series series)
        {
            try
            {
                var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    IsVirtualItem = false,
                    Recursive = true,
                    ParentId = series.Id
                });

                DateTime? mostRecentDate = null;
                
                foreach (var episode in episodes)
                {
                    var episodeAirDate = episode.PremiereDate;
                    if (episodeAirDate.HasValue)
                    {
                        if (!mostRecentDate.HasValue || episodeAirDate.Value > mostRecentDate.Value)
                        {
                            mostRecentDate = episodeAirDate.Value;
                        }
                    }
                }

                return mostRecentDate;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting most recent episode air date for series {SeriesName}", series.Name);
                return null;
            }
        }
        
        private Dictionary<int, TmdbMovieCacheEntry> LoadTmdbCache(string cachePath)
        {
            var cache = new Dictionary<int, TmdbMovieCacheEntry>();
            
            try
            {
                if (File.Exists(cachePath))
                {
                    var json = File.ReadAllText(cachePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var entries = System.Text.Json.JsonSerializer.Deserialize<List<TmdbMovieCacheEntry>>(json, new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (entries != null)
                        {
                            foreach (var entry in entries)
                            {
                                cache[entry.TmdbId] = entry;
                                _logger.LogDebug("[TMDB Cache] Loaded entry: TMDB ID {TmdbId}, Last checked: {LastChecked}", 
                                    entry.TmdbId, entry.LastChecked);
                            }
                            _logger.LogInformation("[TMDB Cache] Loaded {Count} entries from cache file", cache.Count);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("[TMDB Cache] Cache file does not exist, starting with empty cache");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TMDB Cache] Error loading cache file, starting with empty cache: {Message}", ex.Message);
            }
            
            return cache;
        }
        
        private void SaveTmdbCache(string cachePath, Dictionary<int, TmdbMovieCacheEntry> cache)
        {
            try
            {
                var entries = cache.Values.ToList();
                var json = System.Text.Json.JsonSerializer.Serialize(entries, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(cachePath, json);
                _logger.LogInformation("[TMDB Cache] Saved {Count} entries to cache file", cache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TMDB Cache] Error saving cache file: {Message}", ex.Message);
            }
        }
    }
    
    internal class TmdbMovieCacheEntry
    {
        public int TmdbId { get; set; }
        public DateTime LastChecked { get; set; }
    }
}