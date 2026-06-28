using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("UserProducts")]
public class DbUserProduct
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, MaxLength(254)]
    public string ErpNextLineName { get; set; } = string.Empty; // unique row ID in Sales Order Item

    [Required, MaxLength(254)]
    public string CustomerErpNextUserId { get; set; } = string.Empty; // customer ERPNext userId/email

    public int? ProductId { get; set; } // FK to DbProduct

    [Required, MaxLength(200)]
    public string ItemCode { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string OrderReference { get; set; } = string.Empty; // Sales Order Name

    public DateTime OrderDate { get; set; }

    public decimal Price { get; set; }

    [MaxLength(50)]
    public string Currency { get; set; } = string.Empty;

    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(ProductId))]
    public DbProduct? Product { get; set; }
}
