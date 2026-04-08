using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Agon.Infrastructure.Persistence.Entities;

[Table("trial_tester_grants")]
public sealed class TrialTesterGrantEntity
{
    [Key]
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Required]
    [Column("granted_at")]
    public DateTimeOffset GrantedAt { get; set; }

    [Column("granted_by")]
    [MaxLength(200)]
    public string? GrantedBy { get; set; }

    [Required]
    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("revoked_at")]
    public DateTimeOffset? RevokedAt { get; set; }

    [Column("revoked_by")]
    [MaxLength(200)]
    public string? RevokedBy { get; set; }

    [Column("revoke_reason")]
    [MaxLength(400)]
    public string? RevokeReason { get; set; }

    [Required]
    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
