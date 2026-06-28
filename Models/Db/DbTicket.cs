using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("Tickets")]
public class DbTicket
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, MaxLength(254)]
    public string ErpNextId { get; set; } = string.Empty; // HD Ticket name in ERPNext

    [Required, MaxLength(254)]
    public string CustomerErpNextUserId { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string ProductErpNextId { get; set; } = string.Empty; // item_code

    [MaxLength(500)]
    public string ProductName { get; set; } = string.Empty;

    public DateTime WriteDate { get; set; }

    public DateTime CreateDate { get; set; }

    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Mirrors ERPNext HD Ticket status — use TicketStatus constants.
    /// Values: Open | Replied | Resolved | Closed
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = Rihla.Config.TicketStatus.Open;

    /// <summary>low / medium / high</summary>
    [MaxLength(20)]
    public string Priority { get; set; } = "medium";

    [MaxLength(100)]
    public string? ConversationId { get; set; }

    public int? SpecialistAppUserId { get; set; } // Specialist assigned (acting as Engineer)

    public int? SupportAppUserId { get; set; } // Support agent assigned (acting as Customer Care)

    public int? VisitId { get; set; }

    // Rating
    public float? TicketRating { get; set; }

    [MaxLength(1000)]
    public string? TicketRatingFeedback { get; set; }

    public DateTime? TicketRatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(ConversationId))]
    public DbConversation? Conversation { get; set; }

    [ForeignKey(nameof(SpecialistAppUserId))]
    public AppUser? Specialist { get; set; }

    [ForeignKey(nameof(SupportAppUserId))]
    public AppUser? Support { get; set; }
}

