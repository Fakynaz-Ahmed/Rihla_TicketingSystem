using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("AppTokens")]
public class AppToken
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>JWT jti claim — globally unique per token.</summary>
    [Required, MaxLength(100)]
    public string Jti { get; set; } = string.Empty;

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Null = active. Non-null = revoked at this timestamp.</summary>
    public DateTime? RevokedAt { get; set; }
}
