using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("PassportData")]
public class DbPassportData
{
    [Key]
    [Required]
    [MaxLength(100)]
    public string PassportNumber { get; set; } = string.Empty;

    public int UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public AppUser UploadedByUser { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string ConversationId { get; set; } = string.Empty;

    [ForeignKey(nameof(ConversationId))]
    public DbConversation Conversation { get; set; } = null!;

    // ── English Fields ────────────────────────────────────────────────────────
    [MaxLength(200)]
    public string? FullName { get; set; }

    [MaxLength(100)]
    public string? Nationality { get; set; }

    public DateTime? DateOfBirth { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public DateTime? DateOfIssue { get; set; }

    [MaxLength(100)]
    public string? IssuingCountry { get; set; }

    [MaxLength(10)]
    public string? Gender { get; set; }

    [MaxLength(200)]
    public string? PlaceOfBirth { get; set; }

    [MaxLength(200)]
    public string? Profession { get; set; }

    // ── Arabic Fields ─────────────────────────────────────────────────────────
    [MaxLength(200)]
    public string? FullNameAr { get; set; }

    [MaxLength(100)]
    public string? NationalityAr { get; set; }

    [MaxLength(10)]
    public string? GenderAr { get; set; }

    [MaxLength(200)]
    public string? PlaceOfBirthAr { get; set; }

    [MaxLength(200)]
    public string? ProfessionAr { get; set; }

    // ── Egyptian Passport-Specific Fields ─────────────────────────────────────
    [MaxLength(50)]
    public string? NationalId { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public MilitaryStatus? MilitaryStatus { get; set; }

    // ── Meta ──────────────────────────────────────────────────────────────────
    public string? RawJsonData { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    /// <summary>Pending = awaiting user confirmation | Confirmed = saved by user | Canceled = canceled by user</summary>
    public PassportStatus Status { get; set; } = PassportStatus.Pending;

    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
}
