using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Tvdb.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public const string ProjectApiKey = "7f7eed88-2530-4f84-8ee7-f154471b8f87";
        private int _cacheDurationInHours = 1;
        private int _cacheDurationInDays = 7;
        private int _metadataUpdateInHours = 2;

        public string SubscriberPIN { get; set; } = string.Empty;

        public int CacheDurationInHours
        {
            get => _cacheDurationInHours;
            set => _cacheDurationInHours = value < 1 ? 1 : value;
        }

        public int CacheDurationInDays
        {
            get => _cacheDurationInDays;
            set => _cacheDurationInDays = value < 1 ? 7 : value;
        }

        public string FallbackLanguages { get; set; } = string.Empty;

        public bool ImportSeasonName { get; set; } = false;

        public bool FallbackToOriginalLanguage { get; set; } = false;

        public bool IncludeMissingSpecials { get; set; } = true;

        public bool RemoveAllMissingEpisodesOnRefresh { get; set; } = false;

        public bool CreateStubFilesForMissingEpisodes { get; set; } = true;

        public string EpisodeStubFilePath { get; set; } = string.Empty;

        public bool IncludeOriginalCountryInTags { get; set; } = false;

        public int MetadataUpdateInHours
        {
            get => _metadataUpdateInHours;
            set => _metadataUpdateInHours = value < 1 ? 1 : value;
        }

        public bool UpdateSeriesScheduledTask { get; set; } = false;

        public bool UpdateSeasonScheduledTask { get; set; } = false;

        public bool UpdateEpisodeScheduledTask { get; set; } = false;

        public bool UpdateMovieScheduledTask { get; set; } = false;

        public bool UpdatePersonScheduledTask { get; set; } = false;
    }
}
