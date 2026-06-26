using Rihla.Models.Db;

namespace Rihla.Services.Db;

public interface IVisitActivityRepository
{
    Task<DbVisitActivity> AddAsync(DbVisitActivity activity);
    Task<List<DbVisitActivity>> GetByVisitAsync(int visitId);
}
