using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Trakt.Api;
using Trakt.Helpers;

namespace Trakt.ScheduledTasks;

/// <summary>
/// Task that syncs Trakt list items and creates/updates stub files.
/// </summary>
public class SyncTraktListTask : IScheduledTask
{
    private readonly IUserManager _userManager;
    private readonly ILogger<SyncTraktListTask> _logger;
    private readonly TraktApi _traktApi;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncTraktListTask"/> class.
    /// </summary>
    /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="appHost">Instance of the <see cref="IServerApplicationHost"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    public SyncTraktListTask(
        ILoggerFactory loggerFactory,
        IUserManager userManager,
        IHttpClientFactory httpClientFactory,
        IServerApplicationHost appHost,
        IUserDataManager userDataManager)
    {
        _userManager = userManager;
        _logger = loggerFactory.CreateLogger<SyncTraktListTask>();
        _loggerFactory = loggerFactory;
        _traktApi = new TraktApi(loggerFactory.CreateLogger<TraktApi>(), httpClientFactory, appHost, userDataManager, userManager);
    }

    /// <inheritdoc />
    public string Key => "TraktSyncListTask";

    /// <inheritdoc />
    public string Name => "Sync Trakt list and update stub files";

    /// <inheritdoc />
    public string Category => "Trakt";

    /// <inheritdoc />
    public string Description => "Fetches the latest items from each user's selected Trakt list and creates/updates stub files in their library paths";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Return a trigger set to 12 hours
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(12).Ticks,
                MaxRuntimeTicks = TimeSpan.FromMinutes(30).Ticks
            }
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var users = _userManager.Users.Where(u => UserHelper.GetTraktUser(u, true) != null).ToList();

        if (users.Count == 0)
        {
            _logger.LogInformation("No Trakt users found");
            return;
        }

        var percentPerUser = 100d / users.Count;
        double currentProgress = 0;

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var traktUser = UserHelper.GetTraktUser(user, true);
                if (traktUser == null)
                {
                    _logger.LogDebug("No Trakt user configuration for user {UserName}", user.Username);
                    currentProgress += percentPerUser;
                    progress.Report(currentProgress);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(traktUser.SelectedListId))
                {
                    _logger.LogDebug("No Trakt list selected for user {UserName}", user.Username);
                    currentProgress += percentPerUser;
                    progress.Report(currentProgress);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(traktUser.StubMovieLibraryPath) && string.IsNullOrWhiteSpace(traktUser.StubTvLibraryPath))
                {
                    _logger.LogWarning("No library paths configured for user {UserName}", user.Username);
                    currentProgress += percentPerUser;
                    progress.Report(currentProgress);
                    continue;
                }

                _logger.LogInformation("Syncing Trakt list for user {UserName}", user.Username);

                // Fetch list items from Trakt
                var listItems = await _traktApi.SendGetListItemsRequest(traktUser, traktUser.SelectedListId).ConfigureAwait(false);
                if (listItems == null || listItems.Count == 0)
                {
                    _logger.LogInformation("No items found in Trakt list for user {UserName}", user.Username);
                    currentProgress += percentPerUser;
                    progress.Report(currentProgress);
                    continue;
                }

                _logger.LogInformation("Found {Count} items in Trakt list for user {UserName}", listItems.Count, user.Username);

                // Create/update stub files
                var stubHelper = new StubFileHelper(_loggerFactory.CreateLogger<StubFileHelper>());
                var created = stubHelper.CreateStubFiles(listItems, traktUser.StubMovieLibraryPath, traktUser.StubTvLibraryPath);

                _logger.LogInformation("Created/updated {Count} stub files for user {UserName}", created, user.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Trakt list for user {UserName}", user.Username);
            }
            finally
            {
                currentProgress += percentPerUser;
                progress.Report(currentProgress);
            }
        }

        _logger.LogInformation("Completed Trakt list sync for all users");
    }
}
