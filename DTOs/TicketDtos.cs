using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Rihla.DTOs;

public class TicketMachineDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }
}

public class TicketDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("machine")]
    public TicketMachineDto Machine { get; set; } = new();

    [JsonPropertyName("ticket_number")]
    public int TicketNumber { get; set; }

    [JsonPropertyName("invoice_number")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("last_updated")]
    public DateTime LastUpdated { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class TicketDetailDto : TicketDto
{
    [JsonPropertyName("attachments")]
    public List<string> Attachments { get; set; } = [];
}

public class CreateTicketRequestDto
{
    [Required]
    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("attachments")]
    public List<string>? Attachments { get; set; }
}

public class AttachmentUploadDto
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("file_type")]
    public string FileType { get; set; } = string.Empty;

    [JsonPropertyName("size_kb")]
    public long SizeKb { get; set; }
}

// ── Support ticket DTOs (conversation-linked tickets) ─────────────────────────

public class SupportTicketDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>open / in_progress / resolved / closed</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "open";

    /// <summary>low / medium / high</summary>
    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";

    [JsonPropertyName("product_erpnext_id")]
    public string ProductErpNextId { get; set; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("conversation_id")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("customer_erpnext_user_id")]
    public string? CustomerErpNextUserId { get; set; }

    [JsonPropertyName("specialist_name")]
    public string? SpecialistName { get; set; }

    [JsonPropertyName("support_name")]
    public string? SupportName { get; set; }

    [JsonPropertyName("visit_id")]
    public int? VisitId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("ticket_rating")]
    public float? TicketRating { get; set; }

    [JsonPropertyName("ticket_rating_feedback")]
    public string? TicketRatingFeedback { get; set; }
}


public class CreateSupportTicketRequestDto
{
    [Required]
    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";
}

public class CreateTicketAdminRequestDto
{
    /// <summary>ERPNext username/email of the customer.</summary>
    [Required]
    [JsonPropertyName("customer_id")]
    public string CustomerId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Item code of product/service (from ERPNext).</summary>
    [Required]
    [JsonPropertyName("product_erpnext_id")]
    public string ProductErpNextId { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";

    /// <summary>Local DB user ID (AppUser.Id) of the Support agent to assign.</summary>
    [Required]
    [JsonPropertyName("support_id")]
    public int SupportId { get; set; }
}

public class UpdateSupportTicketRequestDto
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>low / medium / high</summary>
    [JsonPropertyName("priority")]
    public string? Priority { get; set; }
}

public class ResolveTicketDto
{
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class CancelTicketDto
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class RateTicketDto
{
    [Required]
    [Range(1.0, 5.0)]
    [JsonPropertyName("rating")]
    public float Rating { get; set; }

    [MaxLength(1000)]
    [JsonPropertyName("feedback")]
    public string? Feedback { get; set; }
}

public class TicketFilterDto
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

