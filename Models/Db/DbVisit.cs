using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("Visits")]
public class DbVisit
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, MaxLength(254)]
    public string ErpNextId { get; set; } = string.Empty; // Maintenance Visit ID in ERPNext

    [Required, MaxLength(254)]
    public string CustomerErpNextUserId { get; set; } = string.Empty;

    public int AssignedUserId { get; set; } // AppUser ID of Assigned Specialist/Guide

    [MaxLength(200)]
    public string AssignedUserName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string PartnerName { get; set; } = string.Empty; // Customer Name

    [Required, MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Stage { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Priority { get; set; } = "0";

    public bool IsDone { get; set; }

    public DateTime? PlannedStart { get; set; }

    public DateTime? PlannedEnd { get; set; }

    public DateTime? Deadline { get; set; }

    public DateTime? VisitDate { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>JSON array of tag name strings.</summary>
    [MaxLength(2000)]
    public string TagsJson { get; set; } = "[]";

    public DateTime CreateDate { get; set; }

    public DateTime WriteDate { get; set; }

    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    // Visit request details
    [MaxLength(50)]
    public string VisitType { get; set; } = string.Empty; // e.g. "meeting", "site_visit", "guide_service"

    [MaxLength(100)]
    public string MaintenanceType { get; set; } = string.Empty;

    /// <summary>JSON array of local DB Product IDs requested for this visit.</summary>
    public string RequestedProductsJson { get; set; } = "[]";

    public bool IsCancelled { get; set; }

    [MaxLength(1000)]
    public string? CancellationReason { get; set; }

    [MaxLength(2000)]
    public string? CloseAttachmentUrl { get; set; }

    // Rating
    public float? VisitRating { get; set; }

    [MaxLength(1000)]
    public string? VisitRatingFeedback { get; set; }

    public DateTime? VisitRatedAt { get; set; }

    // Relations
    public int? TicketId { get; set; }

    [ForeignKey(nameof(TicketId))]
    public DbTicket? Ticket { get; set; }

    public int? ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public DbProduct? Product { get; set; }
}
