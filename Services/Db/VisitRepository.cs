using Microsoft.EntityFrameworkCore;
using Rihla.Data;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public class VisitRepository : IVisitRepository
{
    private readonly AppDbContext _db;

    public VisitRepository(AppDbContext db) => _db = db;

    public async Task<DbVisit> CreateAsync(DbVisit visit)
    {
        _db.Visits.Add(visit);
        await _db.SaveChangesAsync();
        return visit;
    }

    public async Task<DbVisit?> GetByIdAsync(int id) =>
        await _db.Visits
            .Include(v => v.Ticket)
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == id);

    public async Task<DbVisit?> GetByErpNextIdAsync(string erpNextId) =>
        await _db.Visits.FirstOrDefaultAsync(v => v.ErpNextId == erpNextId);

    public async Task UpdateAsync(DbVisit visit)
    {
        _db.Visits.Update(visit);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateErpNextIdAsync(int visitId, string erpNextId)
    {
        var row = await _db.Visits.FindAsync(visitId);
        if (row is null) return;
        row.ErpNextId = erpNextId;
        await _db.SaveChangesAsync();
    }

    public async Task UpsertManyAsync(IEnumerable<DbVisit> visits)
    {
        var incoming = visits.ToList();
        if (incoming.Count == 0) return;

        var erpNextIds = incoming.Select(v => v.ErpNextId).ToHashSet();
        var existing = await _db.Visits
            .Where(v => erpNextIds.Contains(v.ErpNextId))
            .ToDictionaryAsync(v => v.ErpNextId);

        var now = DateTime.UtcNow;
        foreach (var v in incoming)
        {
            if (existing.TryGetValue(v.ErpNextId, out var row))
            {
                row.CustomerErpNextUserId = v.CustomerErpNextUserId;
                row.AssignedUserId = v.AssignedUserId;
                row.AssignedUserName = v.AssignedUserName;
                row.PartnerName = v.PartnerName;
                row.Name = v.Name;
                row.Stage = v.Stage;
                row.Priority = v.Priority;
                row.IsDone = v.IsDone;
                row.PlannedStart = v.PlannedStart;
                row.PlannedEnd = v.PlannedEnd;
                row.Deadline = v.Deadline;
                row.Description = v.Description;
                row.TagsJson = v.TagsJson;
                row.CreateDate = v.CreateDate;
                row.WriteDate = v.WriteDate;
                row.SyncedAt = now;
            }
            else
            {
                v.SyncedAt = now;
                _db.Visits.Add(v);
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<DbVisit>> GetAllAsync() =>
        await _db.Visits
            .Include(v => v.Ticket)
            .Include(v => v.Product)
            .OrderByDescending(v => v.PlannedStart ?? v.WriteDate)
            .ToListAsync();

    public async Task<List<DbVisit>> GetByCustomerAsync(string customerErpNextUserId) =>
        await _db.Visits
            .Include(v => v.Ticket)
            .Include(v => v.Product)
            .Where(v => v.CustomerErpNextUserId == customerErpNextUserId)
            .OrderByDescending(v => v.PlannedStart ?? v.WriteDate)
            .ToListAsync();

    public async Task<List<DbVisit>> GetByAssignedUserAsync(int userId) =>
        await _db.Visits
            .Include(v => v.Ticket)
            .Include(v => v.Product)
            .Where(v => v.AssignedUserId == userId)
            .OrderByDescending(v => v.PlannedStart ?? v.WriteDate)
            .ToListAsync();

    public async Task<(List<DbVisit> Items, int Total)> GetByAssignedUserFilteredAsync(
        int userId, string? status, string? priority, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var query = _db.Visits
            .Include(v => v.Ticket)
            .Include(v => v.Product)
            .Where(v => v.AssignedUserId == userId)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = status switch
            {
                "done" => query.Where(v => v.IsDone),
                "cancelled" => query.Where(v => v.IsCancelled),
                "in_progress" => query.Where(v => !v.IsDone && !v.IsCancelled &&
                    (v.Stage.ToLower().Contains("progress") || v.Stage.ToLower().Contains("ongoing"))),
                "new" => query.Where(v => !v.IsDone && !v.IsCancelled &&
                    (v.Stage.ToLower().Contains("new") || v.Stage.ToLower() == "new")),
                _ => query
            };

        if (!string.IsNullOrEmpty(priority))
            query = query.Where(v => v.Priority == (priority == "high" ? "1" : "0"));

        if (from.HasValue)
            query = query.Where(v => v.PlannedStart >= from.Value || v.VisitDate >= from.Value);

        if (to.HasValue)
            query = query.Where(v => v.PlannedStart <= to.Value || v.VisitDate <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(v => v.PlannedStart ?? v.WriteDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task SaveRatingAsync(int id, float rating, string? feedback)
    {
        await _db.Visits
            .Where(v => v.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.VisitRating, rating)
                .SetProperty(v => v.VisitRatingFeedback, feedback)
                .SetProperty(v => v.VisitRatedAt, DateTime.UtcNow));
    }
}
