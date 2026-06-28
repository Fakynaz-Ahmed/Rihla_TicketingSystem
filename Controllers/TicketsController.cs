using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rihla.DTOs;
using Rihla.Services.Db;
using Rihla.Services.Tickets;
using System.Security.Claims;

namespace Rihla.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly ITicketService _ticketService;
    private readonly IAppUserRepository _userRepo;
    private readonly ILogger<TicketsController> _logger;

    public TicketsController(
        ITicketService ticketService,
        IAppUserRepository userRepo,
        ILogger<TicketsController> logger)
    {
        _ticketService = ticketService;
        _userRepo = userRepo;
        _logger = logger;
    }

    /// <summary>Get tickets assigned to the authenticated support user.</summary>
    [HttpGet("support")]
    public async Task<IActionResult> GetSupportTickets([FromQuery] TicketFilterDto filter)
    {
        try
        {
            var userId = GetUserId();
            var result = await _ticketService.GetSupportTicketsAsync(userId, filter);
            return Ok(ApiResponse<VisitsResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting support tickets");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب تذاكر الدعم." });
        }
    }

    /// <summary>Get tickets assigned to the authenticated specialist.</summary>
    [HttpGet("specialist")]
    public async Task<IActionResult> GetSpecialistTickets([FromQuery] TicketFilterDto filter)
    {
        try
        {
            var userId = GetUserId();
            var result = await _ticketService.GetSpecialistTicketsAsync(userId, filter);
            return Ok(ApiResponse<VisitsResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting specialist tickets");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب تذاكر المتخصص." });
        }
    }

    /// <summary>Get all tickets (managers/admins only).</summary>
    [HttpGet("all")]
    [Authorize(Roles = "System Manager")]
    public async Task<IActionResult> GetAllTickets([FromQuery] TicketFilterDto filter)
    {
        try
        {
            var result = await _ticketService.GetAllTicketsAsync(filter);
            return Ok(ApiResponse<VisitsResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tickets");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب كل التذاكر." });
        }
    }

    /// <summary>Get tickets for the authenticated customer.</summary>
    [HttpGet("customer")]
    public async Task<IActionResult> GetCustomerTickets([FromQuery] TicketFilterDto filter)
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.ErpNextUserId))
            {
                return NotFound(new ApiErrorResponse { Message = "المستخدم أو حساب ERPNext غير موجود." });
            }

            var result = await _ticketService.GetCustomerTicketsAsync(user.ErpNextUserId, filter);
            return Ok(ApiResponse<VisitsResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer tickets");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب تذاكر العميل." });
        }
    }

    /// <summary>Create a ticket by an admin or support user.</summary>
    [HttpPost("admin")]
    public async Task<IActionResult> CreateTicketAdmin([FromBody] CreateTicketAdminRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Employee";
            var result = await _ticketService.CreateTicketAdminAsync(request, userId, role);
            return Ok(ApiResponse<SupportTicketDto>.Ok(result, "تم إنشاء التذكرة بنجاح."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating ticket by admin");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل إنشاء التذكرة." });
        }
    }

    /// <summary>Request field visit for a ticket (assigns specialist).</summary>
    [HttpPost("{id:int}/visit")]
    public async Task<IActionResult> RequestVisitForTicket(int id, [FromBody] RequestVisitDto request)
    {
        try
        {
            var userId = GetUserId();
            var result = await _ticketService.RequestVisitForTicketAsync(id, request, userId);
            return Ok(ApiResponse<SupportTicketDto>.Ok(result, "تم جدولة الزيارة وإسناد المتخصص بنجاح."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting visit for ticket {TicketId}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جدولة الزيارة للتذكرة." });
        }
    }

    /// <summary>Sync tickets from ERPNext.</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncTickets()
    {
        try
        {
            var result = await _ticketService.SyncTicketsAsync();
            return Ok(ApiResponse<SyncResultDto>.Ok(result, "تمت مزامنة التذاكر بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing tickets");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل مزامنة التذاكر." });
        }
    }

    /// <summary>Get ticket by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetTicketById(int id)
    {
        try
        {
            var result = await _ticketService.GetSupportTicketByIdAsync(id);
            if (result == null)
            {
                return NotFound(new ApiErrorResponse { Message = "التذكرة غير موجودة." });
            }
            return Ok(ApiResponse<SupportTicketDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ticket by ID {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب تفاصيل التذكرة." });
        }
    }

    /// <summary>Update ticket details.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateTicket(int id, [FromBody] UpdateSupportTicketRequestDto request)
    {
        try
        {
            var result = await _ticketService.UpdateSupportTicketAsync(id, request);
            return Ok(ApiResponse<SupportTicketDto>.Ok(result, "تم تحديث التذكرة بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating ticket {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل تحديث التذكرة." });
        }
    }

    /// <summary>Resolve a ticket.</summary>
    [HttpPost("{id:int}/resolve")]
    public async Task<IActionResult> ResolveTicket(int id)
    {
        try
        {
            await _ticketService.ResolveTicketBySupportAsync(id);
            return Ok(ApiResponse<object?>.Ok(null, "تم حل التذكرة بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving ticket {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل حل التذكرة." });
        }
    }

    /// <summary>Cancel a ticket.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelTicket(int id, [FromBody] CancelTicketRequestDto request)
    {
        try
        {
            await _ticketService.CancelTicketBySupportAsync(id, request?.Reason);
            return Ok(ApiResponse<object?>.Ok(null, "تم إلغاء التذكرة بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling ticket {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل إلغاء التذكرة." });
        }
    }

    /// <summary>Rate ticket performance (customer only).</summary>
    [HttpPost("{id:int}/rate")]
    public async Task<IActionResult> RateTicket(int id, [FromBody] RateTicketRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.ErpNextUserId))
            {
                return NotFound(new ApiErrorResponse { Message = "المستخدم أو حساب ERPNext غير موجود." });
            }

            var result = await _ticketService.RateTicketAsync(id, request.Rating, request.Feedback, user.ErpNextUserId);
            return Ok(ApiResponse<SupportTicketDto>.Ok(result, "تم تقييم التذكرة بنجاح."));
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
            _logger.LogError(ex, "Error rating ticket {TicketId}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل تقييم التذكرة." });
        }
    }

    private int GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return int.TryParse(userIdStr, out var id) ? id : 0;
    }
}

public class CancelTicketRequestDto
{
    public string? Reason { get; set; }
}

public class RateTicketRequestDto
{
    public float Rating { get; set; }
    public string? Feedback { get; set; }
}
