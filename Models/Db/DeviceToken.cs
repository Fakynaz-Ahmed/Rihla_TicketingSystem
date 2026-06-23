using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("DeviceTokens")]
public class DeviceToken
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;
    
    [Required]
    public string Token { get; set; } = string.Empty;
    
    /// <summary>"android" | "ios"</summary>
    [Required, MaxLength(20)]
    public string Platform { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
