using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Rihla.Models.Mongo;

public class MongoMessage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    [BsonElement("role")]
    public string Role { get; set; } = string.Empty;

    [BsonElement("content")]
    public string Content { get; set; } = string.Empty;

    [BsonElement("attachment_url")]
    public string? AttachmentUrl { get; set; }

    [BsonElement("attachment_type")]
    public string? AttachmentType { get; set; }

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
