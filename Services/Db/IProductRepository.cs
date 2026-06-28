using Rihla.Models.Db;

namespace Rihla.Services.Db;

public interface IProductRepository
{
    Task UpsertManyAsync(IEnumerable<DbProduct> products);
    Task<List<DbProduct>> GetAllAsync();
    Task<(List<DbProduct> Items, int Total)> GetFilteredAsync(
        string? search, string? category, int page, int pageSize);
    Task<List<DbProduct>> GetByIdsAsync(IEnumerable<int> ids);
    Task<List<DbProduct>> GetByErpNextIdsAsync(IEnumerable<string> erpNextIds);
    Task<DbProduct?> GetByIdAsync(int id);
    Task<DbProduct?> GetByErpNextIdAsync(string erpNextId);
    Task<bool> DeleteByErpNextIdAsync(string erpNextId);
}
