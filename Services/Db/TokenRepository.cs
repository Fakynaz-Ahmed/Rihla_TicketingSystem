using Microsoft.EntityFrameworkCore;
using Rihla.Data;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public class TokenRepository : ITokenRepository
{
    private readonly AppDbContext _db;

    public TokenRepository(AppDbContext db) => _db = db;

    public async Task<AppToken> CreateAsync(string jti, int userId, DateTime issuedAt, DateTime expiresAt)
    {
        var token = new AppToken
        {
            Jti = jti,
            UserId = userId,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt
        };
        _db.Tokens.Add(token);
        await _db.SaveChangesAsync();
        return token;
    }

    public Task<AppToken?> GetByJtiAsync(string jti) =>
        _db.Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.Jti == jti);

    public async Task<bool> IsActiveAsync(string jti)
    {
        var token = await _db.Tokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Jti == jti);

        return token is not null
            && token.RevokedAt is null
            && token.ExpiresAt > DateTime.UtcNow;
    }

    public async Task RevokeAsync(string jti)
    {
        var token = await _db.Tokens.FirstOrDefaultAsync(t => t.Jti == jti);
        if (token is not null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task RevokeAllByUserIdAsync(int userId)
    {
        var now = DateTime.UtcNow;
        var active = await _db.Tokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync();

        foreach (var t in active)
            t.RevokedAt = now;

        if (active.Count > 0)
            await _db.SaveChangesAsync();
    }
}
