using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rihla.DTOs;
using Rihla.Services.Db;
using Rihla.Services.Visits;
using System.Security.Claims;

namespace Rihla.Controllers;

[ApiController]
[Route("api/visits")]
[Authorize]
public class VisitsController : ControllerBase
{
    private readonly IVisitService _visitService;
    private readonly IAppUserRepository _userRepo;
    private readonly ILogger<VisitsController> _logger;

    public VisitsController(
        IVisitService visitService,
        IAppUserRepository userRepo,
        ILogger<VisitsController> logger)
    {
        _visitService = visitService;
        _userRepo = userRepo;
        _logger = logger;
    }

    /// <summary>Get all scheduled visits (Managers/Admins only).</summary>
    [HttpGet("all")]
    [Authorize(Roles = "System Manager")]
    public async Task<IActionResult> GetAllVisits([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var result = await _visitService.GetAllVisitsAsync(page, pageSize);
            return Ok(ApiResponse<VisitsResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all visits");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب كل الزيارات الميدانية." });
        }
    }

    /// <summary>Get visits for the authenticated customer.</summary>
    [HttpGet("customer")]
    public async Task<IActionResult> GetCustomerVisits([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.ErpNextUserId))
            {
                return NotFound(new ApiErrorResponse { Message = "المستخدم أو حساب ERPNext غير موجود." });
            }

            var result = await _visitService.GetVisitsAsync(user.ErpNextUserId, page, pageSize);
            return Ok(ApiResponse<VisitsResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer visits");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب زيارات العميل." });
        }
    }

    /// <summary>Get visits assigned to the authenticated specialist.</summary>
    [HttpGet("specialist")]
    public async Task<IActionResult> GetSpecialistVisits([FromQuery] VisitFilterDto filter)
    {
        try
        {
            var userId = GetUserId();
            var result = await _visitService.GetSpecialistVisitsFilteredAsync(userId, filter);
            return Ok(ApiResponse<VisitsResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting specialist visits");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب زيارات المتخصص." });
        }
    }

    /// <summary>Get visit details by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetVisitById(int id)
    {
        try
        {
            var result = await _visitService.GetVisitDetailAsync(id);
            if (result == null)
            {
                return NotFound(new ApiErrorResponse { Message = "الزيارة غير موجودة." });
            }
            return Ok(ApiResponse<VisitDetailDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting visit by ID {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب تفاصيل الزيارة." });
        }
    }

    /// <summary>Sync visits from ERPNext Maintenance Visit.</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncVisits()
    {
        try
        {
            var result = await _visitService.SyncVisitsAsync();
            return Ok(ApiResponse<SyncResultDto>.Ok(result, "تمت مزامنة الزيارات بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing visits");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل مزامنة الزيارات." });
        }
    }

    /// <summary>Create a visit manually.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateVisit([FromBody] CreateVisitRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            var result = await _visitService.CreateVisitAsync(request, user?.Name ?? "Admin");
            return Ok(ApiResponse<VisitDto>.Ok(result, "تم إنشاء الزيارة بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating visit");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل إنشاء الزيارة." });
        }
    }

    /// <summary>Update visit details (planned dates, description).</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateVisit(int id, [FromBody] UpdateVisitDto request)
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            var result = await _visitService.UpdateVisitAsync(id, request, user?.Name ?? "Admin");
            return Ok(ApiResponse<VisitDto>.Ok(result, "تم تحديث الزيارة بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating visit {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل تحديث الزيارة." });
        }
    }

    /// <summary>Update visit execution status (e.g. In Progress, Completed).</summary>
    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> UpdateVisitStatus(
        int id,
        [FromForm] string status,
        [FromForm] string? notes,
        IFormFile? attachment)
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            var result = await _visitService.UpdateVisitStatusAsync(id, status, notes, user?.Name ?? "Admin", attachment);
            return Ok(ApiResponse<VisitDto>.Ok(result, "تم تحديث حالة الزيارة بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating visit status {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل تحديث حالة الزيارة." });
        }
    }

    /// <summary>Cancel specialist visit.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelVisit(int id, [FromBody] CancelVisitRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            var result = await _visitService.CancelSpecialistVisitAsync(id, request?.Reason, user?.Name ?? "Admin");
            return Ok(ApiResponse<VisitDto>.Ok(result, "تم إلغاء الزيارة بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling visit {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل إلغاء الزيارة." });
        }
    }

    /// <summary>Get activities log for a visit.</summary>
    [HttpGet("{id:int}/activities")]
    public async Task<IActionResult> GetActivities(int id)
    {
        try
        {
            var result = await _visitService.GetActivitiesAsync(id);
            return Ok(ApiResponse<List<VisitActivityDto>>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting activities for visit {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب سجل حركات الزيارة." });
        }
    }

    /// <summary>Rate visit performance (customer only).</summary>
    [HttpPost("{id:int}/rate")]
    public async Task<IActionResult> RateVisit(int id, [FromBody] RateVisitRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.ErpNextUserId))
            {
                return NotFound(new ApiErrorResponse { Message = "المستخدم أو حساب ERPNext غير موجود." });
            }

            var result = await _visitService.RateVisitAsync(id, request.Rating, request.Feedback, user.ErpNextUserId);
            return Ok(ApiResponse<VisitDto>.Ok(result, "تم تقييم الزيارة بنجاح."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ApiErrorResponse { Message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rating visit {VisitId}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل تقييم الزيارة." });
        }
    }

    private int GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return int.TryParse(userIdStr, out var id) ? id : 0;
    }
}

public class CancelVisitRequestDto
{
    public string? Reason { get; set; }
}

public class RateVisitRequestDto
{
    public float Rating { get; set; }
    public string? Feedback { get; set; }
}
