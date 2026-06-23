using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("AppUsers")]
public class AppUser
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The ERPNext username (e.g. "john@company.com").</summary>
    [Required, MaxLength(254)]
    public string ErpNextUserId { get; set; } = string.Empty;

    [Required, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>BCrypt hash of the local password set on first login.</summary>
    [MaxLength(500)]
    public string? PasswordHash { get; set; }

    /// <summary>Department from ERPNext (e.g. "Sales", "Accounts", "HR").</summary>
    [MaxLength(200)]
    public string? Department { get; set; }

    /// <summary>Primary ERPNext role used for permissions (e.g. "Sales Manager", "Accounts User").</summary>
    [MaxLength(200)]
    public string? PrimaryRole { get; set; }

    /// <summary>JSON array of allowed ERP module names for this user (e.g. ["Sales","Accounts"]).</summary>
    [MaxLength(2000)]
    public string? AllowedModules { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    /// <summary>Relative URL to avatar image (e.g. /avatars/5.jpg).</summary>
    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    /// <summary>Preferred response language: "ar" or "en".</summary>
    [MaxLength(5)]
    public string Language { get; set; } = "ar";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<AppToken> Tokens { get; set; } = [];
    public ICollection<DbConversation> Conversations { get; set; } = [];
}
