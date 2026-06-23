using MongoDB.Driver;
using Rihla.Models.Mongo;
using Microsoft.Extensions.Configuration;

namespace Rihla.Services.Db;

public class MessageRepository : IMessageRepository
{
    private readonly IMongoCollection<MongoMessage> _collection;

    public MessageRepository(IMongoClient client, IConfiguration configuration)
    {
        var dbName = configuration["MongoDB:DatabaseName"] ?? "simplex_chat";
        var db = client.GetDatabase(dbName);
        _collection = db.GetCollection<MongoMessage>("messages");
    }

    public async Task EnsureIndexesAsync()
    {
        var indexKeys = Builders<MongoMessage>.IndexKeys
            .Ascending(m => m.ConversationId)
            .Ascending(m => m.Timestamp);
        await _collection.Indexes.CreateOneAsync(new CreateIndexModel<MongoMessage>(indexKeys));
    }

    public async Task<MongoMessage> AddAsync(string conversationId, string role, string content,
        string? attachmentUrl = null, string? attachmentType = null)
    {
        var msg = new MongoMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            AttachmentUrl = attachmentUrl,
            AttachmentType = attachmentType,
            Timestamp = DateTime.UtcNow
        };
        await _collection.InsertOneAsync(msg);
        return msg;
    }

    public async Task<List<MongoMessage>> GetByConversationAsync(string conversationId, int limit = 100)
    {
        return await _collection
            .Find(m => m.ConversationId == conversationId)
            .SortBy(m => m.Timestamp)
            .Limit(limit)
            .ToListAsync();
    }
    public async Task<List<MongoMessage>> GetByConversationIdsAsync(List<string> conversationIds)
    {
        return await _collection
            .Find(m => conversationIds.Contains(m.ConversationId))
            .SortByDescending(m => m.Timestamp)
            .ToListAsync();
    }

    public async Task DeleteByConversationAsync(string conversationId) =>
        await _collection.DeleteManyAsync(m => m.ConversationId == conversationId);
}
