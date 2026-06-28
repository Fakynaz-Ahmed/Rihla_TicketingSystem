using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rihla.Services.Db;
using Rihla.WebSockets;
using System;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace Rihla.Controllers;

[ApiController]
[Route("api/webhooks")]
public class ERPWebhookController : ControllerBase
{
    private readonly ITicketRepository _ticketRepo;
    private readonly IAppUserRepository _userRepo;
    private readonly IMessageRepository _msgRepo;
    private readonly IWebSocketHub _hub;
    private readonly ILogger<ERPWebhookController> _logger;
    public ERPWebhookController(
        ITicketRepository ticketRepo,
        IAppUserRepository userRepo,
        IMessageRepository msgRepo,
        IWebSocketHub hub,
        ILogger<ERPWebhookController> logger)
    {
        _ticketRepo = ticketRepo;
        _userRepo = userRepo;
        _msgRepo = msgRepo;
        _hub = hub;
        _logger = logger;
    }
    [HttpPost("erpnext-communication")]
    public async Task<IActionResult> HandleErpNextCommunication([FromBody] ErpNextCommunicationPayload payload)
    {
        try
        {
            if (payload == null || string.IsNullOrEmpty(payload.TicketId))
            {
                return BadRequest("Invalid payload.");
            }
            _logger.LogInformation("Received ERPNext communication webhook for ticket {TicketId}.", payload.TicketId);
            // 1. Resolve ticket & conversation ID
            var ticket = await _ticketRepo.GetByErpNextIdAsync(payload.TicketId);
            if (ticket == null)
            {
                _logger.LogWarning("ERPNext ticket {TicketId} not found locally. Skipping sync.", payload.TicketId);
                return NotFound($"Ticket {payload.TicketId} not found.");
            }
            var conversationId = ticket.ConversationId;
            // 2. Identify sender role
            var senderUser = await _userRepo.GetByEmailAsync(payload.SenderEmail);
            if (senderUser == null)
            {
                _logger.LogWarning("Sender {Email} is not a registered user in our database. Skipping sync.", payload.SenderEmail);
                return Ok("Skipped: sender not a registered app user.");
            }
            string role;
            if (senderUser.PrimaryRole == Rihla.Config.AppRoles.CustomerCare)
            {
                role = "support";
            }
            else if (senderUser.PrimaryRole == Rihla.Config.AppRoles.Specialist)
            {
                role = "specialist";
            }
            else
            {
                // If it is the Customer sending an email/message to the ticket from ERPNext, we ignore it 
                // because customer messages are already captured directly from our live chat app.
                _logger.LogInformation("Sender {Email} has role {Role}. Skipping to prevent duplicate/untracked sync.", payload.SenderEmail, senderUser.PrimaryRole);
                return Ok("Skipped: sender is a customer.");
            }
            // 3. Process and clean message content
            var cleanText = StripHtml(payload.Content);
            if (string.IsNullOrEmpty(cleanText))
            {
                return Ok("Skipped: empty message content.");
            }
            // 4. Save to local database (MongoDB message repo)
            await _msgRepo.AddAsync(conversationId, role, cleanText);
            _logger.LogInformation("Synced ERPNext reply from {Sender} ({Role}) to conversation {ConvId}.", payload.SenderName, role, conversationId);
            // 5. Push to live chat WebSocket (SignalR)
            await _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.MessageReceived,
                new MessagePayload(
                    conversation_id: conversationId,
                    id: Guid.NewGuid().ToString("N"),
                    role: role,
                    content: cleanText,
                    timestamp: DateTime.UtcNow,
                    attachment_url: null,
                    attachment_type: null));
            return Ok(new { Success = true, Message = "Message synced successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ERPNext communication webhook.");
            return StatusCode(500, "Internal server error.");
        }
    }
    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        // Strip HTML tags
        var clean = Regex.Replace(input, "<.*?>", string.Empty);
        // Decode HTML entities
        return System.Net.WebUtility.HtmlDecode(clean).Trim();
    }
}
public class ErpNextCommunicationPayload
{
    [JsonPropertyName("ticket_id")]
    public string TicketId { get; set; } = string.Empty;
    [JsonPropertyName("sender_email")]
    public string SenderEmail { get; set; } = string.Empty;
    [JsonPropertyName("sender_name")]
    public string SenderName { get; set; } = string.Empty;
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}