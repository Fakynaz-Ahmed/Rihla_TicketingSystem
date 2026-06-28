using Microsoft.AspNetCore.Http;
using Rihla.DTOs;
using Rihla.Models.Db;

namespace Rihla.Services.Visits;

public interface IVisitService
{
    Task<DbVisit> CreateLocalVisitAsync(DbVisit visit);
    Task<VisitDto> CreateVisitAsync(CreateVisitRequestDto dto, string createdBy);
    Task UpdateVisitErpNextIdAsync(int visitId, string erpNextId);
    Task<VisitsResultDto> GetAllVisitsAsync(int page, int pageSize);
    Task<VisitsResultDto> GetVisitsAsync(string customerErpNextUserId, int page, int pageSize);
    Task<VisitsResultDto> GetSpecialistVisitsAsync(int userId, int page, int pageSize);
    Task<VisitsResultDto> GetSpecialistVisitsFilteredAsync(int userId, VisitFilterDto filter);
    Task<VisitDetailDto?> GetVisitDetailAsync(int visitId);
    Task<SyncResultDto> SyncVisitsAsync();
    Task SyncSingleVisitAsync(string erpNextId);
    Task<VisitDto> UpdateVisitAsync(int visitId, UpdateVisitDto dto, string performedBy);
    Task<VisitDto> UpdateVisitStatusAsync(int visitId, string status, string? notes, string performedBy, IFormFile? attachment = null);
    Task<VisitDto> CancelSpecialistVisitAsync(int visitId, string? reason, string performedBy);
    Task<List<VisitActivityDto>> GetActivitiesAsync(int visitId);
    Task CloseVisitByErpNextIdAsync(string erpNextId);
    Task CloseVisitByIdAsync(int visitId);
    Task<VisitDto> RateVisitAsync(int visitId, float rating, string? feedback, string customerErpNextUserId);
}
