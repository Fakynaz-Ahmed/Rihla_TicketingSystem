using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace Rihla.WebSockets;

public sealed class WebSocketHub : IWebSocketHub, IDisposable
{
    private const string OutChannel = "ws:out";

    private readonly ConcurrentDictionary<string, WsConnection> _connections = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _groups = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _userConnections = new();
    private readonly ISubscriber _sub;
    private readonly ILogger<WebSocketHub> _logger;

    public WebSocketHub(IConnectionMultiplexer redis, ILogger<WebSocketHub> logger)
    {
        _sub = redis.GetSubscriber();
        _logger = logger;
        _sub.Subscribe(RedisChannel.Literal(OutChannel), (channel, msg) =>
        {
            if (!msg.IsNullOrEmpty) Task.Run(() => DeliverAsync(msg!));
        });
    }

    public void Register(string id, WsConnection conn)
    {
        _connections[id] = conn;
        if (conn.OdooUserId is not null)
            _userConnections.GetOrAdd(conn.OdooUserId, _ => new()).TryAdd(id, 0);
    }

    public void Unregister(string id)
    {
        if (_connections.TryRemove(id, out var conn) && conn.OdooUserId is not null)
        {
            if (_userConnections.TryGetValue(conn.OdooUserId, out var userConns))
                userConns.TryRemove(id, out _);
        }
        foreach (var g in _groups.Values) g.TryRemove(id, out _);
    }

    public void AddToGroup(string id, string group) =>
        _groups.GetOrAdd(group, _ => new()).TryAdd(id, 0);

    public void RemoveFromGroup(string id, string group)
    {
        if (_groups.TryGetValue(group, out var g)) g.TryRemove(id, out _);
    }

    public void AddUserToGroup(string odooUserId, string group)
    {
        if (!_userConnections.TryGetValue(odooUserId, out var conns)) return;
        foreach (var connId in conns.Keys)
            AddToGroup(connId, group);
    }

    public async Task SendToGroupAsync(string group, string eventName, object data)
    {
        var envelope = JsonSerializer.Serialize(new
        {
            group,
            @event = eventName,
            data   = JsonSerializer.SerializeToElement(data)
        });
        await _sub.PublishAsync(RedisChannel.Literal(OutChannel), envelope);
    }

    private async Task DeliverAsync(string envelope)
    {
        try
        {
            using var doc = JsonDocument.Parse(envelope);
            var root  = doc.RootElement;
            var group = root.GetProperty("group").GetString()!;

            var clientBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                @event = root.GetProperty("event").GetString(),
                data   = root.GetProperty("data")
            }));

            if (!_groups.TryGetValue(group, out var members)) return;

            foreach (var connId in members.Keys.ToArray())
            {
                if (!_connections.TryGetValue(connId, out var conn) || !conn.IsOpen) continue;
                await SendFrameAsync(conn, clientBytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error delivering WebSocket broadcast.");
        }
    }

    internal static async Task SendFrameAsync(WsConnection conn, byte[] data)
    {
        await conn.SendLock.WaitAsync();
        try
        {
            if (conn.IsOpen)
                await conn.Socket.SendAsync(data, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally { conn.SendLock.Release(); }
    }

    public void Dispose() => _sub.UnsubscribeAll();
}

public sealed class WsConnection
{
    public WebSocket       Socket      { get; }
    public ClaimsPrincipal User        { get; }
    public string?         OdooUserId  { get; }
    public SemaphoreSlim   SendLock    { get; } = new(1, 1);
    public bool            IsOpen      => Socket.State == WebSocketState.Open;

    public WsConnection(WebSocket socket, ClaimsPrincipal user)
    {
        Socket     = socket;
        User       = user;
        OdooUserId = user.FindFirst("sub")?.Value;
    }
}
