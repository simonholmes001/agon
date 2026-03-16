using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using Agon.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Infrastructure.Persistence.PostgreSQL;

/// <summary>
/// EF Core DbContext for Agon's PostgreSQL persistence layer.
/// </summary>
public class AgonDbContext : DbContext
{
    public AgonDbContext(DbContextOptions<AgonDbContext> options) : base(options)
    {
    }

    // Truth Map state - stored as JSONB for flexibility
    public DbSet<TruthMapEntity> TruthMaps => Set<TruthMapEntity>();
    
    // Append-only patch event log
    public DbSet<TruthMapPatchEvent> TruthMapPatchEvents => Set<TruthMapPatchEvent>();

    // Sessions
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();

    // Agent messages (conversation history)
    public DbSet<AgentMessageEntity> AgentMessages => Set<AgentMessageEntity>();

    // Session attachment metadata
    public DbSet<SessionAttachmentEntity> SessionAttachments => Set<SessionAttachmentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TruthMapEntity - stores current state as JSONB
        modelBuilder.Entity<TruthMapEntity>(entity =>
        {
            entity.ToTable("truth_maps");
            entity.HasKey(e => e.SessionId);
            
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.CurrentState)
                .HasColumnName("current_state")
                .HasColumnType("jsonb")
                .IsRequired();
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        // TruthMapPatchEvent - append-only event log
        modelBuilder.Entity<TruthMapPatchEvent>(entity =>
        {
            entity.ToTable("truth_map_patch_events");
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.PatchJson)
                .HasColumnName("patch_json")
                .HasColumnType("jsonb")
                .IsRequired();
            entity.Property(e => e.Agent).HasColumnName("agent").IsRequired();
            entity.Property(e => e.Round).HasColumnName("round");
            entity.Property(e => e.AppliedAt).HasColumnName("applied_at");
            
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => new { e.SessionId, e.Round });
        });

        // SessionEntity
        modelBuilder.Entity<SessionEntity>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Mode).HasColumnName("mode").IsRequired();
            entity.Property(e => e.FrictionLevel).HasColumnName("friction_level");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Phase).HasColumnName("phase").IsRequired();
            entity.Property(e => e.CurrentRound).HasColumnName("current_round");
            entity.Property(e => e.TokensUsed).HasColumnName("tokens_used");
            entity.Property(e => e.TargetedLoopCount).HasColumnName("targeted_loop_count");
            entity.Property(e => e.ClarificationIncomplete).HasColumnName("clarification_incomplete");
            entity.Property(e => e.ClarificationRoundCount).HasColumnName("ClarificationRoundCount");
            entity.Property(e => e.ForkedFrom).HasColumnName("forked_from");
            entity.Property(e => e.ForkSnapshotId).HasColumnName("fork_snapshot_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
        });

        // SessionAttachmentEntity
        modelBuilder.Entity<SessionAttachmentEntity>(entity =>
        {
            entity.ToTable("session_attachments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
            entity.Property(e => e.ContentType).HasColumnName("content_type").IsRequired();
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.BlobName).HasColumnName("blob_name").IsRequired();
            entity.Property(e => e.BlobUri).HasColumnName("blob_uri").IsRequired();
            entity.Property(e => e.AccessUrl).HasColumnName("access_url").IsRequired();
            entity.Property(e => e.ExtractedText).HasColumnName("extracted_text");
            entity.Property(e => e.UploadedAt).HasColumnName("uploaded_at");

            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => new { e.SessionId, e.UploadedAt });
            entity.HasIndex(e => e.UserId);
        });
    }
}

/// <summary>
/// Entity for storing the current Truth Map state as JSONB.
/// </summary>
public class TruthMapEntity
{
    public Guid SessionId { get; set; }
    public string CurrentState { get; set; } = "{}"; // JSONB serialized TruthMap
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Entity for append-only patch event log.
/// </summary>
public class TruthMapPatchEvent
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public string PatchJson { get; set; } = "{}"; // JSONB serialized TruthMapPatch
    public string Agent { get; set; } = string.Empty;
    public int Round { get; set; }
    public DateTime AppliedAt { get; set; }
}

/// <summary>
/// Entity for session metadata.
/// </summary>
public class SessionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Mode { get; set; } = string.Empty; // "quick" or "deep"
    public int FrictionLevel { get; set; }
    public string Status { get; set; } = string.Empty; // "active", "complete", etc.
    public string Phase { get; set; } = string.Empty; // Current session phase
    public int CurrentRound { get; set; }
    public int TokensUsed { get; set; }
    public int TargetedLoopCount { get; set; }
    public bool ClarificationIncomplete { get; set; }
    public int ClarificationRoundCount { get; set; }
    public Guid? ForkedFrom { get; set; }
    public Guid? ForkSnapshotId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Entity for uploaded session attachment metadata.
/// </summary>
public class SessionAttachmentEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string BlobName { get; set; } = string.Empty;
    public string BlobUri { get; set; } = string.Empty;
    public string AccessUrl { get; set; } = string.Empty;
    public string? ExtractedText { get; set; }
    public DateTime UploadedAt { get; set; }
}
