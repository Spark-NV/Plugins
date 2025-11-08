using System.Net.Mime;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Providers;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Jellyfin.Plugin.AutoCollections.Configuration;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AutoCollections.Api
{
    [ApiController]
    [Route("AutoCollections")]
    [Produces(MediaTypeNames.Application.Json)]


    public class AutoCollectionsController : ControllerBase
    {
        private readonly AutoCollectionsManager _syncAutoCollectionsManager;
        private readonly ILogger<AutoCollectionsManager> _logger;

        public AutoCollectionsController(
            IProviderManager providerManager,
            ICollectionManager collectionManager,
            ILibraryManager libraryManager,
            ILogger<AutoCollectionsManager> logger,
            IApplicationPaths applicationPaths
        )
        {
            _syncAutoCollectionsManager = new AutoCollectionsManager(providerManager, collectionManager, libraryManager, logger, applicationPaths);
            _logger = logger;
        }        [HttpPost("AutoCollections")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> AutoCollectionsRequest()
        {
            _logger.LogInformation("Generating Auto Collections");
            await _syncAutoCollectionsManager.ExecuteAutoCollectionsNoProgress();
            _logger.LogInformation("Completed");
            return NoContent();
        }
        
        [HttpGet("ExportConfiguration")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Produces("application/json")]
        public ActionResult ExportConfiguration()
        {
            _logger.LogInformation("Exporting Auto Collections configuration");
            
            var config = Plugin.Instance!.Configuration;

            var exportData = new
            {
                TitleMatchPairs = config.TitleMatchPairs,
                ExpressionCollections = config.ExpressionCollections,
                TmdbApiKey = config.TmdbApiKey,
                EnableTmdbCollections = config.EnableTmdbCollections
            };
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            
            var json = JsonSerializer.Serialize(exportData, options);
            
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", "auto-collections-config.json");
        }
        
        [HttpPost("ImportConfiguration")]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ImportConfiguration()
        {
            _logger.LogInformation("Importing Auto Collections configuration");
            
            try
            {                using var reader = new StreamReader(Request.Body);
                var json = await reader.ReadToEndAsync();
                
                json = RemoveJsonComments(json);
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                };
                
                var importedConfig = JsonSerializer.Deserialize<PluginConfiguration>(json, options);
                
                if (importedConfig == null)
                {
                    return BadRequest("Invalid configuration file");
                }
                  if (importedConfig.ExpressionCollections != null)
                {
                    foreach (var collection in importedConfig.ExpressionCollections)
                    {
                        collection.Expression = collection.Expression
                            .Replace("TITEL", "TITLE")
                            .Replace("GENERE", "GENRE");
                        
                        bool isValid = collection.ParseExpression();
                        
                        if (!isValid && collection.ParseErrors.Count > 0)
                        {
                            _logger.LogWarning($"Expression errors in '{collection.CollectionName}': {string.Join(", ", collection.ParseErrors)}");
                        }
                    }
                }
                  var currentConfig = Plugin.Instance!.Configuration;
                currentConfig.TitleMatchPairs = importedConfig.TitleMatchPairs;
                currentConfig.ExpressionCollections = importedConfig.ExpressionCollections;
                
                if (!string.IsNullOrWhiteSpace(importedConfig.TmdbApiKey))
                {
                    currentConfig.TmdbApiKey = importedConfig.TmdbApiKey;
                }
                currentConfig.EnableTmdbCollections = importedConfig.EnableTmdbCollections;
                
                Plugin.Instance.SaveConfiguration();
                
                _logger.LogInformation("Configuration imported successfully");
                return Ok(new { Success = true, Message = "Configuration imported successfully" });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error deserializing configuration");
                return BadRequest($"Invalid JSON format: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing configuration");
                return BadRequest(new { Message = $"Error importing configuration: {ex.Message}" });
            }
        }

        [HttpPost("AddConfiguration")]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> AddConfiguration()
        {
            _logger.LogInformation("Adding Auto Collections configuration (merge)");

            try
            {
                using var reader = new StreamReader(Request.Body);
                var json = await reader.ReadToEndAsync();
                json = RemoveJsonComments(json);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                };

                var configToAdd = JsonSerializer.Deserialize<PluginConfiguration>(json, options);

                if (configToAdd == null)
                {
                    return BadRequest(new { Message = "Invalid configuration file for merging." });
                }

                var currentConfig = Plugin.Instance!.Configuration;

                if (configToAdd.TitleMatchPairs != null)
                {
                    if (currentConfig.TitleMatchPairs == null)
                    {
                        currentConfig.TitleMatchPairs = new List<TitleMatchPair>();
                    }
                    currentConfig.TitleMatchPairs.AddRange(configToAdd.TitleMatchPairs);
                    _logger.LogInformation($"Added {configToAdd.TitleMatchPairs.Count} TitleMatchPairs.");
                }

                if (configToAdd.ExpressionCollections != null)
                {
                    if (currentConfig.ExpressionCollections == null)
                    {
                        currentConfig.ExpressionCollections = new List<ExpressionCollection>();
                    }
                    foreach (var collection in configToAdd.ExpressionCollections)
                    {
                        collection.Expression = collection.Expression
                            .Replace("TITEL", "TITLE")
                            .Replace("GENERE", "GENRE");
                        
                        bool isValid = collection.ParseExpression();
                        if (!isValid && collection.ParseErrors.Count > 0)
                        {
                            _logger.LogWarning($"Expression errors in merged collection '{collection.CollectionName}': {string.Join(", ", collection.ParseErrors)}");
                        }
                    }
                    currentConfig.ExpressionCollections.AddRange(configToAdd.ExpressionCollections);
                    _logger.LogInformation($"Added {configToAdd.ExpressionCollections.Count} ExpressionCollections.");
                }

                if (!string.IsNullOrWhiteSpace(configToAdd.TmdbApiKey))
                {
                    currentConfig.TmdbApiKey = configToAdd.TmdbApiKey;
                }
                if (configToAdd.EnableTmdbCollections)
                {
                    currentConfig.EnableTmdbCollections = configToAdd.EnableTmdbCollections;
                }

                Plugin.Instance.SaveConfiguration();

                _logger.LogInformation("Configuration added (merged) successfully");
                return Ok(new { Success = true, Message = "Configuration added successfully" });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error deserializing configuration for merging");
                return BadRequest(new { Message = $"Invalid JSON format for merging: {ex.Message}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding configuration");
                return BadRequest(new { Message = $"Error adding configuration: {ex.Message}" });
            }
        }

        private string RemoveJsonComments(string json)
        {
            var lineCommentRegex = new System.Text.RegularExpressions.Regex(@"\/\/.*?$", System.Text.RegularExpressions.RegexOptions.Multiline);
            return lineCommentRegex.Replace(json, string.Empty);
        }
    }
}