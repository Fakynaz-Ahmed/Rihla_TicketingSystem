using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Rihla.DTOs;
using Rihla.Resources;
using Rihla.Services.Auth;
using Rihla.Services.Db;
using System.Security.Claims;
using System.Text.Json;

namespace Rihla.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IAppUserRepository _userRepo;
    private readonly ILogger<AuthController> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public AuthController(
        IAuthService authService,
        IAppUserRepository userRepo,
        ILogger<AuthController> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _authService = authService;
        _userRepo    = userRepo;
        _logger      = logger;
        _localizer   = localizer;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            return Ok(ApiResponse<LoginResponseDto>.Ok(result, "تم تسجيل الدخول بنجاح."));
        }
        catch (Exception ex)
        {
            return AuthError(ex, request.Email);
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto request)
    {
        try
        {
            var result = await _authService.RefreshTokenAsync(request.RefreshToken);
            return Ok(ApiResponse<LoginResponseDto>.Ok(result, "تم تجديد الرمز بنجاح."));
        }
        catch (Exception ex)
        {
            return AuthError(ex);
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var token = ExtractBearerToken();
        if (!string.IsNullOrEmpty(token))
        {
            await _authService.LogoutAsync(token);
        }
        return Ok(ApiResponse<object?>.Ok(null, "تم تسجيل الخروج بنجاح."));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!int.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(new ApiErrorResponse { Message = "مستخدم غير مصرح له." });
        }

        var appUser = await _userRepo.GetByIdAsync(userId);
        if (appUser == null)
        {
            return NotFound(new ApiErrorResponse { Message = "المستخدم غير موجود." });
        }

        var modules = appUser.AllowedModules != null
            ? JsonSerializer.Deserialize<List<string>>(appUser.AllowedModules) ?? []
            : [];

        return Ok(ApiResponse<UserInfoDto>.Ok(new UserInfoDto
        {
            Id             = appUser.Id,
            ErpNextUserId  = appUser.ErpNextUserId,
            Email          = appUser.Email,
            Name           = appUser.Name,
            PrimaryRole    = appUser.PrimaryRole,
            Department     = appUser.Department,
            AllowedModules = modules,
            AvatarUrl      = appUser.AvatarUrl,
            Phone          = appUser.Phone,
            Language       = appUser.Language
        }));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        try
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdStr, out var userId))
            {
                return Unauthorized(new ApiErrorResponse { Message = "مستخدم غير مصرح له." });
            }

            await _authService.ChangePasswordAsync(userId, request.OldPassword, request.NewPassword);
            return Ok(ApiResponse<object?>.Ok(null, "تم تغيير كلمة المرور بنجاح."));
        }
        catch (Exception ex)
        {
            return AuthError(ex);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IActionResult AuthError(Exception ex, string? context = null)
    {
        return ex switch
        {
            ArgumentException e           => BadRequest(new ApiErrorResponse { Message = e.Message }),
            UnauthorizedAccessException e => Unauthorized(new ApiErrorResponse { Message = e.Message }),
            InvalidOperationException e   => BadRequest(new ApiErrorResponse { Message = e.Message }),
            _                             => Log500(ex, context)
        };
    }

    private IActionResult Log500(Exception ex, string? context)
    {
        _logger.LogError(ex, "Unexpected auth error{Context}", context is null ? "" : $" for {context}");
        return StatusCode(500, new ApiErrorResponse { Message = "حدث خطأ داخلي في الخادم." });
    }

    private string? ExtractBearerToken()
    {
        var header = Request.Headers.Authorization.FirstOrDefault();
        return header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? header["Bearer ".Length..].Trim()
            : null;
    }
}
