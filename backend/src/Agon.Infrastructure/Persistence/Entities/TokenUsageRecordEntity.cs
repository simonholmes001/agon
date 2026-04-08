using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Agon.Infrastructure.Persistence.Entities;

[Table("token_usage_records")]
public sealed class TokenUsageRecordEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Required]
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Required]
    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Required]
    [Column("agent_id")]
    [MaxLength(100)]
    public string AgentId { get; set; } = string.Empty;

    [Required]
    [Column("provider")]
    [MaxLength(100)]
    public string Provider { get; set; } = string.Empty;

    [Required]
    [Column("model")]
    [MaxLength(200)]
    public string Model { get; set; } = string.Empty;

    [Required]
    [Column("prompt_tokens")]
    public int PromptTokens { get; set; }

    [Required]
    [Column("completion_tokens")]
    public int CompletionTokens { get; set; }

    [Required]
    [Column("total_tokens")]
    public int TotalTokens { get; set; }

    [Required]
    [Column("source")]
    [MaxLength(50)]
    public string Source { get; set; } = string.Empty;

    [Required]
    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }
}
