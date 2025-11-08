using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.AutoCollections.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.AutoCollections
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly AutoCollectionsManager _syncAutoCollectionsManager;

        public Plugin(
            IServerApplicationPaths appPaths,
            IXmlSerializer xmlSerializer,
            ICollectionManager collectionManager,
            IProviderManager providerManager,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            ILoggerFactory loggerFactory)
            : base(appPaths, xmlSerializer)
        {
            Instance = this;
            _syncAutoCollectionsManager = new AutoCollectionsManager(
                providerManager,
                collectionManager,
                libraryManager,
                userDataManager,
                loggerFactory.CreateLogger<AutoCollectionsManager>(),
                appPaths);

            InitializeConfigurationIfNeeded();
        }        private void InitializeConfigurationIfNeeded()
        {
            AttemptMigrationFromTags();
            
            bool needsInitialization = false;
            
            if (!Configuration.IsInitialized)
            {
#pragma warning disable CS0618
                bool hasExistingConfig = (Configuration.TitleMatchPairs != null && Configuration.TitleMatchPairs.Count > 0) ||
                                        (Configuration.ExpressionCollections != null && Configuration.ExpressionCollections.Count > 0) ||
                                        (Configuration.TagTitlePairs != null && Configuration.TagTitlePairs.Count > 0) ||
                                        (Configuration.Tags != null && Configuration.Tags.Length > 0);
#pragma warning restore CS0618
                
                if (hasExistingConfig)
                {
                    Configuration.IsInitialized = true;
                    SaveConfiguration();
                }
                else
                {
                    needsInitialization = true;
                }
            }
            
            if (Configuration.ExpressionCollections == null)
            {
                Configuration.ExpressionCollections = new List<ExpressionCollection>();
            }
              if (needsInitialization)
            {
                Configuration.TitleMatchPairs = new List<TitleMatchPair>
                {
                    new TitleMatchPair("Marvel", "Marvel Universe"),
                    new TitleMatchPair("Star Wars", "Star Wars Collection"),
                    new TitleMatchPair("Harry Potter", "Harry Potter Series"),
                    new TitleMatchPair("Lord of the Rings", "Middle Earth"),
                    new TitleMatchPair("Pirates", "Pirates Movies"),
                    new TitleMatchPair("Fast & Furious", "Fast & Furious Saga"),
                    new TitleMatchPair("Jurassic", "Jurassic Collection"),
                };
                
                Configuration.ExpressionCollections = new List<ExpressionCollection>
                {
                    new ExpressionCollection("Marvel Action", "STUDIO \"Marvel\" AND GENRE \"Action\"", false),
                    new ExpressionCollection("Spielberg or Nolan", "DIRECTOR \"Spielberg\" OR DIRECTOR \"Nolan\"", false),
                    new ExpressionCollection("Tom Hanks Dramas", "ACTOR \"Tom Hanks\" AND GENRE \"Drama\"", false)
                };

                #pragma warning disable CS0618
                Configuration.TagTitlePairs = new List<TagTitlePair>();
                Configuration.Tags = Array.Empty<string>();
                #pragma warning restore CS0618

                Configuration.IsInitialized = true;

                SaveConfiguration();
            }
        }        private void AttemptMigrationFromTags()
        {
            #pragma warning disable CS0618
            if (Configuration.TitleMatchPairs == null)
            {
                Configuration.TitleMatchPairs = new List<TitleMatchPair>();
            }
            
            bool migrationPerformed = false;
            
            if (Configuration.TagTitlePairs != null && Configuration.TagTitlePairs.Count > 0)
            {
                foreach (var tagPair in Configuration.TagTitlePairs)
                {
                    var titleMatch = new TitleMatchPair(
                        titleMatch: tagPair.Tag,
                        collectionName: tagPair.Title,
                        caseSensitive: false);
                        
                    Configuration.TitleMatchPairs.Add(titleMatch);
                }
                migrationPerformed = true;
            }
            else if (Configuration.Tags != null && Configuration.Tags.Length > 0)
            {
                foreach (var tag in Configuration.Tags)
                {
                    var titleMatch = new TitleMatchPair(tag);
                    Configuration.TitleMatchPairs.Add(titleMatch);
                }
                migrationPerformed = true;
            }
            
            if (migrationPerformed)
            {
                Configuration.IsInitialized = true;
                SaveConfiguration();
            }
            #pragma warning restore CS0618
        }

        public override string Name => "Auto Collections";

        public static Plugin Instance { get; private set; }        public override string Description
            => "Enables creation of Auto Collections based on simple criteria or advanced boolean expressions with custom collection names";        
        
        private readonly Guid _id = new Guid("06ebf4a9-1326-4327-968d-8da00e1ea2eb");
        public override Guid Id => _id;

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "Auto Collections",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configurationpage.html"
                }
            };
        }
    }
}
