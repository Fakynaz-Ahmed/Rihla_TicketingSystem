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
    
    Task UpdateStatusExtendedAsync(string conversationId, string status,
        string? escalationReason = null, DateTime? escalatedAt = null,
        int? supportId = null, string? supportName = null,
        int? specialistId = null, string? specialistName = null,
        DateTime? endedAt = null);

    Task ReopenConversationAsync(string conversationId);
    Task<DbConversation?> GetLastByUserIdAsync(int userId);
    Task<Dictionary<int, int>> GetActiveCountBySupportIdsAsync(List<int> agentIds);
    Task<Dictionary<int, int>> GetActiveCountBySpecialistIdsAsync(List<int> agentIds);

    Task<List<DbConversation>> GetSupportConversationsAsync(int supportUserId, int page, int pageSize);
    Task<int> CountSupportConversationsAsync(int supportUserId);
    Task<List<DbConversation>> GetSpecialistConversationsAsync(int specialistUserId, int page, int pageSize);
    Task<int> CountSpecialistConversationsAsync(int specialistUserId);

    Task DeleteAsync(string conversationId);

    /// <summary>Returns true if the user is assigned as SupportId in any non-ended conversation.</summary>
    Task<bool> IsAssignedAsSupportAsync(int userId);

    /// <summary>Returns true if the user is assigned as SpecialistId in any non-ended conversation.</summary>
    Task<bool> IsAssignedAsSpecialistAsync(int userId);
}

