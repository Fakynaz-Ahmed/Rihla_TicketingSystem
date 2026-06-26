using Rihla.DTOs;
using Rihla.Models.Db;

namespace Rihla.Services.Tickets;

public interface ITicketService
{
    // ── Support / Admin ───────────────────────────────────────────────────────
    Task<VisitsResultDto> GetSupportTicketsAsync(int supportAppUserId, TicketFilterDto filter);
    Task<VisitsResultDto> GetSpecialistTicketsAsync(int specialistAppUserId, TicketFilterDto filter);
    Task<VisitsResultDto> GetAllTicketsAsync(TicketFilterDto filter);
    Task<SupportTicketDto> CreateTicketAdminAsync(CreateTicketAdminRequestDto dto, int callerUserId, string callerRole);
    Task<SupportTicketDto> RequestVisitForTicketAsync(int ticketId, RequestVisitDto dto, int callerUserId);

    // ── Customer ──────────────────────────────────────────────────────────────
    Task<VisitsResultDto> GetCustomerTicketsAsync(string customerErpNextUserId, TicketFilterDto filter);
    Task<SyncResultDto> SyncTicketsAsync();

    // ── Least Busy specialist / support ──────────────────────────────────────────
    Task<AppUser?> GetLeastBusySpecialistAsync();
    Task<AppUser?> GetLeastBusySupportAsync();

    // ── Support ticket operations (conversation-linked) ───────────────────────
    Task<SupportTicketDto> CreateSupportTicketAsync(
        string conversationId, string productErpNextId,
        string customerErpNextUserId, string customerName,
        int specialistAppUserId, string specialistName,
        int supportAppUserId, string supportName,
        string priority = "medium");

    Task CreateConversationTicketAsync(string conversationId, string productErpNextId, string productName,
        string customerErpNextUserId, string customerName);

    /// <summary>
    /// Called when a Customer Care agent is auto-assigned to an escalated conversation.
    /// Creates an HD Ticket in ERPNext (fault-tolerant) and saves it locally linked to the conversation.
    /// </summary>
    Task<DbTicket> CreateEscalationTicketAsync(
        string conversationId,
        string customerErpNextUserId,
        string customerName,
        int supportAppUserId,
        string supportName,
        string escalationReason);

    Task UpdateTicketSupportAsync(string conversationId, int supportAppUserId);
    Task<SupportTicketDto?> UpdateTicketSpecialistAsync(string conversationId, int specialistAppUserId, string specialistName);
    Task<DbVisit?> CreateErpNextTaskForTicketAsync(string conversationId, int specialistAppUserId, RequestVisitDto? dto = null);

    Task<SupportTicketDto?> GetSupportTicketByIdAsync(int id);
    Task<SupportTicketDto> UpdateSupportTicketAsync(int id, UpdateSupportTicketRequestDto req);
    Task<string?> ResolveTicketBySupportAsync(int id);
    Task<string?> CancelTicketBySupportAsync(int id, string? reason);
    Task CloseTicketByConversationAsync(string conversationId);
    Task UnlinkConversationAsync(string conversationId);
    Task<DbTicket?> GetLastTicketByConversationIdAsync(string conversationId);
    Task<int?> GetLastVisitIdByConversationIdAsync(string conversationId);
    Task<SupportTicketDto> RateTicketAsync(int ticketId, float rating, string? feedback, string customerErpNextUserId);
}
