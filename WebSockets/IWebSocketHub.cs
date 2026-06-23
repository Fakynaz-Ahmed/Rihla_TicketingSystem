namespace Rihla.WebSockets;

public interface IWebSocketHub
{
    Task SendToGroupAsync(string group, string eventName, object data);
    void Register(string connectionId, WsConnection connection);
    void Unregister(string connectionId);
    void AddToGroup(string connectionId, string group);
    void RemoveFromGroup(string connectionId, string group);
    void AddUserToGroup(string odooUserId, string group);
}
