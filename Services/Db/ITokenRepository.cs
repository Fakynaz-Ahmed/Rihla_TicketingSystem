using Rihla.Models.Db;

namespace Rihla.Services.Db;

public interface ITokenRepository
{
    Task<AppToken> CreateAsync(string jti, int userId, DateTime issuedAt, DateTime expiresAt);
    Task<AppToken?> GetByJtiAsync(string jti);
    Task<bool> IsActiveAsync(string jti);
    Task RevokeAsync(string jti);
    Task RevokeAllByUserIdAsync(int userId);
}
