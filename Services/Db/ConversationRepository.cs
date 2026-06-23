using Microsoft.EntityFrameworkCore;
using Rihla.Data;
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

    public async Task DeleteAsync(string conversationId)
    {
        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv is null) return;
        _db.Conversations.Remove(conv);
        await _db.SaveChangesAsync();
    }
}
