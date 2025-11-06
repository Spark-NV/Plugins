using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    public class ImportSimklListTask : IScheduledTask
    {
        private readonly ILogger<ImportSimklListTask> _logger;
        private readonly PlanToWatchImporter _planToWatchImporter;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;

        public ImportSimklListTask(
            ILogger<ImportSimklListTask> logger,
            PlanToWatchImporter planToWatchImporter,
            IUserManager userManager,
            ILibraryManager libraryManager)
        {
            _logger = logger;
            _planToWatchImporter = planToWatchImporter;
            _userManager = userManager;
            _libraryManager = libraryManager;
        }

        public string Key => "SimklImportListTask";

        public string Name => "Import Simkl List";

        public string Category => "Simkl";

        public string Description => "Fetches the latest items from each user's selected Simkl list and creates/updates folders in their library paths";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(6).Ticks,
                    MaxRuntimeTicks = TimeSpan.FromMinutes(30).Ticks
                }
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (SimklPlugin.Instance == null)
            {
                _logger.LogWarning("SimklPlugin instance is null, cannot run import task");
                return;
            }

            var users = _userManager.Users.ToList();
            var userConfigs = SimklPlugin.Instance.Configuration.UserConfigs;

            if (users.Count == 0)
            {
                _logger.LogInformation("No users found");
                return;
            }

            var usersWithConfig = users
                .Where(u =>
                {
                    var config = userConfigs?.FirstOrDefault(c => c.Id == u.Id);
                    return config != null
                        && !string.IsNullOrEmpty(config.UserToken)
                        && (config.MoviesLibraryId != Guid.Empty || config.TvShowsLibraryId != Guid.Empty
#pragma warning disable CS0618
                            || !string.IsNullOrEmpty(config.MoviesLibraryPath) || !string.IsNullOrEmpty(config.TvShowsLibraryPath));
#pragma warning restore CS0618
                })
                .ToList();

            if (usersWithConfig.Count == 0)
            {
                _logger.LogInformation("No users with valid Simkl configuration found");
                return;
            }

            var percentPerUser = 100d / usersWithConfig.Count;
            double currentProgress = 0;

            foreach (var user in usersWithConfig)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var userConfig = userConfigs?.FirstOrDefault(c => c.Id == user.Id);
                    if (userConfig == null)
                    {
                        _logger.LogDebug("No Simkl configuration found for user {UserName}", user.Username);
                        currentProgress += percentPerUser;
                        progress.Report(currentProgress);
                        continue;
                    }

                    if (string.IsNullOrEmpty(userConfig.UserToken))
                    {
                        _logger.LogDebug("No Simkl token configured for user {UserName}", user.Username);
                        currentProgress += percentPerUser;
                        progress.Report(currentProgress);
                        continue;
                    }

#pragma warning disable CS0618
                    if (userConfig.MoviesLibraryId == Guid.Empty && userConfig.TvShowsLibraryId == Guid.Empty
                        && string.IsNullOrEmpty(userConfig.MoviesLibraryPath) && string.IsNullOrEmpty(userConfig.TvShowsLibraryPath))
#pragma warning restore CS0618
                    {
                        _logger.LogWarning("No library paths configured for user {UserName}", user.Username);
                        currentProgress += percentPerUser;
                        progress.Report(currentProgress);
                        continue;
                    }

                    _logger.LogInformation("Importing Simkl list for user {UserName}", user.Username);

                    var result = await _planToWatchImporter.ImportPlanToWatch(userConfig).ConfigureAwait(false);

                    if (result.Success)
                    {
                        _logger.LogInformation(
                            "Successfully imported Simkl list for user {UserName}. Created {Movies} movie folders, {Shows} TV show folders",
                            user.Username,
                            result.MoviesCreated,
                            result.ShowsCreated);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to import Simkl list for user {UserName}: {Error}",
                            user.Username,
                            result.Error ?? "Unknown error");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing Simkl list for user {UserName}", user.Username);
                }
                finally
                {
                    currentProgress += percentPerUser;
                    progress.Report(currentProgress);
                }
            }

            _logger.LogInformation("Completed Simkl list import for all users");

            _logger.LogInformation("Waiting 60 seconds before triggering library scan");
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Triggering library scan");
            try
            {
                var scanProgress = new Progress<double>();
                await _libraryManager.ValidateMediaLibrary(scanProgress, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Library scan completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering library scan");
            }
        }
    }
}
