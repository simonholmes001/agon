using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Agon.Infrastructure.Persistence.Entities;

/// <summary>
/// EF Core entity for agent messages (conversation history).
/// Per backend-implementation.instructions.md: Infrastructure entities map to database tables.
/// </summary>
[Table("agent_messages")]
public sealed class AgentMessageEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Required]
    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Required]
    [Column("agent_id")]
    [MaxLength(100)]
    public string AgentId { get; set; } = string.Empty;

    [Required]
    [Column("message")]
    public string Message { get; set; } = string.Empty;

    [Required]
    [Column("round")]
    public int Round { get; set; }

    [Required]
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
