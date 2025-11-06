using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using Jellyfin.Plugin.Simkl.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Simkl.API
{
    [ApiController]
    [Authorize]
    [Route("Simkl")]
    public class Endpoints : ControllerBase
    {
        private readonly SimklApi _simklApi;
        private readonly PlanToWatchImporter _planToWatchImporter;
        private readonly ILibraryManager _libraryManager;

        public Endpoints(SimklApi simklApi, PlanToWatchImporter planToWatchImporter, ILibraryManager libraryManager)
        {
            _simklApi = simklApi;
            _planToWatchImporter = planToWatchImporter;
            _libraryManager = libraryManager;
        }

        [HttpGet("oauth/pin")]
        public async Task<ActionResult<CodeResponse?>> GetPin()
        {
            return await _simklApi.GetCode();
        }

        [HttpGet("oauth/pin/{userCode}")]
        public async Task<ActionResult<CodeStatusResponse?>> GetPinStatus([FromRoute] string userCode)
        {
            return await _simklApi.GetCodeStatus(userCode);
        }

        [HttpGet("users/settings/{userId}")]
        public async Task<ActionResult<UserSettings?>> GetUserSettings([FromRoute] Guid userId)
        {
            var userConfiguration = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
            if (userConfiguration == null)
            {
                return NotFound();
            }

            return await _simklApi.GetUserSettings(userConfiguration.UserToken);
        }

        [HttpGet("libraries")]
        public ActionResult<List<LibraryInfo>> GetLibraries()
        {
            var libraries = _libraryManager.GetVirtualFolders()
                .Select(vf => new LibraryInfo
                {
                    Id = Guid.TryParse(vf.ItemId, out var guid) ? guid : Guid.Empty,
                    Name = vf.Name,
                    LibraryType = "Library",
                    Path = vf.LibraryOptions?.PathInfos?.FirstOrDefault()?.Path ?? string.Empty
                })
                .ToList();

            return libraries;
        }

        [HttpPost("import/plan-to-watch/{userId}")]
        public async Task<ActionResult<PlanToWatchImporter.ImportResult>> ImportPlanToWatch([FromRoute] Guid userId)
        {
            var userConfiguration = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
            if (userConfiguration == null)
            {
                return NotFound();
            }

#pragma warning disable CS0618
            if (userConfiguration.MoviesLibraryId == Guid.Empty && userConfiguration.TvShowsLibraryId == Guid.Empty
                && string.IsNullOrEmpty(userConfiguration.MoviesLibraryPath) && string.IsNullOrEmpty(userConfiguration.TvShowsLibraryPath))
#pragma warning restore CS0618
            {
                return BadRequest("Please configure at least one library (Movies or TV Shows) before importing.");
            }

            var result = await _planToWatchImporter.ImportPlanToWatch(userConfiguration);

            if (!result.Success && !string.IsNullOrEmpty(result.Error))
            {
                return BadRequest(result);
            }

            return result;
        }
    }
}