using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rihla.DTOs;
using Rihla.Services.Ai;
using Rihla.Services.Cache;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rihla.Controllers;

/// <summary>
/// Admin-only settings endpoints.
/// </summary>
[ApiController]
[Route("api/settings")]
//[Authorize(Roles = "Admin,SuperAdmin")]
public class ApiSettingsController : ControllerBase
{
    private readonly ICacheService _cache;
    private readonly ILogger<ApiSettingsController> _logger;
    private readonly IWebHostEnvironment _env;

    public ApiSettingsController(ICacheService cache, ILogger<ApiSettingsController> logger, IWebHostEnvironment env)
    {
        _cache  = cache;
        _logger = logger;
        _env    = env;
    }

    public record SetAiKeyRequest(
        [Required(AllowEmptyStrings = false, ErrorMessage = "api_key is required.")]
        string ApiKey);

    /// <summary>
    /// Sets (or replaces) the AI API key stored in Redis and updates appsettings.json.
    /// Takes effect immediately — no app restart needed.
    /// </summary>
    [HttpPut("ai-key")]
    public async Task<IActionResult> SetAiKey([FromBody] SetAiKeyRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponse
            {
                Message = "Validation failed.",
                Errors  = ModelState.ToDictionary(
                    k => k.Key,
                    v => string.Join("; ", v.Value!.Errors.Select(e => e.ErrorMessage)))
            });

        // 1. Update Redis (immediate effect without restart)
        await _cache.SetAsync(AiService.CacheKey, request.ApiKey, TimeSpan.FromDays(3650));

        // 2. Update appsettings.json (persists across cache clears)
        try
        {
            var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
            if (System.IO.File.Exists(appSettingsPath))
            {
                var jsonString = await System.IO.File.ReadAllTextAsync(appSettingsPath);
                var jsonNode = JsonNode.Parse(jsonString);
                
                if (jsonNode?["OpenAI"] is JsonObject openAiNode)
                {
                    openAiNode["ApiKey"] = request.ApiKey;
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    await System.IO.File.WriteAllTextAsync(appSettingsPath, jsonNode.ToJsonString(options));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update appsettings.json");
        }

        _logger.LogInformation("AI API key updated.");

        return Ok(ApiResponse<object?>.Ok(null,
            "AI API key updated successfully in Cache and appsettings.json. All subsequent AI calls will use the new key."));
    }
}
