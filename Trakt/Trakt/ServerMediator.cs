using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trakt.Api;

namespace Trakt;

/// <summary>
/// All communication between the server and the plugins server instance should occur in this class.
/// </summary>
public class ServerMediator : IHostedService, IDisposable
{
    private readonly ILogger<ServerMediator> _logger;
    private readonly TraktApi _traktApi;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerMediator"/> class.
    /// </summary>
    /// <param name="loggerFactory">The <see cref="ILoggerFactory"/>.</param>
    /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
    /// <param name="appHost">The <see cref="IServerApplicationHost"/>.</param>
    /// <param name="userDataManager">The <see cref="IUserDataManager"/>.</param>
    /// <param name="userManager">The <see cref="IUserManager"/>.</param>
    public ServerMediator(
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IServerApplicationHost appHost,
        IUserDataManager userDataManager,
        IUserManager userManager)
    {
        _logger = loggerFactory.CreateLogger<ServerMediator>();
        _traktApi = new TraktApi(loggerFactory.CreateLogger<TraktApi>(), httpClientFactory, appHost, userDataManager, userManager);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // No event handlers needed - plugin only creates stubs from Trakt lists
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No event handlers to unregister
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Dispose managed resources here if needed
        }
    }
}
