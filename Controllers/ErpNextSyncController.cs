using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rihla.DTOs;
using Rihla.Services.ErpNext;
using System;
using System.Threading.Tasks;

namespace Rihla.Controllers;

[ApiController]
[Route("api/erpnext")]
[Authorize] // Require authorization for manual sync
public class ErpNextSyncController : ControllerBase
{
    private readonly IErpNextSyncService _syncService;

    public ErpNextSyncController(IErpNextSyncService syncService)
    {
        _syncService = syncService;
    }

    /// <summary>
    /// Manually trigger synchronization of ERPNext users with the backend database.
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncUsers()
    {
        try
        {
            var result = await _syncService.SyncUsersAsync();
            if (result.Success)
            {
                return Ok(ApiResponse<SyncResultDto>.Ok(result, result.Message));
            }
            return BadRequest(ApiResponse<SyncResultDto>.Fail(result.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiErrorResponse { Message = $"Internal server error during sync: {ex.Message}" });
        }
    }
}
