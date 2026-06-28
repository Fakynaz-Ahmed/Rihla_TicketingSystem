using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("Conversations")]
public class DbConversation
{
    [Key, MaxLength(100)]
    public string ConversationId { get; set; } = string.Empty;

    // ── User ──────────────────────────────────────────────────────────────────
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(200)]
    public string UserName { get; set; } = string.Empty;

    // ── AI Thread ─────────────────────────────────────────────────────────────
    /// <summary>OpenAI thread ID for maintaining conversation context.</summary>
    [MaxLength(200)]
    public string? AiThreadId { get; set; }

    // ── Status ────────────────────────────────────────────────────────────────
    /// <summary>active | ended</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = nameof(ConversationStatus.ai);

    /// <summary>Short title auto-generated from the first message.</summary>
    [MaxLength(300)]
    public string? Title { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    // ── Support / Specialist Routing ──────────────────────────────────────────
    public int? SupportId { get; set; }

    [ForeignKey(nameof(SupportId))]
    public AppUser? Support { get; set; }

    [MaxLength(200)]
    public string? SupportName { get; set; }

    public int? SpecialistId { get; set; }

    [ForeignKey(nameof(SpecialistId))]
    public AppUser? Specialist { get; set; }

    [MaxLength(200)]
    public string? SpecialistName { get; set; }

    [MaxLength(1000)]
    public string? EscalationReason { get; set; }

    public DateTime? EscalatedAt { get; set; }
}

