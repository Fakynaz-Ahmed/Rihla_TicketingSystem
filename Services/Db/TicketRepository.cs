using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rihla.Config;
using Rihla.Data;
using Rihla.DTOs;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public class TicketRepository : ITicketRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<TicketRepository> _logger;

    public TicketRepository(AppDbContext db, ILogger<TicketRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task UpsertManyAsync(IEnumerable<DbTicket> tickets)
    {
        var incoming = tickets.ToList();
        if (incoming.Count == 0) return;

        var erpNextIds = incoming.Select(t => t.ErpNextId).ToHashSet();
        var existing = await _db.Tickets
            .Where(t => erpNextIds.Contains(t.ErpNextId))
            .ToDictionaryAsync(t => t.ErpNextId);

        var now = DateTime.UtcNow;
        foreach (var t in incoming)
        {
            if (existing.TryGetValue(t.ErpNextId, out var row))
            {
                row.CustomerErpNextUserId = t.CustomerErpNextUserId;
                row.Title     = t.Title;
                row.Status    = t.Status;  // sync status from ERPNext
                row.ProductErpNextId = t.ProductErpNextId;
                row.ProductName      = t.ProductName;
                row.WriteDate  = t.WriteDate;
                row.CreateDate = t.CreateDate;
                row.SyncedAt   = now;
            }
            else
            {
                t.SyncedAt = now;
                _db.Tickets.Add(t);
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<DbTicket>> GetByCustomerAsync(string customerErpNextUserId) =>
             await _db.Tickets
            .Where(t => t.CustomerErpNextUserId == customerErpNextUserId)
            .OrderByDescending(t => t.CreateDate)
            .ToListAsync();
    
    public async Task<DbTicket?> GetByErpNextIdAsync(string erpNextId) =>
            await _db.Tickets
            .Include(t => t.Specialist)
            .Include(t => t.Support)
            .FirstOrDefaultAsync(t => t.ErpNextId == erpNextId);
    

    // ── Filtered paginated queries ────────────────────────────────────────────

    public Task<(List<DbTicket> Items, int Total)> GetAllFilteredAsync(TicketFilterDto filter) =>
        QueryFiltered(_db.Tickets.Include(t => t.Specialist).Include(t => t.Support), filter);

    public Task<(List<DbTicket> Items, int Total)> GetByCustomerFilteredAsync(string customerErpNextUserId, TicketFilterDto filter) =>
        QueryFiltered(_db.Tickets.Where(t => t.CustomerErpNextUserId == customerErpNextUserId), filter);

    public Task<(List<DbTicket> Items, int Total)> GetBySupportFilteredAsync(int supportAppUserId, TicketFilterDto filter) =>
        QueryFiltered(_db.Tickets
            .Include(t => t.Specialist)
            .Include(t => t.Support)
            .Where(t => t.SupportAppUserId == supportAppUserId), filter);

    public Task<(List<DbTicket> Items, int Total)> GetBySpecialistFilteredAsync(int specialistAppUserId, TicketFilterDto filter) =>
        QueryFiltered(_db.Tickets
            .Include(t => t.Specialist)
            .Include(t => t.Support)
            .Where(t => t.SpecialistAppUserId == specialistAppUserId), filter);

    private static async Task<(List<DbTicket> Items, int Total)> QueryFiltered(
        IQueryable<DbTicket> query, TicketFilterDto f)
    {
        if (!string.IsNullOrEmpty(f.Status)) query = query.Where(t => t.Status == f.Status);
        if (!string.IsNullOrEmpty(f.Priority)) query = query.Where(t => t.Priority == f.Priority);
        if (f.From.HasValue) query = query.Where(t => t.CreateDate >= f.From.Value);
        if (f.To.HasValue) query = query.Where(t => t.CreateDate <= f.To.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.CreateDate)
            .Skip((f.Page - 1) * f.PageSize)
            .Take(f.PageSize)
            .AsNoTracking()
            .ToListAsync();

        return (items, total);
    }

    // ── Support ticket CRUD ───────────────────────────────────────────────────

    public async Task<DbTicket> CreateAsync(DbTicket ticket)
    {
        ticket.SyncedAt = DateTime.UtcNow;
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ticket;
    }

    public Task<DbTicket?> GetByIdAsync(int id) =>
        _db.Tickets
           .Include(t => t.Specialist)
           .Include(t => t.Support)
           .AsNoTracking()
           .FirstOrDefaultAsync(t => t.Id == id);

    public Task<DbTicket?> GetByConversationIdAsync(string conversationId) =>
        _db.Tickets
           .Include(t => t.Specialist)
           .Include(t => t.Support)
           .Where(t => t.ConversationId == conversationId)
           .OrderByDescending(t => t.Id)
           .FirstOrDefaultAsync();

    public Task<DbTicket?> GetLastByConversationIdAsync(string conversationId) =>
        _db.Tickets
           .Where(t => t.ConversationId == conversationId)
           .OrderByDescending(t => t.Id)
           .FirstOrDefaultAsync();

    public Task<int?> GetLastVisitIdByConversationIdAsync(string conversationId) =>
        _db.Tickets
           .Where(t => t.ConversationId == conversationId)
           .Join(_db.Visits, t => t.Id, v => v.TicketId, (t, v) => (int?)v.Id)
           .OrderByDescending(v => v)
           .FirstOrDefaultAsync();

    public async Task UpdateAssignmentsAsync(string conversationId, int? supportAppUserId, int? specialistAppUserId)
    {
        var ticket = await _db.Tickets
            .Where(t => t.ConversationId == conversationId)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync();
        if (ticket is null) return;
        if (supportAppUserId.HasValue) ticket.SupportAppUserId = supportAppUserId;
        if (specialistAppUserId.HasValue) ticket.SpecialistAppUserId = specialistAppUserId;
        ticket.SyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(int id, string? status, string? priority)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return;

        if (status is not null) ticket.Status = status;
        if (priority is not null) ticket.Priority = priority;

        ticket.SyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateTitleAsync(int id, string title)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return;
        ticket.Title = title;
        ticket.SyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateVisitIdAsync(int ticketId, int visitId)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId);
        if (ticket is null) return;
        ticket.VisitId = visitId;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateSpecialistAsync(int ticketId, int specialistAppUserId)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId);
        if (ticket is null) return;
        ticket.SpecialistAppUserId = specialistAppUserId;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateStatusByConversationIdAsync(string conversationId, string status)
    {
        var ticket = await _db.Tickets
            .Where(t => t.ConversationId == conversationId)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync();
        if (ticket is null) return;

        ticket.Status   = status;
        ticket.SyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var ticket = await _db.Tickets.FindAsync(id);
        if (ticket is null) return;
        _db.Tickets.Remove(ticket);
        await _db.SaveChangesAsync();
    }

    public Task<int> GetActiveCountBySpecialistAsync(int specialistAppUserId) =>
        _db.Tickets
           .CountAsync(t => t.SpecialistAppUserId == specialistAppUserId
                         && t.Status == TicketStatus.Open
                         || (t.SpecialistAppUserId == specialistAppUserId
                         && t.Status == TicketStatus.Replied));

    public Task<int> GetActiveCountBySupportAsync(int supportAppUserId) =>
        _db.Tickets
           .CountAsync(t => t.SupportAppUserId == supportAppUserId
                         && t.Status == TicketStatus.Open
                         || (t.SupportAppUserId == supportAppUserId
                         && t.Status == TicketStatus.Replied));

    public async Task UpdateStatusByTicketIdAsync(int ticketId, string status)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId);
        if (ticket is null) return;
        ticket.Status   = status;
        ticket.SyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UnlinkConversationAsync(string conversationId)
    {
        await _db.Tickets
            .Where(t => t.ConversationId == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConversationId, (string?)null));
    }

    public Task CloseTicketStatusAsync(string conversationId) =>
        _db.Tickets
           .Where(t => t.ConversationId == conversationId)
           .ExecuteUpdateAsync(s => s
               .SetProperty(t => t.Status, TicketStatus.Resolved)
               .SetProperty(t => t.SyncedAt, DateTime.UtcNow));

    public async Task<Dictionary<string, float?>> GetRatingsByConversationIdsAsync(IEnumerable<string> conversationIds)
    {
        var ids = conversationIds.ToList();
        return await _db.Tickets
            .Where(t => t.ConversationId != null && ids.Contains(t.ConversationId))
            .GroupBy(t => t.ConversationId!)
            .Select(g => new { ConversationId = g.Key, Rating = g.OrderByDescending(t => t.Id).First().TicketRating })
            .ToDictionaryAsync(x => x.ConversationId, x => x.Rating);
    }

    public async Task SaveRatingAsync(int id, float rating, string? feedback)
    {
        await _db.Tickets
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.TicketRating, rating)
                .SetProperty(t => t.TicketRatingFeedback, feedback)
                .SetProperty(t => t.TicketRatedAt, DateTime.UtcNow));
    }
}
