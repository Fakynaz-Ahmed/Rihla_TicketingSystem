using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rihla.Config;

namespace Rihla.Services.Jwt;

public class JwtService : IJwtService
{
    private readonly JwtSettings _settings;

    public JwtService(IOptions<JwtSettings> settings) => _settings = settings.Value;

    public (string token, string jti, DateTime expiresAt) GenerateToken(
        int userId, string email, string name, string primaryRole,
        string? department = null, string? erpNextUserId = null)
    {
        var jti       = Guid.NewGuid().ToString();
        var expiresAt = DateTime.UtcNow.AddDays(_settings.ExpirationDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Name, name),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("role", primaryRole)
        };

        if (!string.IsNullOrEmpty(department))
            claims.Add(new Claim("department", department));

        if (!string.IsNullOrEmpty(erpNextUserId))
            claims.Add(new Claim("erp_user", erpNextUserId));

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer            : _settings.Issuer,
            audience          : _settings.Audience,
            claims            : claims,
            notBefore         : DateTime.UtcNow,
            expires           : expiresAt,
            signingCredentials : creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), jti, expiresAt);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = key,
            ValidateIssuer           = true,
            ValidIssuer              = _settings.Issuer,
            ValidateAudience         = true,
            ValidAudience            = _settings.Audience,
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.Zero
        };

        try { return new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _); }
        catch { return null; }
    }

    public string? GetJti(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
        }
        catch { return null; }
    }
}
