using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Agon.Infrastructure.Persistence.Entities;

[Table("trial_controls")]
public sealed class TrialControlEntity
{
    [Key]
    [Column("name")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("value")]
    [MaxLength(200)]
    public string Value { get; set; } = string.Empty;

    [Required]
    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
