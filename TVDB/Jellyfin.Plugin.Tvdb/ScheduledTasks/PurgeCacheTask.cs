using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tvdb.ScheduledTasks
{
    public class PurgeCacheTask : IScheduledTask
    {
        private readonly ILogger<PurgeCacheTask> _logger;
        private readonly TvdbClientManager _tvdbClientManager;

        public PurgeCacheTask(
            ILogger<PurgeCacheTask> logger,
            TvdbClientManager tvdbClientManager)
        {
            _logger = logger;
            _tvdbClientManager = tvdbClientManager;
        }

        public string Name => "Purge TheTVDB plugin cache";

        public string Key => "PurgeTheTVDBPluginCache";

        public string Description => "Purges the TheTVDB Cache";

        public string Category => "TheTVDB";

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (_tvdbClientManager.PurgeCache())
            {
                _logger.LogInformation("TheTvdb plugin cache purged successfully");
            }
            else
            {
                _logger.LogError("TheTvdb plugin cache purge failed");
            }

            return Task.CompletedTask;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Enumerable.Empty<TaskTriggerInfo>();
        }
    }
}
