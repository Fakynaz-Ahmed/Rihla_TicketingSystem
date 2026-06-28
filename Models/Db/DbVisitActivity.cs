using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rihla.Models.Db;

[Table("VisitActivities")]
public class DbVisitActivity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int VisitId { get; set; }

    [ForeignKey(nameof(VisitId))]
    public DbVisit? Visit { get; set; }

    /// <summary>status_change | assignment | reschedule | cancel | note | sync</summary>
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(200)]
    public string User { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? StatusFrom { get; set; }

    [MaxLength(100)]
    public string? StatusTo { get; set; }
}
