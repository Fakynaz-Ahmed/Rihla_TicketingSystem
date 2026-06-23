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
}
