using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rihla.Config;
using Rihla.DTOs;
using Rihla.Services.Cache;
using Rihla.Services.Db;
using Rihla.Services.ErpNext;
using Rihla.Services.Jwt;

namespace Rihla.Services.Auth;

/// <summary>
/// Auth flow:
///   1. Employee sends email + password
///   2. We call ERPNext to verify credentials and fetch user info + roles
///   3. We upsert the local AppUser (linked to ERPNext username)
///   4. We issue a local JWT with role/department claims
///   5. Return access token + refresh token
///
/// On first login: ERPNext password is verified directly.
/// Subsequent logins: ERPNext password is still verified (single source of truth).
/// </summary>
public class AuthService : IAuthService
{
    private readonly IErpNextClient _erp;
    private readonly ICacheService _cache;
    private readonly IJwtService _jwt;
    private readonly JwtSettings _jwtSettings;
    private readonly IAppUserRepository _userRepo;
    private readonly ITokenRepository _tokenRepo;
    private readonly ILogger<AuthService> _logger;

    private const string RefreshPrefix = "refresh:";

    public AuthService(
        IErpNextClient erp,
        ICacheService cache,
        IJwtService jwt,
        IOptions<JwtSettings> jwtSettings,
        IAppUserRepository userRepo,
        ITokenRepository tokenRepo,
        ILogger<AuthService> logger)
    {
        _erp         = erp;
        _cache       = cache;
        _jwt         = jwt;
        _jwtSettings = jwtSettings.Value;
        _userRepo    = userRepo;
        _tokenRepo   = tokenRepo;
        _logger      = logger;
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            throw new ArgumentException("البريد الإلكتروني وكلمة المرور مطلوبان.");

        // 1. Verify against ERPNext
        var erpUser = await _erp.AuthenticateAsync(request.Email.Trim(), request.Password)
            ?? throw new UnauthorizedAccessException("بيانات الاعتماد غير صحيحة.");

        // 2. Build allowed modules from roles
        var allowedModules = DetermineModules(erpUser.Roles);
        var primaryRole    = DeterminePrimaryRole(erpUser.Roles);
        var modulesJson    = JsonSerializer.Serialize(allowedModules);

        // 3. Upsert local user
        var appUser = await _userRepo.UpsertAsync(
            erpNextUserId  : erpUser.Username,
            email          : erpUser.Email,
            name           : erpUser.FullName,
            department     : erpUser.Department,
            primaryRole    : primaryRole,
            allowedModules : modulesJson,
            phone          : erpUser.Phone,
            avatarUrl      : null);

        // 4. Issue JWT
        var (token, jti, expiresAt) = _jwt.GenerateToken(
            appUser.Id, appUser.Email, appUser.Name, primaryRole ?? "Employee",
            department: appUser.Department, erpNextUserId: appUser.ErpNextUserId);

        await _tokenRepo.CreateAsync(jti, appUser.Id, DateTime.UtcNow, expiresAt);

        // 5. Issue refresh token
        var refreshToken = Guid.NewGuid().ToString("N");
        var refreshTtl   = TimeSpan.FromDays(_jwtSettings.RefreshTokenDays);
        await _cache.SetAsync($"{RefreshPrefix}{refreshToken}", jti, refreshTtl);

        _logger.LogInformation("User {Email} logged in. Role={Role}", appUser.Email, primaryRole);

        return BuildResponse(token, refreshToken, expiresAt, appUser, primaryRole, allowedModules);
    }

    // ── Refresh Token ─────────────────────────────────────────────────────────

    public async Task<LoginResponseDto> RefreshTokenAsync(string refreshToken)
    {
        var jti = await _cache.GetAsync($"{RefreshPrefix}{refreshToken}");
        if (string.IsNullOrEmpty(jti))
            throw new UnauthorizedAccessException("انتهت صلاحية رمز التجديد أو أنه غير صالح.");

        var appToken = await _tokenRepo.GetByJtiAsync(jti);
        if (appToken is null || appToken.RevokedAt is not null)
            throw new UnauthorizedAccessException("تم إلغاء رمز التجديد.");

        var user = await _userRepo.GetByIdAsync(appToken.UserId)
            ?? throw new UnauthorizedAccessException("المستخدم غير موجود.");

        // Revoke old tokens
        await _tokenRepo.RevokeAsync(jti);
        await _cache.RemoveAsync($"{RefreshPrefix}{refreshToken}");

        // Issue new
        var modules = user.AllowedModules is not null
            ? JsonSerializer.Deserialize<List<string>>(user.AllowedModules) ?? []
            : new List<string>();

        var (newToken, newJti, expiresAt) = _jwt.GenerateToken(
            user.Id, user.Email, user.Name, user.PrimaryRole ?? "Employee",
            department: user.Department, erpNextUserId: user.ErpNextUserId);

        await _tokenRepo.CreateAsync(newJti, user.Id, DateTime.UtcNow, expiresAt);

        var newRefresh = Guid.NewGuid().ToString("N");
        await _cache.SetAsync($"{RefreshPrefix}{newRefresh}", newJti, TimeSpan.FromDays(_jwtSettings.RefreshTokenDays));

        return BuildResponse(newToken, newRefresh, expiresAt, user, user.PrimaryRole, modules);
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    public async Task LogoutAsync(string token)
    {
        var jti = _jwt.GetJti(token);
        if (!string.IsNullOrEmpty(jti))
        {
            await _tokenRepo.RevokeAsync(jti);
            _logger.LogInformation("Token revoked (jti={Jti})", jti);
        }
    }

    // ── Change Password ───────────────────────────────────────────────────────

    public async Task ChangePasswordAsync(int userId, string oldPassword, string newPassword)
    {
        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("المستخدم غير موجود.");

        // Verify old password against ERPNext
        var verified = await _erp.AuthenticateAsync(user.Email, oldPassword);
        if (verified is null)
            throw new ArgumentException("كلمة المرور الحالية غير صحيحة.");

        // ERPNext doesn't expose a change-password API easily without admin session,
        // so we store a local hash as a note (real password change must go through ERPNext UI/admin)
        // For now, just confirm and log.
        _logger.LogInformation("Password change requested for user {UserId} — must be done in ERPNext.", userId);

        throw new InvalidOperationException(
            "تغيير كلمة المرور يتم عبر لوحة تحكم ERPNext مباشرةً. يرجى التواصل مع المسؤول.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string? DeterminePrimaryRole(List<string> roles)
    {
        // Priority order — first match wins
        var priority = new[]
        {
            // ── Employee roles ──
            "System Manager", "Administrator",
            "Sales Manager", "Sales User", "Sales Master Manager",
            "Purchase Manager", "Purchase User",
            "Accounts Manager", "Accounts User",
            "Stock Manager", "Stock User",
            "HR Manager", "HR User",
            "Manufacturing Manager", "Manufacturing User",
            "Project Manager", "Projects User",
            "Employee Self Service",

            // ── Client roles (ERPNext Portal/Customer accounts) ──
            "Customer", "Portal User", "Website User"
        };

        foreach (var p in priority)
            if (roles.Contains(p, StringComparer.OrdinalIgnoreCase))
                return p;

        return roles.FirstOrDefault();
    }

    public static List<string> DetermineModules(List<string> roles)
    {
        var modules = new HashSet<string>();

        var roleModuleMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales Manager"]           = ["Sales", "CRM", "Customers"],
            ["Sales User"]              = ["Sales", "Customers"],
            ["Sales Master Manager"]    = ["Sales", "CRM", "Customers"],
            ["Purchase Manager"]        = ["Purchase", "Suppliers"],
            ["Purchase User"]           = ["Purchase"],
            ["Accounts Manager"]        = ["Accounts", "Finance"],
            ["Accounts User"]           = ["Accounts"],
            ["Stock Manager"]           = ["Stock", "Inventory"],
            ["Stock User"]              = ["Stock"],
            ["HR Manager"]              = ["HR", "Employees", "Payroll"],
            ["HR User"]                 = ["HR", "Employees"],
            ["Manufacturing Manager"]   = ["Manufacturing", "Stock"],
            ["Manufacturing User"]      = ["Manufacturing"],
            ["System Manager"]          = ["Sales", "Purchase", "Accounts", "Stock", "HR", "Manufacturing"],
            ["Administrator"]           = ["Sales", "Purchase", "Accounts", "Stock", "HR", "Manufacturing"],
            ["Employee Self Service"]   = ["HR"]
        };

        foreach (var role in roles)
        {
            if (roleModuleMap.TryGetValue(role, out var mods))
                foreach (var m in mods)
                    modules.Add(m);
        }

        return [.. modules];
    }

    private static LoginResponseDto BuildResponse(
        string token, string refreshToken, DateTime expiresAt,
        Models.Db.AppUser user, string? primaryRole, List<string> modules) => new()
    {
        AccessToken  = token,
        RefreshToken = refreshToken,
        ExpiresAt    = expiresAt,
        User = new UserInfoDto
        {
            Id             = user.Id,
            ErpNextUserId  = user.ErpNextUserId,
            Email          = user.Email,
            Name           = user.Name,
            PrimaryRole    = primaryRole ?? "Employee",
            Department     = user.Department,
            AllowedModules = modules,
            AvatarUrl      = user.AvatarUrl,
            Phone          = user.Phone,
            Language       = user.Language
        }
    };

}
