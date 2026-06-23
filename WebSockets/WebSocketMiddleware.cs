using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Rihla.Config;
using Rihla.Services.Cache;
using Rihla.Services.Db;

namespace Rihla.WebSockets;

public class WebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly WebSocketHub    _hub;
    private readonly ICacheService   _cache;
    private readonly JwtSettings     _jwt;
    private readonly ILogger<WebSocketMiddleware> _logger;

    private const string UnreadPrefix = "unread:";

    public WebSocketMiddleware(
        RequestDelegate next,
        WebSocketHub hub,
        ICacheService cache,
        IOptions<JwtSettings> jwt,
        ILogger<WebSocketMiddleware> logger)
    {
        _next   = next;
        _hub    = hub;
        _cache  = cache;
        _jwt    = jwt.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/ws/chat"))
        {
            await _next(context);
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var token = context.Request.Query["access_token"].ToString();
        var principal = ValidateToken(token);
        if (principal is null)
        {
            context.Response.StatusCode = 401;
            return;
        }

        var ws           = await context.WebSockets.AcceptWebSocketAsync();
        var connectionId = Guid.NewGuid().ToString("N");
        var conn         = new WsConnection(ws, principal);

        _hub.Register(connectionId, conn);

        var userIdStr = principal.FindFirst("sub")?.Value ?? "";

        // Auto-join personal channel
        if (!string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out var userId))
        {
            _hub.AddToGroup(connectionId, WsGroups.User(userId));
            await AutoJoinConversationsAsync(connectionId, conn, userId, context.RequestServices);
        }

        _logger.LogDebug("WS connected {ConnId} user {UserId}", connectionId, userIdStr);

        try   { await RunLoopAsync(connectionId, conn, context.RequestServices); }
        finally
        {
            _hub.Unregister(connectionId);
            _logger.LogDebug("WS disconnected {ConnId}", connectionId);
        }
    }

    private async Task AutoJoinConversationsAsync(
        string connectionId, WsConnection conn, int userId, IServiceProvider services)
    {
        try
        {
            var userRepo = services.GetRequiredService<IAppUserRepository>();
            var convRepo = services.GetRequiredService<IConversationRepository>();

            var appUser = await userRepo.GetByIdAsync(userId);
            if (appUser is null) return;

            var convs = await convRepo.GetByUserIdAsync(appUser.Id, 1, 1000);
            var activeConvs = convs.Where(c => c.Status != "ended").ToList();

            foreach (var conv in activeConvs)
            {
                _hub.AddToGroup(connectionId, WsGroups.Conv(conv.ConversationId));

                var count = await _cache.GetIntAsync($"{UnreadPrefix}{userId}:{conv.ConversationId}");
                if (count > 0)
                {
                    await SendDirectAsync(conn, WsEvents.UnreadCount,
                        new UnreadCountPayload(conv.ConversationId, count));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-join conversations for user {UserId}", userId);
        }
    }

    private async Task RunLoopAsync(string connectionId, WsConnection conn, IServiceProvider services)
    {
        var buffer = new byte[8192];

        while (conn.IsOpen)
        {
            WebSocketReceiveResult result;
            try { result = await conn.Socket.ReceiveAsync(buffer, CancellationToken.None); }
            catch { break; }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (conn.IsOpen)
                    await conn.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text) continue;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await DispatchAsync(connectionId, conn, json, services);
        }
    }

    private async Task DispatchAsync(string connectionId, WsConnection conn, string json, IServiceProvider services)
    {
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "join_conversation":
                    var joinConvId = root.GetProperty("conversation_id").GetString()!;
                    if (await IsConversationParticipantAsync(conn, joinConvId, services))
                    {
                        _hub.AddToGroup(connectionId, WsGroups.Conv(joinConvId));
                        _logger.LogInformation("User {UserId} joined conversation {ConversationId}", conn.OdooUserId, joinConvId);
                    }
                    else
                    {
                        await SendDirectAsync(conn, "error",
                            new { message = "Access denied. You are not a participant of this conversation." });
                    }
                    break;

                case "leave_conversation":
                    _hub.RemoveFromGroup(connectionId, WsGroups.Conv(
                        root.GetProperty("conversation_id").GetString()!));
                    break;

                case "mark_read":
                    await HandleMarkReadAsync(conn, root.GetProperty("conversation_id").GetString()!);
                    break;

                case "typing":
                    var convId     = root.GetProperty("conversation_id").GetString()!;
                    var senderName = root.GetProperty("sender_name").GetString()!;
                    var senderRole = root.GetProperty("sender_role").GetString()!;
                    await _hub.SendToGroupAsync(WsGroups.Conv(convId), WsEvents.TypingIndicator,
                        new TypingPayload(convId, senderName, senderRole));
                    break;

                case "ping":
                    await SendDirectAsync(conn, "pong", new { });
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS dispatch error.");
        }
    }

    private async Task<bool> IsConversationParticipantAsync(
        WsConnection conn, string conversationId, IServiceProvider services)
    {
        if (string.IsNullOrEmpty(conn.OdooUserId) ||
            !int.TryParse(conn.OdooUserId, out var userId))
            return false;

        using var scope  = services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IAppUserRepository>();
        var convRepo = scope.ServiceProvider.GetRequiredService<IConversationRepository>();

        var appUser = await userRepo.GetByIdAsync(userId);
        if (appUser is null) return false;

        var conv = await convRepo.GetByIdAsync(conversationId);
        if (conv is null) return false;

        return conv.UserId == appUser.Id;
    }

    private async Task HandleMarkReadAsync(WsConnection conn, string conversationId)
    {
        if (string.IsNullOrEmpty(conn.OdooUserId)) return;

        await _cache.ResetIntAsync($"{UnreadPrefix}{conn.OdooUserId}:{conversationId}");
        await SendDirectAsync(conn, WsEvents.UnreadCount, new UnreadCountPayload(conversationId, 0));
    }

    internal static async Task SendDirectAsync(WsConnection conn, string eventName, object data)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { @event = eventName, data }));
        await WebSocketHub.SendFrameAsync(conn, bytes);
    }

    private ClaimsPrincipal? ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            handler.InboundClaimTypeMap.Clear(); // keep claims clean
            return handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey        = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SecretKey)),
                ValidateIssuer          = true,
                ValidIssuer             = _jwt.Issuer,
                ValidateAudience        = true,
                ValidAudience           = _jwt.Audience,
                ValidateLifetime        = true,
                ClockSkew               = TimeSpan.Zero,
                NameClaimType           = "sub"
            }, out _);
        }
        catch { return null; }
    }
}
