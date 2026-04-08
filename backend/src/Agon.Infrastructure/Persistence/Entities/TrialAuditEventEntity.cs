using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Agon.Infrastructure.Persistence.Entities;

[Table("trial_audit_events")]
public sealed class TrialAuditEventEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Required]
    [Column("action")]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [Column("outcome")]
    [MaxLength(50)]
    public string Outcome { get; set; } = string.Empty;

    [Column("user_id")]
    public Guid? UserId { get; set; }

    [Column("actor")]
    [MaxLength(200)]
    public string? Actor { get; set; }

    [Column("reason_code")]
    [MaxLength(100)]
    public string? ReasonCode { get; set; }

    [Column("details_json")]
    public string? DetailsJson { get; set; }

    [Required]
    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }
}
