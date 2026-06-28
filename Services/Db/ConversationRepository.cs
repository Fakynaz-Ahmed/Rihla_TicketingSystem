using Microsoft.EntityFrameworkCore;
using Rihla.Data;
using Rihla.DTOs;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public class ConversationRepository : IConversationRepository
{
    private readonly AppDbContext _db;

    public ConversationRepository(AppDbContext db) => _db = db;

    public Task<DbConversation?> GetByIdAsync(string conversationId) =>
        _db.Conversations
           .Include(c => c.User)
           .FirstOrDefaultAsync(c => c.ConversationId == conversationId);

    public Task<List<DbConversation>> GetByUserIdAsync(int userId, int page, int pageSize) =>
        _db.Conversations
           .Where(c => c.UserId == userId)
           .OrderByDescending(c => c.CreatedAt)
           .Skip((page - 1) * pageSize)
           .Take(pageSize)
           .ToListAsync();

    public Task<int> CountByUserIdAsync(int userId) =>
        _db.Conversations.CountAsync(c => c.UserId == userId);

    public async Task<DbConversation> CreateAsync(
        string conversationId, int userId, string userName, string? aiThreadId = null)
    {
        var conv = new DbConversation
        {
            ConversationId = conversationId,
            UserId         = userId,
            UserName       = userName,
            AiThreadId     = aiThreadId,
            Status         = "active",
            CreatedAt      = DateTime.UtcNow
        };
        _db.Conversations.Add(conv);
        await _db.SaveChangesAsync();
        return conv;
    }

    public async Task UpdateAiThreadIdAsync(string conversationId, string aiThreadId)
    {
        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv is null) return;
        conv.AiThreadId = aiThreadId;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateTitleAsync(string conversationId, string title)
    {
        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv is null) return;
        conv.Title = title;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateStatusAsync(string conversationId, string status, DateTime? endedAt = null)
    {
        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv is null) return;
        conv.Status  = status;
        conv.EndedAt = endedAt;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateStatusExtendedAsync(string conversationId, string status,
        string? escalationReason = null, DateTime? escalatedAt = null,
        int? supportId = null, string? supportName = null,
        int? specialistId = null, string? specialistName = null,
        DateTime? endedAt = null)
    {
        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv is null) return;

        conv.Status = status;
        if (escalationReason is not null) conv.EscalationReason = escalationReason;
        if (escalatedAt.HasValue) conv.EscalatedAt = escalatedAt;
        if (supportId.HasValue) conv.SupportId = supportId;
        if (supportName is not null) conv.SupportName = supportName;
        if (specialistId.HasValue) conv.SpecialistId = specialistId;
        if (specialistName is not null) conv.SpecialistName = specialistName;
        if (endedAt.HasValue) conv.EndedAt = endedAt;

        await _db.SaveChangesAsync();
    }

    public async Task ReopenConversationAsync(string conversationId)
    {
        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv is null) return;

        conv.Status = "ai";
        conv.SupportId = null;
        conv.SupportName = null;
        conv.SpecialistId = null;
        conv.SpecialistName = null;
        conv.EscalationReason = null;
        conv.EscalatedAt = null;
        conv.EndedAt = null;

        await _db.SaveChangesAsync();
    }

    public Task<DbConversation?> GetLastByUserIdAsync(int userId) =>
        _db.Conversations
           .Where(c => c.UserId == userId)
           .OrderByDescending(c => c.CreatedAt)
           .FirstOrDefaultAsync();

    public async Task<Dictionary<int, int>> GetActiveCountBySupportIdsAsync(List<int> agentIds)
    {
        var activeCounts = await _db.Conversations
            .Where(c => c.SupportId != null && agentIds.Contains(c.SupportId.Value) && c.Status != "ended")
            .GroupBy(c => c.SupportId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

        foreach (var id in agentIds)
        {
            if (!activeCounts.ContainsKey(id))
                activeCounts[id] = 0;
        }

        return activeCounts;
    }

    public async Task<Dictionary<int, int>> GetActiveCountBySpecialistIdsAsync(List<int> agentIds)
    {
        var activeCounts = await _db.Conversations
            .Where(c => c.SpecialistId != null && agentIds.Contains(c.SpecialistId.Value) && c.Status != "ended")
            .GroupBy(c => c.SpecialistId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

        foreach (var id in agentIds)
        {
            if (!activeCounts.ContainsKey(id))
                activeCounts[id] = 0;
        }

        return activeCounts;
    }

    public Task<List<DbConversation>> GetSupportConversationsAsync(int supportUserId, int page, int pageSize) =>
        _db.Conversations
           .Include(c => c.User)
           .Where(c => c.SupportId == supportUserId || (c.SupportId == null && (c.Status == nameof(ConversationStatus.pending_cc) || c.Status == nameof(ConversationStatus.with_cc))))
           .OrderByDescending(c => c.CreatedAt)
           .Skip((page - 1) * pageSize)
           .Take(pageSize)
           .ToListAsync();

    public Task<int> CountSupportConversationsAsync(int supportUserId) =>
        _db.Conversations
           .CountAsync(c => c.SupportId == supportUserId || (c.SupportId == null && (c.Status == nameof(ConversationStatus.pending_cc) || c.Status == nameof(ConversationStatus.with_cc))));

    public Task<List<DbConversation>> GetSpecialistConversationsAsync(int specialistUserId, int page, int pageSize) =>
        _db.Conversations
           .Include(c => c.User)
           .Where(c => c.SpecialistId == specialistUserId || (c.SpecialistId == null && (c.Status == nameof(ConversationStatus.pending_specialist) || c.Status == nameof(ConversationStatus.with_specialist))))
           .OrderByDescending(c => c.CreatedAt)
           .Skip((page - 1) * pageSize)
           .Take(pageSize)
           .ToListAsync();

    public Task<int> CountSpecialistConversationsAsync(int specialistUserId) =>
        _db.Conversations
           .CountAsync(c => c.SpecialistId == specialistUserId || (c.SpecialistId == null && (c.Status == nameof(ConversationStatus.pending_specialist) || c.Status == nameof(ConversationStatus.with_specialist))));

    public async Task DeleteAsync(string conversationId)
    {
        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv is null) return;
        _db.Conversations.Remove(conv);
        await _db.SaveChangesAsync();
    }

    public Task<bool> IsAssignedAsSupportAsync(int userId) =>
        _db.Conversations.AnyAsync(c => c.SupportId == userId && c.Status != "ended");

    public Task<bool> IsAssignedAsSpecialistAsync(int userId) =>
        _db.Conversations.AnyAsync(c => c.SpecialistId == userId && c.Status != "ended");
}

