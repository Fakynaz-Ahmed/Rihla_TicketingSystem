using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rihla.Config;
using Rihla.DTOs;
using Rihla.Models.Db;
using Rihla.Services.Db;
using Rihla.Services.ErpNext;
using Rihla.Services.Visits;

namespace Rihla.Services.Tickets;

public class TicketService : ITicketService
{
    private readonly IErpNextClient _erp;
    private readonly ITicketRepository _ticketRepo;
    private readonly IAppUserRepository _userRepo;
    private readonly IProductRepository _productRepo;
    private readonly IVisitService _visitService;
    private readonly ILogger<TicketService> _logger;

    // ── User type routing constants (sourced from Config/UserRoles.cs) ──────────
    /// <summary>Primary role value stored for Customer Care agents.</summary>
    private const string CustomerCareRole  = AppRoles.CustomerCare;
    /// <summary>Primary role value stored for Specialists.</summary>
    private const string SpecialistRole    = AppRoles.Specialist;
    /// <summary>Dept name used as secondary filter for Specialists.</summary>
    private const string SpecialistDept    = ErpDepartments.Specialist;
    /// <summary>Dept name used as secondary filter for Customer Care agents.</summary>
    private const string CustomerCareDept  = ErpDepartments.CustomerCare;

    public TicketService(
        IErpNextClient erp,
        ITicketRepository ticketRepo,
        IAppUserRepository userRepo,
        IProductRepository productRepo,
        IVisitService visitService,
        ILogger<TicketService> logger)
    {
        _erp = erp;
        _ticketRepo = ticketRepo;
        _userRepo = userRepo;
        _productRepo = productRepo;
        _visitService = visitService;
        _logger = logger;
    }

    public async Task<VisitsResultDto> GetSupportTicketsAsync(int supportAppUserId, TicketFilterDto filter)
    {
        var (items, total) = await _ticketRepo.GetBySupportFilteredAsync(supportAppUserId, filter);
        return new VisitsResultDto
        {
            Items = items.Select(MapTicketToVisitDto).ToList(),
            Total = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<VisitsResultDto> GetSpecialistTicketsAsync(int specialistAppUserId, TicketFilterDto filter)
    {
        var (items, total) = await _ticketRepo.GetBySpecialistFilteredAsync(specialistAppUserId, filter);
        return new VisitsResultDto
        {
            Items = items.Select(MapTicketToVisitDto).ToList(),
            Total = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<VisitsResultDto> GetAllTicketsAsync(TicketFilterDto filter)
    {
        var (items, total) = await _ticketRepo.GetAllFilteredAsync(filter);
        return new VisitsResultDto
        {
            Items = items.Select(MapTicketToVisitDto).ToList(),
            Total = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<SupportTicketDto> CreateTicketAdminAsync(CreateTicketAdminRequestDto dto, int callerUserId, string callerRole)
    {
        var customer = await _userRepo.GetByErpNextUserIdAsync(dto.CustomerId)
            ?? throw new KeyNotFoundException($"Customer with ERPNext ID {dto.CustomerId} not found.");

        var product = await _productRepo.GetByErpNextIdAsync(dto.ProductErpNextId)
            ?? throw new KeyNotFoundException($"Product with ID {dto.ProductErpNextId} not found.");

        AppUser support;
        if (callerRole.Equals("Technical Support", StringComparison.OrdinalIgnoreCase))
        {
            support = await _userRepo.GetByIdAsync(callerUserId)
                ?? throw new InvalidOperationException("Caller support user not found.");
        }
        else
        {
            support = await _userRepo.GetByIdAsync(dto.SupportId)
                ?? throw new KeyNotFoundException($"Support Agent with ID {dto.SupportId} not found.");
        }

        // Fault tolerant ERPNext call
        string erpNextId = "PENDING_SYNC_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var erpPayload = new Dictionary<string, object?>
            {
                ["subject"] = dto.Title,
                ["raised_by"] = customer.Email,
                ["priority"] = dto.Priority,
                ["status"] = "Open"
            };

            var erpTicket = await _erp.CreateDocAsync("HD Ticket", erpPayload);
            if (erpTicket is not null && erpTicket.TryGetValue("name", out var nameVal) && nameVal is not null)
            {
                erpNextId = nameVal.ToString()!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERPNext HD Ticket creation failed. Ticket will be created locally first.");
        }

        var now = DateTime.UtcNow;
        var dbTicket = new DbTicket
        {
            ErpNextId             = erpNextId,
            CustomerErpNextUserId = customer.ErpNextUserId,
            Title                 = dto.Title,
            ProductErpNextId      = product.ItemCode,
            ProductName           = product.Name,
            Status                = TicketStatus.Open,
            Priority              = dto.Priority,
            SupportAppUserId      = support.Id,
            CreateDate            = now,
            WriteDate             = now
        };

        var created = await _ticketRepo.CreateAsync(dbTicket);
        return MapTicket(created);
    }

    public async Task<SupportTicketDto> RequestVisitForTicketAsync(int ticketId, RequestVisitDto dto, int callerUserId)
    {
        var ticket = await _ticketRepo.GetByIdAsync(ticketId)
            ?? throw new KeyNotFoundException($"Ticket with ID {ticketId} not found.");

        // Auto assign least busy specialist
        var specialist = await GetLeastBusySpecialistAsync()
            ?? throw new InvalidOperationException("No available specialists found to assign.");

        // Create the visit request
        var dbVisit = new DbVisit
        {
            ErpNextId = "PENDING_SYNC_" + Guid.NewGuid().ToString("N")[..8],
            CustomerErpNextUserId = ticket.CustomerErpNextUserId,
            AssignedUserId = specialist.Id,
            AssignedUserName = specialist.Name,
            Name = $"Visit for Ticket: {ticket.Title}",
            Stage = "New",
            Priority = dto.Priority == "high" ? "1" : "0",
            Description = dto.Description,
            VisitType = dto.VisitType,
            MaintenanceType = dto.MaintenanceType ?? string.Empty,
            TicketId = ticket.Id,
            ProductId = dto.ProductId,
            CreateDate = DateTime.UtcNow,
            WriteDate = DateTime.UtcNow
        };

        var createdVisit = await _visitService.CreateLocalVisitAsync(dbVisit);

        // Fault tolerant ERPNext visit creation
        try
        {
            var erpPayload = new Dictionary<string, object?>
            {
                ["customer"] = ticket.CustomerErpNextUserId,
                ["maintenance_type"] = dto.MaintenanceType ?? "Periodic",
                ["description"] = dto.Description,
                ["priority"] = dto.Priority == "high" ? "High" : "Medium",
                ["status"] = "Open"
            };

            var erpVisit = await _erp.CreateDocAsync("Maintenance Visit", erpPayload);
            if (erpVisit is not null && erpVisit.TryGetValue("name", out var nameVal) && nameVal is not null)
            {
                await _visitService.UpdateVisitErpNextIdAsync(createdVisit.Id, nameVal.ToString()!);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Maintenance Visit in ERPNext. Will retry later.");
        }

        // Link visit to ticket and update status
        await _ticketRepo.UpdateVisitIdAsync(ticket.Id, createdVisit.Id);
        await _ticketRepo.UpdateSpecialistAsync(ticket.Id, specialist.Id);
        await _ticketRepo.UpdateStatusByTicketIdAsync(ticket.Id, TicketStatus.Replied);


        var updated = await _ticketRepo.GetByIdAsync(ticket.Id);
        return MapTicket(updated!);
    }

    public async Task<VisitsResultDto> GetCustomerTicketsAsync(string customerErpNextUserId, TicketFilterDto filter)
    {
        var (items, total) = await _ticketRepo.GetByCustomerFilteredAsync(customerErpNextUserId, filter);
        return new VisitsResultDto
        {
            Items = items.Select(MapTicketToVisitDto).ToList(),
            Total = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<SyncResultDto> SyncTicketsAsync()
    {
        try
        {
            _logger.LogInformation("Syncing tickets from ERPNext HD Ticket...");
            var rawTickets = await _erp.GetDocListAsync("HD Ticket", fields: new List<string> { "name", "subject", "raised_by", "status", "priority", "creation", "modified" }, limit: 500);
            if (rawTickets == null || rawTickets.Count == 0)
                return new SyncResultDto { Success = true, Message = "No tickets found to sync." };

            var list = new List<DbTicket>();
            foreach (var r in rawTickets)
            {
                var name     = r.GetValueOrDefault("name")?.ToString()     ?? string.Empty;
                var subject  = r.GetValueOrDefault("subject")?.ToString()  ?? string.Empty;
                var raisedBy = r.GetValueOrDefault("raised_by")?.ToString() ?? string.Empty;
                var priority = r.GetValueOrDefault("priority")?.ToString()?.ToLower() ?? "medium";

                // Preserve ERPNext status casing — must match TicketStatus constants exactly.
                // Normalise any legacy lowercase values that may exist in older ERPNext records.
                var rawStatus = r.GetValueOrDefault("status")?.ToString() ?? TicketStatus.Open;
                var status    = NormaliseErpStatus(rawStatus);

                var creationStr = r.GetValueOrDefault("creation")?.ToString();
                var creation    = DateTime.TryParse(creationStr, out var cDate) ? cDate : DateTime.UtcNow;

                var modifiedStr = r.GetValueOrDefault("modified")?.ToString();
                var modified    = DateTime.TryParse(modifiedStr, out var mDate) ? mDate : DateTime.UtcNow;

                list.Add(new DbTicket
                {
                    ErpNextId             = name,
                    CustomerErpNextUserId = raisedBy,
                    Title                 = subject,
                    ProductErpNextId      = string.Empty,
                    ProductName           = string.Empty,
                    Status                = status,
                    Priority              = priority,
                    CreateDate            = creation,
                    WriteDate             = modified,
                    SyncedAt              = DateTime.UtcNow
                });
            }

            await _ticketRepo.UpsertManyAsync(list);
            return new SyncResultDto { Success = true, Message = $"Synced {list.Count} tickets." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncTicketsAsync failed");
            return new SyncResultDto { Success = false, Message = $"Sync failed: {ex.Message}" };
        }
    }

    public async Task<AppUser?> GetLeastBusySpecialistAsync()
    {
        // Primary: search by PrimaryRole == "Specialist" (set at login/sync time)
        var agents = await _userRepo.GetByPrimaryRoleAsync(SpecialistRole);

        // Fallback: search by Department if no PrimaryRole match (handles legacy/un-synced users)
        if (agents.Count == 0)
            agents = await _userRepo.GetByDepartmentsAsync([SpecialistDept]);

        if (agents.Count == 0) return null;

        var agentIds = agents.Select(a => a.Id).ToList();
        var ticketCounts = new Dictionary<int, int>();
        foreach (var id in agentIds)
            ticketCounts[id] = await _ticketRepo.GetActiveCountBySpecialistAsync(id);

        return agents.OrderBy(a => ticketCounts.GetValueOrDefault(a.Id, 0)).FirstOrDefault();
    }

    public async Task<AppUser?> GetLeastBusySupportAsync()
    {
        // Primary: search by PrimaryRole == "Customer Care" (set at login/sync time)
        var agents = await _userRepo.GetByPrimaryRoleAsync(CustomerCareRole);

        // Fallback: search by Department if no PrimaryRole match
        if (agents.Count == 0)
            agents = await _userRepo.GetByDepartmentsAsync([CustomerCareDept]);

        if (agents.Count == 0) return null;

        var agentIds = agents.Select(a => a.Id).ToList();
        var ticketCounts = new Dictionary<int, int>();
        foreach (var id in agentIds)
            ticketCounts[id] = await _ticketRepo.GetActiveCountBySupportAsync(id);

        return agents.OrderBy(a => ticketCounts.GetValueOrDefault(a.Id, 0)).FirstOrDefault();
    }

    public async Task<SupportTicketDto> CreateSupportTicketAsync(
        string conversationId, string productErpNextId,
        string customerErpNextUserId, string customerName,
        int specialistAppUserId, string specialistName,
        int supportAppUserId, string supportName,
        string priority = "medium")
    {
        string erpNextId = "PENDING_SYNC_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var erpPayload = new Dictionary<string, object?>
            {
                ["subject"] = $"Ticket from chat: {conversationId}",
                ["raised_by"] = customerErpNextUserId,
                ["priority"] = priority,
                ["status"] = "Open"
            };

            var erpTicket = await _erp.CreateDocAsync("HD Ticket", erpPayload);
            if (erpTicket is not null && erpTicket.TryGetValue("name", out var nameVal) && nameVal is not null)
            {
                erpNextId = nameVal.ToString()!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create HD Ticket in ERPNext during conversation. Ticket created locally.");
        }

        var now = DateTime.UtcNow;
        var ticket = new DbTicket
        {
            ErpNextId             = erpNextId,
            CustomerErpNextUserId = customerErpNextUserId,
            Title                 = $"Ticket from chat: {conversationId}",
            ProductErpNextId      = productErpNextId,
            ProductName           = productErpNextId,
            Status                = TicketStatus.Open,
            Priority              = priority,
            ConversationId        = conversationId,
            SpecialistAppUserId   = specialistAppUserId,
            SupportAppUserId      = supportAppUserId,
            CreateDate            = now,
            WriteDate             = now
        };

        var created = await _ticketRepo.CreateAsync(ticket);
        return MapTicket(created);
    }

    public async Task CreateConversationTicketAsync(string conversationId, string productErpNextId, string productName,
        string customerErpNextUserId, string customerName)
    {
        var now = DateTime.UtcNow;
        var ticket = new DbTicket
        {
            ErpNextId             = "PENDING_SYNC_" + Guid.NewGuid().ToString("N")[..8],
            CustomerErpNextUserId = customerErpNextUserId,
            Title                 = $"Chat Support: {conversationId}",
            ProductErpNextId      = productErpNextId,
            ProductName           = productName,
            Status                = TicketStatus.Open,
            Priority              = "medium",
            ConversationId        = conversationId,
            CreateDate            = now,
            WriteDate             = now
        };

        await _ticketRepo.CreateAsync(ticket);
    }

    public async Task<DbTicket> CreateEscalationTicketAsync(
        string conversationId,
        string customerErpNextUserId,
        string customerName,
        int supportAppUserId,
        string supportName,
        string escalationReason)
    {
        // 1. Try creating the ticket in ERPNext first (fault tolerant)
        string erpNextId = "PENDING_SYNC_" + Guid.NewGuid().ToString("N")[..8];
        var shortConvId  = conversationId[..Math.Min(8, conversationId.Length)];
        var ticketTitle  = $"[Chat] {shortConvId}_{customerName}: {escalationReason}";
        try
        {
            var erpPayload = new Dictionary<string, object?>
            {
                ["subject"]     = ticketTitle,
                ["raised_by"]   = customerErpNextUserId,
                ["priority"]    = "Medium",
                ["status"]      = "Open",
                ["description"] = $"Escalated from chat conversation: {conversationId}\nReason: {escalationReason}"
            };

            var erpTicket = await _erp.CreateDocAsync("HD Ticket", erpPayload);
            if (erpTicket is not null && erpTicket.TryGetValue("name", out var nameVal) && nameVal is not null)
            {
                erpNextId = nameVal.ToString()!;
                _logger.LogInformation("Created ERPNext HD Ticket {Id} for escalated conversation {ConvId}.", erpNextId, conversationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERPNext HD Ticket creation failed for escalation. Ticket will sync later.");
        }

        // 2. Save locally linked to conversation + support agent
        var now = DateTime.UtcNow;
        var dbTicket = new DbTicket
        {
            ErpNextId             = erpNextId,
            CustomerErpNextUserId = customerErpNextUserId,
            Title                 = ticketTitle,
            ProductErpNextId      = string.Empty,
            ProductName           = string.Empty,
            Status                = TicketStatus.Open,
            Priority              = "medium",
            ConversationId        = conversationId,
            SupportAppUserId      = supportAppUserId,
            CreateDate            = now,
            WriteDate             = now
        };

        var created = await _ticketRepo.CreateAsync(dbTicket);
        _logger.LogInformation("Escalation ticket {TicketId} created and linked to conversation {ConvId} with support agent {SupportId}.",
            created.Id, conversationId, supportAppUserId);

        return created;
    }

    public async Task UpdateTicketSupportAsync(string conversationId, int supportAppUserId)
    {
        await _ticketRepo.UpdateAssignmentsAsync(conversationId, supportAppUserId, null);
    }

    public async Task<SupportTicketDto?> UpdateTicketSpecialistAsync(string conversationId, int specialistAppUserId, string specialistName)
    {
        await _ticketRepo.UpdateAssignmentsAsync(conversationId, null, specialistAppUserId);
        await _ticketRepo.UpdateStatusByConversationIdAsync(conversationId, TicketStatus.Replied);

        var ticket = await _ticketRepo.GetByConversationIdAsync(conversationId);
        return ticket is null ? null : MapTicket(ticket);
    }

    public async Task<DbVisit?> CreateErpNextTaskForTicketAsync(string conversationId, int specialistAppUserId, RequestVisitDto? dto = null)
    {
        var ticket = await _ticketRepo.GetByConversationIdAsync(conversationId);
        if (ticket is null) return null;

        var specialist = await _userRepo.GetByIdAsync(specialistAppUserId);
        var specName = specialist?.Name ?? "Specialist";

        // 1. Create visit in ERPNext first
        string erpNextId;
        try
        {
            var erpPayload = new Dictionary<string, object?>
            {
                ["customer"] = ticket.CustomerErpNextUserId,
                ["description"] = dto?.Description ?? "Requested via support chat.",
                ["priority"] = dto?.Priority == "high" ? "High" : "Medium",
                ["status"] = "Open"
            };

            var erpVisit = await _erp.CreateDocAsync("Maintenance Visit", erpPayload);
            if (erpVisit is null || !erpVisit.TryGetValue("name", out var nameVal) || nameVal is null)
            {
                throw new InvalidOperationException("ERPNext returned success but did not return a valid document name/ID.");
            }
            erpNextId = nameVal.ToString()!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Maintenance Visit in ERPNext for conversation ticket {ConvId}. Aborting local creation.", conversationId);
            throw;
        }

        // 2. Create locally in DB since ERPNext succeeded
        var dbVisit = new DbVisit
        {
            ErpNextId = erpNextId,
            CustomerErpNextUserId = ticket.CustomerErpNextUserId,
            AssignedUserId = specialistAppUserId,
            AssignedUserName = specName,
            Name = $"Appointment for Ticket: {ticket.Title}",
            Stage = "New",
            Priority = dto?.Priority == "high" ? "1" : "0",
            Description = dto?.Description ?? "Requested via support chat.",
            VisitType = dto?.VisitType ?? "meeting",
            TicketId = ticket.Id,
            CreateDate = DateTime.UtcNow,
            WriteDate = DateTime.UtcNow
        };

        var created = await _visitService.CreateLocalVisitAsync(dbVisit);
        await _ticketRepo.UpdateVisitIdAsync(ticket.Id, created.Id);
        return created;
    }

    public async Task<SupportTicketDto?> GetSupportTicketByIdAsync(int id)
    {
        var ticket = await _ticketRepo.GetByIdAsync(id);
        return ticket is null ? null : MapTicket(ticket);
    }

    public async Task<SupportTicketDto> UpdateSupportTicketAsync(int id, UpdateSupportTicketRequestDto req)
    {
        await _ticketRepo.UpdateAsync(id, null, req.Priority);
        if (req.Title is not null)
        {
            await _ticketRepo.UpdateTitleAsync(id, req.Title);
        }

        var ticket = await _ticketRepo.GetByIdAsync(id);
        return MapTicket(ticket!);
    }

    public async Task<string?> ResolveTicketBySupportAsync(int id)
    {
        var ticket = await _ticketRepo.GetByIdAsync(id);
        if (ticket is null) return null;

        await _ticketRepo.UpdateStatusByTicketIdAsync(id, TicketStatus.Resolved);

        try
        {
            if (!ticket.ErpNextId.StartsWith("PENDING_SYNC"))
            {
                await _erp.UpdateDocAsync("HD Ticket", ticket.ErpNextId, new Dictionary<string, object?> { ["status"] = "Closed" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close ticket in ERPNext.");
        }

        if (ticket.VisitId.HasValue)
        {
            await _visitService.CloseVisitByIdAsync(ticket.VisitId.Value);
        }

        return ticket.ConversationId;
    }

    public async Task<string?> CancelTicketBySupportAsync(int id, string? reason)
    {
        var ticket = await _ticketRepo.GetByIdAsync(id);
        if (ticket is null) return null;

        await _ticketRepo.UpdateStatusByTicketIdAsync(id, TicketStatus.Closed);

        try
        {
            if (!ticket.ErpNextId.StartsWith("PENDING_SYNC"))
            {
                await _erp.UpdateDocAsync("HD Ticket", ticket.ErpNextId, new Dictionary<string, object?> { ["status"] = "Closed" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel ticket in ERPNext.");
        }

        if (ticket.VisitId.HasValue)
        {
            await _visitService.CloseVisitByIdAsync(ticket.VisitId.Value);
        }

        return ticket.ConversationId;
    }

    public async Task CloseTicketByConversationAsync(string conversationId)
    {
        await _ticketRepo.CloseTicketStatusAsync(conversationId);
    }

    public async Task UnlinkConversationAsync(string conversationId)
    {
        await _ticketRepo.UnlinkConversationAsync(conversationId);
    }

    public Task<DbTicket?> GetLastTicketByConversationIdAsync(string conversationId) =>
        _ticketRepo.GetLastByConversationIdAsync(conversationId);

    public Task<int?> GetLastVisitIdByConversationIdAsync(string conversationId) =>
        _ticketRepo.GetLastVisitIdByConversationIdAsync(conversationId);

    public async Task<SupportTicketDto> RateTicketAsync(int ticketId, float rating, string? feedback, string customerErpNextUserId)
    {
        var ticket = await _ticketRepo.GetByIdAsync(ticketId)
            ?? throw new KeyNotFoundException("Ticket not found.");

        if (ticket.CustomerErpNextUserId != customerErpNextUserId)
            throw new UnauthorizedAccessException("Unauthorized to rate this ticket.");

        await _ticketRepo.SaveRatingAsync(ticketId, rating, feedback);
        var updated = await _ticketRepo.GetByIdAsync(ticketId);
        return MapTicket(updated!);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SupportTicketDto MapTicket(DbTicket t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Status = t.Status,
        Priority = t.Priority,
        ProductErpNextId = t.ProductErpNextId,
        ProductName = t.ProductName,
        ConversationId = t.ConversationId,
        CustomerErpNextUserId = t.CustomerErpNextUserId,
        SpecialistName = t.Specialist?.Name,
        SupportName = t.Support?.Name,
        VisitId = t.VisitId,
        CreatedAt = t.CreateDate,
        TicketRating = t.TicketRating,
        TicketRatingFeedback = t.TicketRatingFeedback
    };

    private static VisitDto MapTicketToVisitDto(DbTicket t) => new()
    {
        Id = t.Id,
        Name = t.Title,
        Status = t.Status,
        Priority = t.Priority,
        TicketId = t.Id,
        ConversationId = t.ConversationId,
        CreatedAt = t.CreateDate,
        UpdatedAt = t.WriteDate,
        VisitType = "ticket"
    };

    /// <summary>
    /// Maps any ERPNext or legacy status string to the canonical <see cref="TicketStatus"/> constant.
    /// ERPNext values are already correct (Open, Replied, Resolved, Closed).
    /// Legacy lowercase values (open, in_progress, resolved, closed, solved, cancelled)
    /// are mapped for backward compatibility.
    /// </summary>
    private static string NormaliseErpStatus(string raw) => raw switch
    {
        // ERPNext native — pass through unchanged
        TicketStatus.Open     => TicketStatus.Open,
        TicketStatus.Replied  => TicketStatus.Replied,
        TicketStatus.Resolved => TicketStatus.Resolved,
        TicketStatus.Closed   => TicketStatus.Closed,

        // Legacy lowercase or alternate spellings
        "open"        => TicketStatus.Open,
        "in_progress" => TicketStatus.Replied,
        "replied"     => TicketStatus.Replied,
        "resolved"    => TicketStatus.Resolved,
        "solved"      => TicketStatus.Resolved,
        "closed"      => TicketStatus.Closed,
        "cancelled"   => TicketStatus.Closed,

        // Unknown — default to Open
        _ => TicketStatus.Open
    };
}
