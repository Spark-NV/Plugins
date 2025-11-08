using MediaBrowser.Model.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.AutoCollections.Configuration
{
    public enum TagMatchingMode
    {
        Or = 0,
        And = 1
    }

    public class TagTitlePair
    {
        public string Tag { get; set; }
        public string Title { get; set; }
        public TagMatchingMode MatchingMode { get; set; }

        public TagTitlePair()
        {
            Tag = string.Empty;
            Title = "Auto Collection";
            MatchingMode = TagMatchingMode.Or;
        }

        public TagTitlePair(string tag, string title = null, TagMatchingMode matchingMode = TagMatchingMode.Or)
        {
            Tag = tag;
            Title = title ?? GetDefaultTitle(tag);
            MatchingMode = matchingMode;
        }

        private static string GetDefaultTitle(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return "Auto Collection";
                
            string firstTag = tag.Split(',')[0].Trim();
            return firstTag.Length > 0
                ? char.ToUpper(firstTag[0]) + firstTag[1..] + " Auto Collection"
                : "Auto Collection";
        }
        
        public string[] GetTagsArray()
        {
            if (string.IsNullOrEmpty(Tag))
                return new string[0];
                
            return Tag.Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();
        }
    }    public enum MatchType
    {
        Title = 0,
        Genre = 1,
        Studio = 2,
        Actor = 3,
        Director = 4
    }
    
    public enum MediaTypeFilter
    {
        All = 0,
        Movies = 1,
        Series = 2
    }

    public class TitleMatchPair
    {
        public string TitleMatch { get; set; }
        public string CollectionName { get; set; }
        public bool CaseSensitive { get; set; }
        public MatchType MatchType { get; set; }
        public MediaTypeFilter MediaType { get; set; }

        public TitleMatchPair()
        {
            TitleMatch = string.Empty;
            CollectionName = "Auto Collection";
            CaseSensitive = false;
            MatchType = MatchType.Title;
            MediaType = MediaTypeFilter.All;
        }

        public TitleMatchPair(string titleMatch, string collectionName = null, bool caseSensitive = false, 
                              MatchType matchType = MatchType.Title, MediaTypeFilter mediaType = MediaTypeFilter.All)
        {
            TitleMatch = titleMatch;
            CollectionName = collectionName ?? GetDefaultCollectionName(titleMatch, matchType);
            CaseSensitive = caseSensitive;
            MatchType = matchType;
            MediaType = mediaType;
        }        private static string GetDefaultCollectionName(string matchString, MatchType matchType)
        {
            if (string.IsNullOrEmpty(matchString))
                return "Auto Collection";
                
            return matchType switch
            {
                MatchType.Genre => $"{matchString} Genre",
                MatchType.Studio => $"{matchString} Studio Productions",
                MatchType.Actor => $"{matchString} Acting",
                MatchType.Director => $"{matchString} Directed",
                _ => $"{matchString} Movies"
            };
        }
    }    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            TitleMatchPairs = new List<TitleMatchPair>();
            ExpressionCollections = new List<ExpressionCollection>();
            
#pragma warning disable CS0618
            TagTitlePairs = new List<TagTitlePair>();
            Tags = new string[0];
#pragma warning restore CS0618
            
            IsInitialized = false;
            
            TmdbApiKey = string.Empty;
            EnableTmdbCollections = false;
        }

        public List<TitleMatchPair> TitleMatchPairs { get; set; }
        
        public List<ExpressionCollection> ExpressionCollections { get; set; }
        
        public bool IsInitialized { get; set; }
        
        public string TmdbApiKey { get; set; }
        public bool EnableTmdbCollections { get; set; }
        
        [Obsolete("Use TitleMatchPairs instead")]
        public List<TagTitlePair> TagTitlePairs { get; set; }
        
        [Obsolete("Use TitleMatchPairs instead")]
        public string[] Tags { get; set; }
    }
}
