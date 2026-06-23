namespace Rihla.WebSockets;

public static class WsEvents
{
    public const string MessageReceived      = "MessageReceived";
    public const string StatusChanged        = "StatusChanged";
    public const string AgentJoined          = "AgentJoined";
    public const string ConversationEnded    = "ConversationEnded";
    public const string ConversationReopened = "ConversationReopened";
    public const string TypingIndicator      = "TypingIndicator";
    public const string NewEscalation        = "NewEscalation";
    public const string EngineerAssigned     = "EngineerAssigned";
    public const string UnreadCount          = "UnreadCount";
    public const string ConversationList     = "ConversationList";
    public const string Notification         = "Notification";
}

public static class WsGroups
{
    public static string Conv(string id)  => $"conv:{id}";
    public static string User(int odooId) => $"user:{odooId}";
    public const string CustomerCareQueue = "queue:customer_care";
}

public record MessagePayload(
    string conversation_id,
    string id,
    string role,
    string content,
    DateTime timestamp,
    string? attachment_url  = null,
    string? attachment_type = null,
    int?    visit_id        = null);

public record StatusPayload(
    string conversation_id,
    string status);

public record AgentJoinedPayload(
    string conversation_id,
    string name,
    string role);

public record ConversationEndedPayload(
    string conversation_id);

public record ConversationReopenedPayload(
    string conversation_id);

public record TypingPayload(
    string conversation_id,
    string sender_name,
    string sender_role);

public record NewEscalationPayload(
    string conversation_id,
    string customer_name,
    int machine_id,
    string escalation_reason,
    DateTime escalated_at);

public record EngineerAssignedPayload(
    string conversation_id,
    string customer_name,
    int machine_id,
    string customer_care_name);

public record UnreadCountPayload(
    string conversation_id,
    int    count);

public record NotificationPayload(
    int      id,
    string   type,
    string   title,
    string   body,
    bool     is_read,
    DateTime created_at,
    string?  data);
