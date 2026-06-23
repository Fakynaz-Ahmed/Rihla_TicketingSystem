using Rihla.Models.Mongo;

namespace Rihla.Services.Db;

public interface IMessageRepository
{
    Task<MongoMessage> AddAsync(string conversationId, string role, string content,
        string? attachmentUrl = null, string? attachmentType = null);

    Task<List<MongoMessage>> GetByConversationAsync(string conversationId, int limit = 100);
    Task<List<MongoMessage>> GetByConversationIdsAsync(List<string> conversationIds);
    Task DeleteByConversationAsync(string conversationId);
}
