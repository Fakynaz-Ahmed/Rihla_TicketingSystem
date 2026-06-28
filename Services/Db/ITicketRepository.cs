using Rihla.DTOs;
using Rihla.Models.Db;

namespace Rihla.Services.Db;

public interface ITicketRepository
{
    Task UpsertManyAsync(IEnumerable<DbTicket> tickets);
    Task<List<DbTicket>> GetByCustomerAsync(string customerErpNextUserId);
    Task<(List<DbTicket> Items, int Total)> GetAllFilteredAsync(TicketFilterDto filter);
    Task<(List<DbTicket> Items, int Total)> GetByCustomerFilteredAsync(string customerErpNextUserId, TicketFilterDto filter);
    Task<(List<DbTicket> Items, int Total)> GetBySupportFilteredAsync(int supportAppUserId, TicketFilterDto filter);
    Task<(List<DbTicket> Items, int Total)> GetBySpecialistFilteredAsync(int specialistAppUserId, TicketFilterDto filter);

    Task<DbTicket> CreateAsync(DbTicket ticket);
    Task<DbTicket?> GetByIdAsync(int id);
    Task<DbTicket?> GetByConversationIdAsync(string conversationId);
    Task<DbTicket?> GetByErpNextIdAsync(string erpNextId);
    Task<DbTicket?> GetLastByConversationIdAsync(string conversationId);
    Task<int?> GetLastVisitIdByConversationIdAsync(string conversationId);
    Task UpdateAsync(int id, string? status, string? priority);
    Task UpdateTitleAsync(int id, string title);
    Task UpdateVisitIdAsync(int ticketId, int visitId);
    Task UpdateSpecialistAsync(int ticketId, int specialistAppUserId);
    Task UpdateAssignmentsAsync(string conversationId, int? supportAppUserId, int? specialistAppUserId);
    Task UpdateStatusByConversationIdAsync(string conversationId, string status);
    Task DeleteAsync(int id);
    Task<int> GetActiveCountBySpecialistAsync(int specialistAppUserId);
    Task<int> GetActiveCountBySupportAsync(int supportAppUserId);
    Task UpdateStatusByTicketIdAsync(int ticketId, string status);
    Task UnlinkConversationAsync(string conversationId);
    Task CloseTicketStatusAsync(string conversationId);
    Task SaveRatingAsync(int id, float rating, string? feedback);
    Task<Dictionary<string, float?>> GetRatingsByConversationIdsAsync(IEnumerable<string> conversationIds);
}
