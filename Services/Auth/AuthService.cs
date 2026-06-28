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

        // ── Path A: ERPNext is reachable ─────────────────────────────────────
        ErpNextUser? erpUser = null;
        bool erpReachable = true;

        try
        {
            erpUser = await _erp.AuthenticateAsync(request.Email.Trim(), request.Password);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ERPNext unreachable during login for {Email}. Falling back to local DB.", request.Email);
            erpReachable = false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "ERPNext timed out during login for {Email}. Falling back to local DB.", request.Email);
            erpReachable = false;
        }

        if (erpReachable)
        {
            // ERPNext was reachable — null means wrong credentials (not a network error)
            if (erpUser is null)
                throw new UnauthorizedAccessException("بيانات الاعتماد غير صحيحة.");

            // Build role & modules from live ERPNext data
            var allowedModules = DetermineModules(erpUser.Roles);
            var primaryRole    = DeterminePrimaryRole(erpUser.Roles, erpUser.Department);
            var modulesJson    = JsonSerializer.Serialize(allowedModules);

            // Upsert local user with fresh ERPNext data
            var appUser = await _userRepo.UpsertAsync(
                erpNextUserId  : erpUser.Username,
                email          : erpUser.Email,
                name           : erpUser.FullName,
                department     : erpUser.Department,
                primaryRole    : primaryRole,
                allowedModules : modulesJson,
                phone          : erpUser.Phone,
                avatarUrl      : null);

            // Cache the password hash for offline fallback
            var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            await _userRepo.UpdatePasswordHashAsync(appUser.Id, hash);

            // Issue tokens
            var (token, jti, expiresAt) = _jwt.GenerateToken(
                appUser.Id, appUser.Email, appUser.Name, primaryRole ?? AppRoles.Employee,
                department: appUser.Department, erpNextUserId: appUser.ErpNextUserId);

            await _tokenRepo.CreateAsync(jti, appUser.Id, DateTime.UtcNow, expiresAt);

            var refreshToken = Guid.NewGuid().ToString("N");
            var refreshTtl   = TimeSpan.FromDays(_jwtSettings.RefreshTokenDays);
            await _cache.SetAsync($"{RefreshPrefix}{refreshToken}", jti, refreshTtl);

            _logger.LogInformation("User {Email} logged in via ERPNext. Role={Role}", appUser.Email, primaryRole);

            return BuildResponse(token, refreshToken, expiresAt, appUser, primaryRole, allowedModules);
        }

        // ── Path B: ERPNext offline — local DB fallback ───────────────────────
        var localUser = await _userRepo.GetByEmailAsync(request.Email.Trim())
            ?? throw new UnauthorizedAccessException("ERPNext غير متاح حالياً ولا يوجد حساب محلي مسجّل لهذا البريد.");

        if (string.IsNullOrEmpty(localUser.PasswordHash))
            throw new UnauthorizedAccessException("ERPNext غير متاح حالياً ولا توجد بيانات محلية محفوظة. يرجى المحاولة لاحقاً.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, localUser.PasswordHash))
            throw new UnauthorizedAccessException("بيانات الاعتماد غير صحيحة.");

        // Resolve cached role & modules
        var cachedModules = localUser.AllowedModules is not null
            ? JsonSerializer.Deserialize<List<string>>(localUser.AllowedModules) ?? []
            : new List<string>();

        var cachedRole = localUser.PrimaryRole ?? AppRoles.Employee;

        // Issue tokens from cached data (no UpsertAsync — ERPNext is offline)
        var (offlineToken, offlineJti, offlineExpiresAt) = _jwt.GenerateToken(
            localUser.Id, localUser.Email, localUser.Name, cachedRole,
            department: localUser.Department, erpNextUserId: localUser.ErpNextUserId);

        await _tokenRepo.CreateAsync(offlineJti, localUser.Id, DateTime.UtcNow, offlineExpiresAt);

        var offlineRefresh = Guid.NewGuid().ToString("N");
        await _cache.SetAsync(
            $"{RefreshPrefix}{offlineRefresh}",
            offlineJti,
            TimeSpan.FromDays(_jwtSettings.RefreshTokenDays));

        await _userRepo.UpdateLastLoginAsync(localUser.Id);

        _logger.LogWarning("User {Email} logged in via LOCAL FALLBACK (ERPNext offline). Role={Role}", localUser.Email, cachedRole);

        return BuildResponse(offlineToken, offlineRefresh, offlineExpiresAt, localUser, cachedRole, cachedModules);
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
            user.Id, user.Email, user.Name, user.PrimaryRole ?? AppRoles.Employee,
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

    /// <summary>
    /// Determines the single PrimaryRole stored in the DB and JWT claim.
    /// Rules (evaluated in order — first match wins):
    ///   1. Admin        → has "System Manager" or "Administrator" role
    ///   2. Specialist   → has "Specialist" role (regardless of dept)
    ///   3. CustomerCare → has any support role ("Support Team" / "Chat Support" / "Agent")
    ///                     Department is NOT required — rule 2 already filters Specialists,
    ///                     and Customers never hold support-level roles.
    ///   4. Customer     → has "Customer" / "Portal User" / "Website User" role
    ///   5. Employee     → fallback for all other ERPNext system-users
    /// </summary>
    public static string DeterminePrimaryRole(List<string> roles, string? department = null)
    {
        bool HasRole(string r) => roles.Contains(r, StringComparer.OrdinalIgnoreCase);

        // 1. Admin
        if (HasRole(ErpRoles.SystemManager) || HasRole(ErpRoles.Administrator))
            return AppRoles.Admin;

        // 2. Specialist — the custom "Specialist" role is the only distinguishing signal
        if (HasRole(ErpRoles.Specialist))
            return AppRoles.Specialist;

        // 3. CustomerCare — any support-level role without the Specialist role above
        //    Department is informational but NOT a hard gate; users may have no Employee
        //    record in ERPNext (and thus a NULL dept) yet still be genuine support agents.
        if (HasRole(ErpRoles.SupportTeam)
         || HasRole(ErpRoles.ChatSupport)
         || HasRole(ErpRoles.Agent)
         || HasRole("Customer Care")
         || HasRole("CustomerCare")
         || HasRole("Support"))
            return AppRoles.CustomerCare;

        // 4. Customer (portal / website accounts)
        if (HasRole(ErpRoles.Customer)
         || HasRole(ErpRoles.PortalUser)
         || HasRole(ErpRoles.WebsiteUser))
            return AppRoles.Customer;

        // 5. Generic employee fallback
        return AppRoles.Employee;
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
            PrimaryRole    = primaryRole ?? AppRoles.Employee,
            Department     = user.Department,
            AllowedModules = modules,
            AvatarUrl      = user.AvatarUrl,
            Phone          = user.Phone,
            Language       = user.Language
        }
    };

}
