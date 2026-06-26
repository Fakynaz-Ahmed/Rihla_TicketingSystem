using System.Text.Json.Serialization;

namespace Rihla.Models.Db;

/// <summary>
/// Conversation lifecycle status stored in the DB and returned in API responses.
/// Enum member names are lowercase/snake_case — they serialise directly to JSON strings.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConversationStatus
{
    ai,
    pending_cc,
    with_cc,
    pending_specialist,
    with_specialist,
    ended
}
