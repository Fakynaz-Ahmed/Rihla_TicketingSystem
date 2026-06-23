using Rihla.Models.Db;

namespace Rihla.Services.Db;

public interface IConversationRepository
{
    Task<DbConversation?> GetByIdAsync(string conversationId);
    Task<List<DbConversation>> GetByUserIdAsync(int userId, int page, int pageSize);
    Task<int> CountByUserIdAsync(int userId);
    Task<DbConversation> CreateAsync(string conversationId, int userId, string userName, string? aiThreadId = null);
    Task UpdateAiThreadIdAsync(string conversationId, string aiThreadId);
    Task UpdateTitleAsync(string conversationId, string title);
    Task UpdateStatusAsync(string conversationId, string status, DateTime? endedAt = null);
    Task DeleteAsync(string conversationId);
}
