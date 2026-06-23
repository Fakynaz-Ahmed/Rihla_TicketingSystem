using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rihla.DTOs;
using Rihla.Services.Ai;
using Rihla.Services.Db;
using System.Security.Claims;

namespace Rihla.Controllers;

[ApiController]
[Route("api/ai-assistant")]
[Authorize]
public class AiAssistantController : ControllerBase
{
    private readonly IConversationService _convService;
    private readonly IAppUserRepository _userRepo;
    private readonly ILogger<AiAssistantController> _logger;

    public AiAssistantController(
        IConversationService convService,
        IAppUserRepository userRepo,
        ILogger<AiAssistantController> logger)
    {
        _convService = convService;
        _userRepo    = userRepo;
        _logger      = logger;
    }

    /// <summary>Start a new AI conversation for the authenticated employee.</summary>
    [HttpPost("conversation")]
    public async Task<IActionResult> StartConversation()
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new ApiErrorResponse { Message = "المستخدم غير موجود." });
            }

            var result = await _convService.StartAsync(
                userId     : user.Id,
                userName   : user.Name,
                userRole   : user.PrimaryRole ?? "Employee",
                language   : user.Language);

            return Ok(ApiResponse<StartConversationResponseDto>.Ok(result, "تم بدء المحادثة بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting conversation for user {UserId}", GetUserId());
            return StatusCode(500, new ApiErrorResponse { Message = "فشل بدء المحادثة." });
        }
    }

    /// <summary>Send a message and/or upload a file, and get the AI assistant's reply.</summary>
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromForm] SendMessageRequestDto request)
    {
        if (request == null || (string.IsNullOrWhiteSpace(request.Message) && request.File == null))
        {
            return BadRequest(new ApiErrorResponse { Message = "الرسالة أو الملف مطلوب." });
        }

        try
        {
            var userId = GetUserId();
            var result = await _convService.SendMessageAsync(request.ConversationId, userId, request.Message, request.File);
            return Ok(ApiResponse<SendMessageResponseDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse { Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ApiErrorResponse { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat error for conversation {ConvId}", request?.ConversationId);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل إرسال الرسالة." });
        }
    }

    /// <summary>Update the status of an extracted passport (e.g., confirm or cancel).</summary>
    [HttpPost("passport/status")]
    [Authorize(Roles = "System Manager")]
    public async Task<IActionResult> UpdatePassportStatus([FromBody] PassportConfirmRequestDto request)
    {
        if (request == null || string.IsNullOrEmpty(request.PassportNumber))
        {
            return BadRequest(new ApiErrorResponse { Message = "رقم جواز السفر مطلوب." });
        }

        if (request.Status != Rihla.Models.Db.PassportStatus.Confirmed && request.Status != Rihla.Models.Db.PassportStatus.Canceled)
        {
            return BadRequest(new ApiErrorResponse { Message = "الحالة يجب أن تكون 1 (مؤكد) أو 2 (ملغي)." });
        }

        try
        {
            var userId = GetUserId();
            var result = await _convService.UpdatePassportStatusAsync(request.PassportNumber, request.Status, userId);
            return Ok(ApiResponse<PassportConfirmResponseDto>.Ok(result, "تم تحديث حالة جواز السفر بنجاح."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse { Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ApiErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update passport status error for passport {PassportNum}", request.PassportNumber);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل تحديث حالة جواز السفر." });
        }
    }

    /// <summary>Get list of all passports (optionally filtered by status).</summary>
    [HttpGet("passport/list")]
    [Authorize(Roles = "System Manager")]
    public async Task<IActionResult> GetPassports([FromQuery] string? status)
    {
        try
        {
            var result = await _convService.GetPassportsAsync(status);
            return Ok(ApiResponse<List<PassportPreviewDto>>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get passports list error");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب قائمة جوازات السفر." });
        }
    }

    /// <summary>End the active conversation.</summary>
    [HttpPost("conversation/end")]
    public async Task<IActionResult> EndConversation([FromBody] EndConversationRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            await _convService.EndAsync(request.ConversationId, userId);
            return Ok(ApiResponse<object?>.Ok(null, "تم إنهاء المحادثة بنجاح."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse { Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ApiErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "End conversation error for {ConvId}", request.ConversationId);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل إنهاء المحادثة." });
        }
    }

    /// <summary>Permanently delete a conversation (SuperAdmin only, or the conversation owner).</summary>
    [HttpDelete("conversation/{conversationId}")]
    public async Task<IActionResult> DeleteConversation(string conversationId)
    {
        try
        {
            // For safety, let's verify if the user owns this conversation or if they are Admin
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            var conv = await _convService.GetHistoryAsync(conversationId, userId); // Throws if not owned

            await _convService.DeleteAsync(conversationId);
            return Ok(ApiResponse<object?>.Ok(null, "تم حذف المحادثة بنجاح."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse { Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ApiErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteConversation error for {ConvId}", conversationId);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل حذف المحادثة." });
        }
    }

    /// <summary>Retrieve full message history for a conversation.</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetHistory([FromQuery] string conversationId)
    {
        if (string.IsNullOrEmpty(conversationId))
        {
            return BadRequest(new ApiErrorResponse { Message = "معرف المحادثة مطلوب." });
        }

        try
        {
            var userId = GetUserId();
            var result = await _convService.GetHistoryAsync(conversationId, userId);
            return Ok(ApiResponse<GetMessagesResponseDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse { Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ApiErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHistory error for conversation {ConvId}", conversationId);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب رسائل المحادثة." });
        }
    }

    /// <summary>Retrieve paged list of conversations for the logged-in user.</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations([FromQuery] GetConversationsRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var result = await _convService.GetConversationsAsync(
                userId   : userId,
                page     : request.Page <= 0 ? 1 : request.Page,
                pageSize : request.PageSize <= 0 ? 20 : request.PageSize);

            return Ok(ApiResponse<PagedResult<ConversationSummaryDto>>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetConversations error for user {UserId}", GetUserId());
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب قائمة المحادثات." });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return int.TryParse(userIdStr, out var id) ? id : 0;
    }
}
