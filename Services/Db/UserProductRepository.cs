using Microsoft.EntityFrameworkCore;
using Rihla.Data;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public class UserProductRepository : IUserProductRepository
{
    private readonly AppDbContext _db;

    public UserProductRepository(AppDbContext db) => _db = db;

    public async Task UpsertManyAsync(IEnumerable<DbUserProduct> userProducts)
    {
        var incoming = userProducts.ToList();
        if (incoming.Count == 0) return;

        var lineNames = incoming.Select(up => up.ErpNextLineName).ToHashSet();
        var existing = await _db.UserProducts
            .Where(up => lineNames.Contains(up.ErpNextLineName))
            .ToDictionaryAsync(up => up.ErpNextLineName);

        var now = DateTime.UtcNow;
        foreach (var up in incoming)
        {
            if (existing.TryGetValue(up.ErpNextLineName, out var row))
            {
                row.CustomerErpNextUserId = up.CustomerErpNextUserId;
                row.ProductId = up.ProductId;
                row.ItemCode = up.ItemCode;
                row.Name = up.Name;
                row.OrderReference = up.OrderReference;
                row.OrderDate = up.OrderDate;
                row.Price = up.Price;
                row.Currency = up.Currency;
                row.SyncedAt = now;
            }
            else
            {
                up.SyncedAt = now;
                _db.UserProducts.Add(up);
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<DbUserProduct>> GetByCustomerAsync(string customerErpNextUserId) =>
        await _db.UserProducts
            .Include(up => up.Product)
            .Where(up => up.CustomerErpNextUserId == customerErpNextUserId)
            .OrderByDescending(up => up.OrderDate)
            .ToListAsync();

    public async Task<DbUserProduct?> GetByLineNameAndCustomerAsync(string lineName, string customerErpNextUserId) =>
        await _db.UserProducts
            .Include(up => up.Product)
            .FirstOrDefaultAsync(up => up.ErpNextLineName == lineName && up.CustomerErpNextUserId == customerErpNextUserId);

    public async Task<DbUserProduct?> GetByProductIdAndCustomerAsync(int productId, string customerErpNextUserId) =>
        await _db.UserProducts
            .Include(up => up.Product)
            .FirstOrDefaultAsync(up => up.ProductId == productId && up.CustomerErpNextUserId == customerErpNextUserId);

    public Task<bool> ProductOwnedByCustomerAsync(int productId, string customerErpNextUserId) =>
        _db.UserProducts.AnyAsync(up => up.ProductId == productId && up.CustomerErpNextUserId == customerErpNextUserId);
}
