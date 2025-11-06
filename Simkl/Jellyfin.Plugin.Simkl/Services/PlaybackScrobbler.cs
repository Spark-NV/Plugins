using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    public class PlaybackScrobbler : IHostedService
    {
        private readonly ISessionManager _sessionManager;
        private readonly ILogger<PlaybackScrobbler> _logger;
        private readonly Dictionary<string, Guid> _lastScrobbled;
        private readonly SimklApi _simklApi;
        private DateTime _nextTry;

        public PlaybackScrobbler(
            ISessionManager sessionManager,
            ILogger<PlaybackScrobbler> logger,
            SimklApi simklApi)
        {
            _sessionManager = sessionManager;
            _logger = logger;
            _simklApi = simklApi;
            _lastScrobbled = new Dictionary<string, Guid>();
            _nextTry = DateTime.UtcNow;
        }

        private static bool CanBeScrobbled(UserConfig config, PlaybackProgressEventArgs playbackProgress)
        {
            var position = playbackProgress.PlaybackPositionTicks;
            var runtime = playbackProgress.MediaInfo.RunTimeTicks;

            if (runtime != null)
            {
                var percentageWatched = position / (float)runtime * 100f;

                if (percentageWatched < config.ScrobblePercentage)
                {
                    return false;
                }
            }

            if (runtime < 60 * 10000 * config.MinLength)
            {
                return false;
            }

            return playbackProgress.MediaInfo.Type switch
            {
                BaseItemKind.Movie => config.ScrobbleMovies,
                BaseItemKind.Episode => config.ScrobbleShows,
                _ => false
            };
        }

        private void OnPlaybackProgress(object? sessions, PlaybackProgressEventArgs e)
        {
            return;
        }

        private void OnPlaybackStopped(object? sessions, PlaybackStopEventArgs e)
        {
            return;
        }

        private Task ScrobbleSession(PlaybackProgressEventArgs eventArgs)
        {
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
