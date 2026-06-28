using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("Products")]
public class DbProduct
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, MaxLength(254)]
    public string ErpNextId { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string ItemCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal ListPrice { get; set; }

    [MaxLength(200)]
    public string Category { get; set; } = string.Empty;

    public bool Active { get; set; }

    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(1000)]
    public string? ImageUrl { get; set; }
}
