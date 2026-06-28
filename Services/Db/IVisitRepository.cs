using Rihla.Models.Db;

namespace Rihla.Services.Db;

public interface IVisitRepository
{
    Task<DbVisit> CreateAsync(DbVisit visit);
    Task<DbVisit?> GetByIdAsync(int id);
    Task<DbVisit?> GetByErpNextIdAsync(string erpNextId);
    Task UpdateErpNextIdAsync(int visitId, string erpNextId);
    Task UpdateAsync(DbVisit visit);
    Task UpsertManyAsync(IEnumerable<DbVisit> visits);
    Task<List<DbVisit>> GetAllAsync();
    Task<List<DbVisit>> GetByCustomerAsync(string customerErpNextUserId);
    Task<List<DbVisit>> GetByAssignedUserAsync(int userId);
    Task<(List<DbVisit> Items, int Total)> GetByAssignedUserFilteredAsync(
        int userId, string? status, string? priority, DateTime? from, DateTime? to, int page, int pageSize);
    Task SaveRatingAsync(int id, float rating, string? feedback);
}
