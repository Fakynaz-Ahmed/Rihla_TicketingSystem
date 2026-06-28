using Rihla.DTOs;

namespace Rihla.Services.Ai;

public interface IConversationService
{
    Task<StartConversationResponseDto> StartAsync(int userId, string userName, string userRole, string language);
    Task<SendMessageResponseDto> SendMessageAsync(string conversationId, int userId, string? message, Microsoft.AspNetCore.Http.IFormFile? file = null);
    Task EndAsync(string conversationId, int userId);
    Task DeleteAsync(string conversationId);
    Task<GetMessagesResponseDto> GetHistoryAsync(string conversationId, int userId);
    Task<PagedResult<ConversationSummaryDto>> GetConversationsAsync(int userId, int page, int pageSize);
    Task<PassportConfirmResponseDto> UpdatePassportStatusAsync(string passportNumber, Rihla.Models.Db.PassportStatus status, int userId);
    Task<List<PassportPreviewDto>> GetPassportsAsync(string? status = null);

    // Customer
    Task EscalateAsync(string conversationId, int userId, string reason);
    Task ReopenAsync(string conversationId, int userId);

    // Support Agent
    Task<SendMessageResponseDto> SupportSendMessageAsync(string conversationId, int supportUserId, string? message, Microsoft.AspNetCore.Http.IFormFile? file = null);
    Task EndBySupportAsync(string conversationId);
    Task RequestSpecialistAsync(string conversationId, int supportUserId, string description);

    // Specialist
    Task<SendMessageResponseDto> SpecialistSendMessageAsync(string conversationId, int specialistUserId, string? message, Microsoft.AspNetCore.Http.IFormFile? file = null);
}

