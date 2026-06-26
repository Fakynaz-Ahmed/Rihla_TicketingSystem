using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Rihla.DTOs;

public class VisitSpecialistDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
}

public class VisitCustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("erp_next_user_id")]
    public string ErpNextUserId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class VisitDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("specialist")]
    public VisitSpecialistDto? Specialist { get; set; }

    [JsonPropertyName("customer")]
    public VisitCustomerDto? Customer { get; set; }

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("deadline")]
    public DateTime? Deadline { get; set; }

    [JsonPropertyName("visit_date")]
    public DateTime? VisitDate { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("conversation_id")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("ticket_id")]
    public int? TicketId { get; set; }

    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("visit_type")]
    public string VisitType { get; set; } = string.Empty;

    [JsonPropertyName("maintenance_type")]
    public string MaintenanceType { get; set; } = string.Empty;

    [JsonPropertyName("products")]
    public List<VisitProductDto> Products { get; set; } = [];

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("close_attachment_url")]
    public string? CloseAttachmentUrl { get; set; }

    [JsonPropertyName("visit_rating")]
    public float? VisitRating { get; set; }

    [JsonPropertyName("visit_rating_feedback")]
    public string? VisitRatingFeedback { get; set; }
}

public class RequestVisitDto
{
    [Required]
    [JsonPropertyName("visit_type")]
    public string VisitType { get; set; } = string.Empty; // "meeting" or "site_visit"

    [Required]
    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";

    [JsonPropertyName("maintenance_type")]
    public string? MaintenanceType { get; set; }

    [Required]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("product_ids")]
    public List<int> ProductIds { get; set; } = [];
}

public class CreateVisitRequestDto
{
    [Required]
    [JsonPropertyName("customer_id")]
    public string CustomerId { get; set; } = string.Empty; // customer ERPNext userId

    [Required]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "new";

    [JsonPropertyName("ticket_id")]
    public int? TicketId { get; set; }

    [JsonPropertyName("specialist_id")]
    public int? SpecialistId { get; set; } // local AppUser ID

    [JsonPropertyName("visit_date")]
    public DateTime? VisitDate { get; set; }
}

public class UpdateVisitDto
{
    [JsonPropertyName("visit_date")]
    public DateTime? VisitDate { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("specialist_id")]
    public int? SpecialistId { get; set; } // local AppUser ID

    [JsonPropertyName("cancel")]
    public bool? Cancel { get; set; }

    [JsonPropertyName("cancellation_reason")]
    public string? CancellationReason { get; set; }
}

public class UpdateVisitStatusDto
{
    [Required]
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonIgnore]
    public IFormFile? Attachment { get; set; }
}

public class CancelSpecialistVisitDto
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class RateVisitDto
{
    [Required]
    [Range(1.0, 5.0)]
    [JsonPropertyName("rating")]
    public float Rating { get; set; }

    [MaxLength(1000)]
    [JsonPropertyName("feedback")]
    public string? Feedback { get; set; }
}

public class VisitActivityDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }
}

public class VisitFilterDto
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }

    [JsonPropertyName("from")]
    public DateTime? From { get; set; }

    [JsonPropertyName("to")]
    public DateTime? To { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; } = 20;
}

public class VisitTicketDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = string.Empty;

    [JsonPropertyName("conversation_id")]
    public string? ConversationId { get; set; }
}

public class VisitProductDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public class VisitDetailDto : VisitDto
{
    [JsonPropertyName("ticket")]
    public VisitTicketDto? Ticket { get; set; }

    [JsonPropertyName("product")]
    public VisitProductDto? ProductDetail { get; set; }
}

public class VisitsResultDto
{
    [JsonPropertyName("items")]
    public List<VisitDto> Items { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
}
