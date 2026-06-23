using Microsoft.EntityFrameworkCore;
using Rihla.Data;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public class DeviceTokenRepository : IDeviceTokenRepository
{
    private readonly AppDbContext _db;

    public DeviceTokenRepository(AppDbContext db) => _db = db;

    public async Task UpdateLastActivityAsync(int odooUserId)
    {
        // empty stub if needed
    }

    public async Task UpsertAsync(int userId, string token, string platform)
    {
        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == token);

        if (existing is not null)
        {
            existing.Platform  = platform;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.DeviceTokens.Add(new DeviceToken
            {
                UserId   = userId,
                Token    = token,
                Platform = platform
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task RemoveAsync(int userId, string token)
    {
        var row = await _db.DeviceTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == token);
        if (row is null) return;
        _db.DeviceTokens.Remove(row);
        await _db.SaveChangesAsync();
    }

    public Task<List<string>> GetByUserIdAsync(int userId) =>
        _db.DeviceTokens
           .Where(t => t.UserId == userId)
           .Select(t => t.Token)
           .ToListAsync();

    public async Task<List<string>> GetByUserIdsAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.ToList();
        return await _db.DeviceTokens
            .Where(t => ids.Contains(t.UserId))
            .Select(t => t.Token)
            .ToListAsync();
    }
}
