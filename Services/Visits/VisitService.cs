using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rihla.Data;
using Rihla.DTOs;
using Rihla.Models.Db;
using Rihla.Services.Cache;
using Rihla.Services.Db;
using Rihla.Services.ErpNext;
using Rihla.Services.Storage;
using Rihla.WebSockets;

namespace Rihla.Services.Visits;

public class VisitService : IVisitService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IErpNextClient _erp;
    private readonly IVisitRepository _visitRepo;
    private readonly IVisitActivityRepository _activityRepo;
    private readonly IAppUserRepository _userRepo;
    private readonly ITicketRepository _ticketRepo;
    private readonly IProductRepository _productRepo;
    private readonly ICacheService _cache;
    private readonly IMessageRepository _msgRepo;
    private readonly IConversationRepository _convRepo;
    private readonly IWebSocketHub _hub;
    private readonly IStorageService _storage;
    private readonly ILogger<VisitService> _logger;
    private readonly AppDbContext _db;

    public VisitService(
        IErpNextClient erp,
        IVisitRepository visitRepo,
        IVisitActivityRepository activityRepo,
        IAppUserRepository userRepo,
        ITicketRepository ticketRepo,
        IProductRepository productRepo,
        ICacheService cache,
        IMessageRepository msgRepo,
        IConversationRepository convRepo,
        IWebSocketHub hub,
        IStorageService storage,
        ILogger<VisitService> logger,
        AppDbContext db)
    {
        _erp = erp;
        _visitRepo = visitRepo;
        _activityRepo = activityRepo;
        _userRepo = userRepo;
        _ticketRepo = ticketRepo;
        _productRepo = productRepo;
        _cache = cache;
        _msgRepo = msgRepo;
        _convRepo = convRepo;
        _hub = hub;
        _storage = storage;
        _logger = logger;
        _db = db;
    }

    public async Task<DbVisit> CreateLocalVisitAsync(DbVisit visit)
    {
        var created = await _visitRepo.CreateAsync(visit);

        try
        {
            await _activityRepo.AddAsync(new DbVisitActivity
            {
                VisitId = created.Id,
                Type = "sync",
                Title = "Visit Created",
                Description = $"Assigned to {created.AssignedUserName}."
                            + (created.VisitDate.HasValue
                                ? $" Scheduled for {created.VisitDate.Value:yyyy-MM-dd}."
                                : string.Empty),
                User = "System",
                Date = created.CreateDate == default ? DateTime.UtcNow : created.CreateDate,
                StatusFrom = null,
                StatusTo = DeriveStatus(created)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log 'Visit Created' activity for visit {VisitId}.", created.Id);
        }

        await _cache.RemoveByPrefixAsync("visits:partner:");
        await _cache.RemoveByPrefixAsync("visits:engineer:");
        return created;
    }

    public async Task<VisitDto> CreateVisitAsync(CreateVisitRequestDto dto, string createdBy)
    {
        var customer = await _userRepo.GetByErpNextUserIdAsync(dto.CustomerId)
            ?? throw new KeyNotFoundException($"Customer with ERPNext ID {dto.CustomerId} not found.");

        var product = await _productRepo.GetByIdAsync(dto.ProductId)
            ?? throw new KeyNotFoundException($"Product with ID {dto.ProductId} not found.");

        AppUser? specialist = null;
        DbTicket? ticket = null;

        if (dto.TicketId.HasValue)
        {
            ticket = await _ticketRepo.GetByIdAsync(dto.TicketId.Value)
                ?? throw new KeyNotFoundException($"Ticket with ID {dto.TicketId} not found.");
        }

        if (dto.SpecialistId.HasValue)
        {
            specialist = await _userRepo.GetByIdAsync(dto.SpecialistId.Value)
                ?? throw new KeyNotFoundException($"Specialist with AppUser ID {dto.SpecialistId.Value} not found.");

            if (ticket is not null)
                await _ticketRepo.UpdateSpecialistAsync(ticket.Id, specialist.Id);
        }
        else if (ticket?.SpecialistAppUserId.HasValue == true)
        {
            specialist = await _userRepo.GetByIdAsync(ticket.SpecialistAppUserId.Value);
        }

        if (specialist is null)
            throw new InvalidOperationException("Specialist could not be resolved. Provide specialist_id or link a ticket that has a specialist assigned.");

        var now = DateTime.UtcNow;
        var priority = dto.Priority;
        var stage = dto.Status == "in_progress" ? "In Progress" : "Draft";

        var dbVisit = new DbVisit
        {
            ErpNextId = "PENDING_SYNC_" + Guid.NewGuid().ToString("N")[..8],
            CustomerErpNextUserId = customer.ErpNextUserId,
            PartnerName = customer.Name,
            AssignedUserId = specialist.Id,
            AssignedUserName = specialist.Name,
            Name = dto.Title,
            Stage = stage,
            Priority = priority,
            IsDone = false,
            VisitDate = dto.VisitDate,
            TagsJson = "[]",
            CreateDate = now,
            WriteDate = now,
            TicketId = dto.TicketId,
            ProductId = dto.ProductId
        };

        var created = await _visitRepo.CreateAsync(dbVisit);

        if (ticket is not null)
            await _ticketRepo.UpdateVisitIdAsync(ticket.Id, created.Id);

        try
        {
            await _activityRepo.AddAsync(new DbVisitActivity
            {
                VisitId = created.Id,
                Type = "created",
                Title = "Visit Created",
                Description = $"Created by {createdBy}."
                            + (created.VisitDate.HasValue
                                ? $" Scheduled for {created.VisitDate.Value:yyyy-MM-dd}."
                                : string.Empty),
                User = createdBy,
                Date = now,
                StatusFrom = null,
                StatusTo = dto.Status
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log creation activity for visit {VisitId}.", created.Id);
        }

        try
        {
            var erpPayload = new Dictionary<string, object?>
            {
                ["customer"] = customer.ErpNextUserId,
                ["description"] = dto.Title,
                ["priority"] = dto.Priority == "high" ? "High" : "Medium",
                ["status"] = "Open"
            };

            var erpVisit = await _erp.CreateDocAsync("Maintenance Visit", erpPayload);
            if (erpVisit is not null && erpVisit.TryGetValue("name", out var nameVal) && nameVal is not null)
            {
                await _visitRepo.UpdateErpNextIdAsync(created.Id, nameVal.ToString()!);
                created.ErpNextId = nameVal.ToString()!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ERPNext Maintenance Visit creation failed for visit {VisitId}.", created.Id);
        }

        await _cache.RemoveByPrefixAsync("visits:partner:");
        await _cache.RemoveByPrefixAsync("visits:engineer:");

        return ToDto(created);
    }

    public async Task UpdateVisitErpNextIdAsync(int visitId, string erpNextId)
    {
        await _visitRepo.UpdateErpNextIdAsync(visitId, erpNextId);
    }

    public async Task<VisitsResultDto> GetAllVisitsAsync(int page, int pageSize)
    {
        var all = await _visitRepo.GetAllAsync();
        return BuildResult(all, page, pageSize);
    }

    public async Task<VisitsResultDto> GetVisitsAsync(string customerErpNextUserId, int page, int pageSize)
    {
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var cacheKey = $"visits:partner:{customerErpNextUserId}:p{page}:ps{pageSize}:{lang}";
        return await _cache.GetOrSetAsync(cacheKey, CacheTtl, async () =>
        {
            var all = await _visitRepo.GetByCustomerAsync(customerErpNextUserId);
            return BuildResult(all, page, pageSize);
        });
    }

    public async Task<VisitsResultDto> GetSpecialistVisitsAsync(int userId, int page, int pageSize)
    {
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var cacheKey = $"visits:specialist:{userId}:p{page}:ps{pageSize}:{lang}";
        return await _cache.GetOrSetAsync(cacheKey, CacheTtl, async () =>
        {
            var all = await _visitRepo.GetByAssignedUserAsync(userId);
            return BuildResult(all, page, pageSize);
        });
    }

    public async Task<VisitsResultDto> GetSpecialistVisitsFilteredAsync(int userId, VisitFilterDto filter)
    {
        var (items, total) = await _visitRepo.GetByAssignedUserFilteredAsync(
            userId, filter.Status, filter.Priority, filter.From, filter.To, filter.Page, filter.PageSize);

        return new VisitsResultDto
        {
            Items = items.Select(ToDto).ToList(),
            Total = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<VisitDetailDto?> GetVisitDetailAsync(int visitId)
    {
        var v = await _visitRepo.GetByIdAsync(visitId);
        if (v is null) return null;

        var base_ = ToDto(v);

        return new VisitDetailDto
        {
            Id = base_.Id,
            Name = base_.Name,
            Specialist = base_.Specialist,
            Customer = base_.Customer,
            Stage = base_.Stage,
            Status = base_.Status,
            Priority = base_.Priority,
            Tags = base_.Tags,
            Deadline = base_.Deadline,
            VisitDate = base_.VisitDate,
            Notes = base_.Notes,
            Description = base_.Description,
            CloseAttachmentUrl = base_.CloseAttachmentUrl,
            ConversationId = base_.ConversationId,
            TicketId = base_.TicketId,
            ProductId = base_.ProductId,
            ProductName = base_.ProductName,
            VisitType = base_.VisitType,
            MaintenanceType = base_.MaintenanceType,
            CreatedAt = base_.CreatedAt,
            UpdatedAt = base_.UpdatedAt,
            VisitRating = base_.VisitRating,
            VisitRatingFeedback = base_.VisitRatingFeedback,
            Ticket = v.Ticket is null ? null : new VisitTicketDto
            {
                Id = v.Ticket.Id,
                Title = v.Ticket.Title,
                Status = v.Ticket.Status,
                Priority = v.Ticket.Priority,
                ConversationId = v.Ticket.ConversationId
            },
            ProductDetail = v.Product is null ? null : new VisitProductDto
            {
                Name = v.Product.Name,
                ImageUrl = v.Product.ImageUrl,
                Description = v.Product.Description
            }
        };
    }

    public async Task SyncSingleVisitAsync(string erpNextId)
    {
        var rawVisit = await _erp.GetDocAsync("Maintenance Visit", erpNextId);
        if (rawVisit is null) return;

        await _visitRepo.UpsertManyAsync([ErpNextVisitToDbRow(rawVisit)]);
        await _cache.RemoveByPrefixAsync("visits:partner:");
        await _cache.RemoveByPrefixAsync("visits:engineer:");
    }

    public async Task<SyncResultDto> SyncVisitsAsync()
    {
        try
        {
            _logger.LogInformation("Syncing Maintenance Visits from ERPNext...");
            var rawVisits = await _erp.GetDocListAsync("Maintenance Visit", fields: new List<string> { "name", "customer", "maintenance_type", "description", "status", "priority", "creation", "modified" }, limit: 500);
            if (rawVisits == null || rawVisits.Count == 0)
                return new SyncResultDto { Success = true, Message = "No visits found to sync." };

            var list = rawVisits.Select(ErpNextVisitToDbRow).ToList();
            await _visitRepo.UpsertManyAsync(list);

            await _cache.RemoveByPrefixAsync("visits:partner:");
            await _cache.RemoveByPrefixAsync("visits:engineer:");

            return new SyncResultDto { Success = true, Message = $"Successfully synced {list.Count} visits." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncVisitsAsync failed");
            return new SyncResultDto { Success = false, Message = $"Sync failed: {ex.Message}" };
        }
    }

    private static DbVisit ErpNextVisitToDbRow(Dictionary<string, object?> r)
    {
        var name = r.GetValueOrDefault("name")?.ToString() ?? string.Empty;
        var customer = r.GetValueOrDefault("customer")?.ToString() ?? string.Empty;
        var desc = r.GetValueOrDefault("description")?.ToString() ?? string.Empty;
        var type = r.GetValueOrDefault("maintenance_type")?.ToString() ?? string.Empty;
        var status = r.GetValueOrDefault("status")?.ToString() ?? string.Empty;
        var priority = r.GetValueOrDefault("priority")?.ToString() ?? "medium";

        var creationStr = r.GetValueOrDefault("creation")?.ToString();
        var creation = DateTime.TryParse(creationStr, out var cDate) ? cDate : DateTime.UtcNow;

        var modifiedStr = r.GetValueOrDefault("modified")?.ToString();
        var modified = DateTime.TryParse(modifiedStr, out var mDate) ? mDate : DateTime.UtcNow;

        return new DbVisit
        {
            ErpNextId = name,
            CustomerErpNextUserId = customer,
            Name = name,
            Description = desc,
            MaintenanceType = type,
            Stage = status,
            Priority = priority,
            IsDone = status.Equals("Completed", StringComparison.OrdinalIgnoreCase),
            CreateDate = creation,
            WriteDate = modified,
            SyncedAt = DateTime.UtcNow
        };
    }

    public async Task<VisitDto> UpdateVisitAsync(int visitId, UpdateVisitDto dto, string performedBy)
    {
        var visit = await _visitRepo.GetByIdAsync(visitId)
            ?? throw new KeyNotFoundException($"Visit {visitId} not found.");

        var now = DateTime.UtcNow;

        if (dto.VisitDate.HasValue)
        {
            var oldDate = visit.VisitDate?.ToString("yyyy-MM-dd") ?? visit.PlannedStart?.ToString("yyyy-MM-dd") ?? "—";
            var newDate = dto.VisitDate.Value.ToString("yyyy-MM-dd");
            visit.VisitDate = dto.VisitDate.Value;
            visit.PlannedStart = dto.VisitDate.Value;

            try
            {
                if (!visit.ErpNextId.StartsWith("PENDING_SYNC"))
                {
                    await _erp.UpdateDocAsync("Maintenance Visit", visit.ErpNextId, new Dictionary<string, object?> { ["visit_date"] = dto.VisitDate.Value });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update visit date in ERPNext.");
            }

            await _activityRepo.AddAsync(new DbVisitActivity
            {
                VisitId = visitId,
                Type = "reschedule",
                Title = "Visit Rescheduled",
                Description = string.IsNullOrWhiteSpace(dto.Notes)
                    ? $"Rescheduled from {oldDate} to {newDate}."
                    : $"Rescheduled from {oldDate} to {newDate}. Note: {dto.Notes}",
                User = performedBy,
                Date = now
            });
        }

        if (dto.SpecialistId.HasValue)
        {
            var specialist = await _userRepo.GetByIdAsync(dto.SpecialistId.Value)
                ?? throw new KeyNotFoundException($"Specialist with AppUser ID {dto.SpecialistId.Value} not found.");

            var oldName = visit.AssignedUserName;
            visit.AssignedUserId = specialist.Id;
            visit.AssignedUserName = specialist.Name;

            await _activityRepo.AddAsync(new DbVisitActivity
            {
                VisitId = visitId,
                Type = "assignment",
                Title = "Specialist Reassigned",
                Description = string.IsNullOrWhiteSpace(dto.Notes)
                    ? $"Reassigned from {oldName} to {specialist.Name}."
                    : $"Reassigned from {oldName} to {specialist.Name}. Note: {dto.Notes}",
                User = performedBy,
                Date = now
            });
        }

        if (dto.Cancel == true)
        {
            var statusBefore = DeriveStatus(visit);
            visit.IsCancelled = true;
            visit.CancellationReason = dto.CancellationReason ?? dto.Notes;

            try
            {
                if (!visit.ErpNextId.StartsWith("PENDING_SYNC"))
                {
                    await _erp.UpdateDocAsync("Maintenance Visit", visit.ErpNextId, new Dictionary<string, object?> { ["status"] = "Cancelled" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel visit in ERPNext.");
            }

            await _activityRepo.AddAsync(new DbVisitActivity
            {
                VisitId = visitId,
                Type = "cancel",
                Title = "Visit Cancelled",
                Description = string.IsNullOrWhiteSpace(visit.CancellationReason)
                    ? "Visit cancelled."
                    : $"Visit cancelled. Reason: {visit.CancellationReason}.",
                User = performedBy,
                Date = now,
                StatusFrom = statusBefore,
                StatusTo = "cancelled"
            });
        }
        else if (!dto.VisitDate.HasValue && !dto.SpecialistId.HasValue && !string.IsNullOrWhiteSpace(dto.Notes))
        {
            await _activityRepo.AddAsync(new DbVisitActivity
            {
                VisitId = visitId,
                Type = "note",
                Title = "Note Added",
                Description = dto.Notes,
                User = performedBy,
                Date = now
            });
        }

        await _visitRepo.UpdateAsync(visit);
        await _cache.RemoveByPrefixAsync("visits:partner:");
        await _cache.RemoveByPrefixAsync("visits:engineer:");

        return ToDto(visit);
    }

    public async Task<VisitDto> UpdateVisitStatusAsync(int visitId, string status, string? notes, string performedBy, IFormFile? attachment = null)
    {
        var visit = await _visitRepo.GetByIdAsync(visitId)
            ?? throw new KeyNotFoundException($"Visit {visitId} not found.");

        if (visit.IsCancelled)
            throw new InvalidOperationException("Cannot update status of a cancelled visit.");

        var statusBefore = DeriveStatus(visit);
        var now = DateTime.UtcNow;

        switch (status)
        {
            case "in_progress":
                if (visit.IsDone)
                    throw new InvalidOperationException("Visit is already completed.");
                visit.Stage = "In Progress";
                break;

            case "done":
                if (visit.IsDone) break;

                if (attachment != null)
                {
                    try
                    {
                        visit.CloseAttachmentUrl = await _storage.SaveVisitAttachmentAsync(visitId, attachment);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to save visit attachment.");
                    }
                }

                visit.IsDone = true;
                visit.Stage = "Completed";
                visit.WriteDate = now;

                var conversationId = visit.Ticket?.ConversationId;
                if (string.IsNullOrEmpty(conversationId) && visit.TicketId.HasValue)
                {
                    var linkedTicket = await _ticketRepo.GetByIdAsync(visit.TicketId.Value);
                    conversationId = linkedTicket?.ConversationId;
                }

                await using (var tx = await _db.Database.BeginTransactionAsync())
                {
                    try
                    {
                        await _visitRepo.UpdateAsync(visit);

                        if (!string.IsNullOrEmpty(conversationId))
                        {
                            await _db.Tickets
                                .Where(t => t.ConversationId == conversationId && t.Status != "solved")
                                .ExecuteUpdateAsync(s => s
                                    .SetProperty(t => t.Status, "solved")
                                    .SetProperty(t => t.SyncedAt, DateTime.UtcNow));

                            await _convRepo.UpdateStatusAsync(conversationId, "ended", endedAt: now);
                        }

                        await tx.CommitAsync();
                    }
                    catch
                    {
                        await tx.RollbackAsync();
                        throw;
                    }
                }

                if (!string.IsNullOrEmpty(conversationId))
                    await _cache.RemoveAsync($"conv:{conversationId}");

                try
                {
                    if (!visit.ErpNextId.StartsWith("PENDING_SYNC"))
                    {
                        await _erp.UpdateDocAsync("Maintenance Visit", visit.ErpNextId, new Dictionary<string, object?> { ["status"] = "Completed" });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to close visit in ERPNext.");
                }

                if (!string.IsNullOrEmpty(conversationId))
                {
                    var msg = $"تم إنهاء الزيارة وحل التذكرة بنجاح بواسطة {performedBy}.";
                    await PushChatMessageSafeAsync(conversationId, msg);

                    try
                    {
                        await _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.ConversationEnded, new ConversationEndedPayload(conversationId));
                        await _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.StatusChanged, new StatusPayload(conversationId, "ended"));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to emit WebSocket events.");
                    }
                }

                await _cache.RemoveByPrefixAsync("visits:partner:");
                await _cache.RemoveByPrefixAsync("visits:engineer:");

                await _activityRepo.AddAsync(new DbVisitActivity
                {
                    VisitId = visitId,
                    Type = "status_change",
                    Title = "Visit Completed",
                    Description = string.IsNullOrWhiteSpace(notes) ? "Status changed to completed." : $"Status changed to completed. Note: {notes}",
                    User = performedBy,
                    Date = now,
                    StatusFrom = statusBefore,
                    StatusTo = "done"
                });

                return ToDto(visit);

            default:
                throw new ArgumentException($"Invalid status '{status}'.");
        }

        await _activityRepo.AddAsync(new DbVisitActivity
        {
            VisitId = visitId,
            Type = "status_change",
            Title = "Visit In Progress",
            Description = string.IsNullOrWhiteSpace(notes) ? "Status changed to in progress." : $"Status changed to in progress. Note: {notes}",
            User = performedBy,
            Date = now,
            StatusFrom = statusBefore,
            StatusTo = status
        });

        await _visitRepo.UpdateAsync(visit);
        await _cache.RemoveByPrefixAsync("visits:partner:");
        await _cache.RemoveByPrefixAsync("visits:engineer:");

        return ToDto(visit);
    }

    public async Task<VisitDto> CancelSpecialistVisitAsync(int visitId, string? reason, string performedBy)
    {
        return await UpdateVisitAsync(visitId, new UpdateVisitDto { Cancel = true, CancellationReason = reason }, performedBy);
    }

    public async Task<List<VisitActivityDto>> GetActivitiesAsync(int visitId)
    {
        var list = await _activityRepo.GetByVisitAsync(visitId);
        return list.Select(a => new VisitActivityDto
        {
            Id = a.Id,
            Type = a.Type,
            Title = a.Title,
            Description = a.Description,
            User = a.User,
            Date = a.Date
        }).ToList();
    }

    public async Task CloseVisitByErpNextIdAsync(string erpNextId)
    {
        var v = await _visitRepo.GetByErpNextIdAsync(erpNextId);
        if (v is not null) await CloseVisitByIdAsync(v.Id);
    }

    public async Task CloseVisitByIdAsync(int visitId)
    {
        var visit = await _visitRepo.GetByIdAsync(visitId);
        if (visit is null || visit.IsDone) return;

        visit.IsDone = true;
        visit.Stage = "Completed";
        visit.WriteDate = DateTime.UtcNow;

        await _visitRepo.UpdateAsync(visit);
    }

    public async Task<VisitDto> RateVisitAsync(int visitId, float rating, string? feedback, string customerErpNextUserId)
    {
        var visit = await _visitRepo.GetByIdAsync(visitId)
            ?? throw new KeyNotFoundException("Visit not found.");

        if (visit.CustomerErpNextUserId != customerErpNextUserId)
            throw new UnauthorizedAccessException("Unauthorized to rate this visit.");

        await _visitRepo.SaveRatingAsync(visitId, rating, feedback);
        var updated = await _visitRepo.GetByIdAsync(visitId);
        return ToDto(updated!);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string DeriveStatus(DbVisit v)
    {
        if (v.IsCancelled) return "cancelled";
        if (v.IsDone) return "done";
        if (v.Stage.Contains("Progress") || v.Stage.Contains("Ongoing")) return "in_progress";
        return "new";
    }

    private static VisitDto ToDto(DbVisit v) => new()
    {
        Id = v.Id,
        Name = v.Name,
        Stage = v.Stage,
        Status = DeriveStatus(v),
        Priority = v.Priority == "1" ? "high" : "medium",
        VisitDate = v.VisitDate,
        Deadline = v.Deadline,
        Notes = v.Description,
        Description = v.Description,
        ConversationId = v.Ticket?.ConversationId,
        TicketId = v.TicketId,
        ProductId = v.ProductId,
        ProductName = v.Product?.Name,
        VisitType = v.VisitType,
        MaintenanceType = v.MaintenanceType,
        CreatedAt = v.CreateDate,
        UpdatedAt = v.WriteDate,
        VisitRating = v.VisitRating,
        VisitRatingFeedback = v.VisitRatingFeedback,
        Specialist = new VisitSpecialistDto { Id = v.AssignedUserId, Name = v.AssignedUserName },
        Customer = new VisitCustomerDto { ErpNextUserId = v.CustomerErpNextUserId, Name = v.PartnerName }
    };

    private VisitsResultDto BuildResult(List<DbVisit> all, int page, int pageSize)
    {
        var total = all.Count;
        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new VisitsResultDto
        {
            Items = pageItems.Select(ToDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private async Task PushChatMessageSafeAsync(string conversationId, string content)
    {
        try
        {
            await _msgRepo.AddAsync(conversationId, "system", content);
            await _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.MessageReceived,
                new MessagePayload(conversationId, Guid.NewGuid().ToString("N"), "system", content, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push system message.");
        }
    }
}
