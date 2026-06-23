using System.Security.Claims;

namespace Rihla.Services.Jwt;

public interface IJwtService
{
    (string token, string jti, DateTime expiresAt) GenerateToken(
        int userId, string email, string name, string primaryRole,
        string? department = null, string? erpNextUserId = null);

    ClaimsPrincipal? ValidateToken(string token);
    string? GetJti(string token);
}
