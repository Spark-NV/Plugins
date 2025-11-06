using System;

namespace Jellyfin.Plugin.Simkl.Configuration
{
    public class UserConfig
    {
        public UserConfig()
        {
            ScrobbleMovies = true;
            ScrobbleShows = true;
            ScrobblePercentage = 70;
            ScrobbleNowWatchingPercentage = 5;
            MinLength = 5;
            UserToken = string.Empty;
            ScrobbleTimeout = 30;
        }

        public bool ScrobbleMovies { get; set; }

        public bool ScrobbleShows { get; set; }

        public int ScrobblePercentage { get; set; }

        public int ScrobbleNowWatchingPercentage { get; set; }

        public int MinLength { get; set; }

        public string UserToken { get; set; }

        public int ScrobbleTimeout { get; set; }

        public Guid Id { get; set; }

        public Guid MoviesLibraryId { get; set; } = Guid.Empty;

        public Guid TvShowsLibraryId { get; set; } = Guid.Empty;

        [Obsolete("Use MoviesLibraryId instead")]
        public string MoviesLibraryPath { get; set; } = string.Empty;

        [Obsolete("Use TvShowsLibraryId instead")]
        public string TvShowsLibraryPath { get; set; } = string.Empty;

        public string ImportListStatus { get; set; } = "plantowatch";

        public string MovieStubFilePath { get; set; } = string.Empty;
    }
}
