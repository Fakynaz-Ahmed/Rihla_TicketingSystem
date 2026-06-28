using Microsoft.EntityFrameworkCore;
using Rihla.Data;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;

    public ProductRepository(AppDbContext db) => _db = db;

    public async Task UpsertManyAsync(IEnumerable<DbProduct> products)
    {
        var incoming = products.ToList();
        if (incoming.Count == 0) return;

        var erpNextIds = incoming.Select(p => p.ErpNextId).ToHashSet();
        var existing = await _db.Products
            .Where(p => erpNextIds.Contains(p.ErpNextId))
            .ToDictionaryAsync(p => p.ErpNextId);

        var now = DateTime.UtcNow;
        foreach (var p in incoming)
        {
            if (existing.TryGetValue(p.ErpNextId, out var row))
            {
                row.Name = p.Name;
                row.DisplayName = p.DisplayName;
                row.ItemCode = p.ItemCode;
                row.Description = p.Description;
                row.ListPrice = p.ListPrice;
                row.Category = p.Category;
                row.Active = p.Active;
                row.SyncedAt = now;
                if (p.ImageUrl is not null) row.ImageUrl = p.ImageUrl;
            }
            else
            {
                p.SyncedAt = now;
                _db.Products.Add(p);
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<DbProduct>> GetAllAsync() =>
        await _db.Products.OrderBy(p => p.Name).ToListAsync();

    public async Task<(List<DbProduct> Items, int Total)> GetFilteredAsync(
        string? search, string? category, int page, int pageSize)
    {
        var q = _db.Products.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p => p.Name.Contains(search) || p.ItemCode.Contains(search) || p.DisplayName.Contains(search));

        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(p => p.Category == category);

        var total = await q.CountAsync();
        var items = await q.OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<List<DbProduct>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.ToList();
        return await _db.Products.Where(p => list.Contains(p.Id)).ToListAsync();
    }

    public async Task<List<DbProduct>> GetByErpNextIdsAsync(IEnumerable<string> erpNextIds)
    {
        var list = erpNextIds.ToList();
        return await _db.Products.Where(p => list.Contains(p.ErpNextId)).ToListAsync();
    }

    public Task<DbProduct?> GetByIdAsync(int id) =>
        _db.Products.FirstOrDefaultAsync(p => p.Id == id);

    public Task<DbProduct?> GetByErpNextIdAsync(string erpNextId) =>
        _db.Products.FirstOrDefaultAsync(p => p.ErpNextId == erpNextId);

    public async Task<bool> DeleteByErpNextIdAsync(string erpNextId)
    {
        var row = await _db.Products.FirstOrDefaultAsync(p => p.ErpNextId == erpNextId);
        if (row is null) return false;
        _db.Products.Remove(row);
        await _db.SaveChangesAsync();
        return true;
    }
}
