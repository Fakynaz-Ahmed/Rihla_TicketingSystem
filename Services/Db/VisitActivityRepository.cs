using Microsoft.EntityFrameworkCore;
using Rihla.Data;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public class VisitActivityRepository : IVisitActivityRepository
{
    private readonly AppDbContext _db;

    public VisitActivityRepository(AppDbContext db) => _db = db;

    public async Task<DbVisitActivity> AddAsync(DbVisitActivity activity)
    {
        _db.VisitActivities.Add(activity);
        await _db.SaveChangesAsync();
        return activity;
    }

    public async Task<List<DbVisitActivity>> GetByVisitAsync(int visitId) =>
        await _db.VisitActivities
            .Where(a => a.VisitId == visitId)
            .OrderByDescending(a => a.Date)
            .ToListAsync();
}
