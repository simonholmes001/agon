using Agon.Application.Interfaces;
using Agon.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Agon.Infrastructure.Persistence.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of attachment metadata persistence.
/// </summary>
public sealed class AttachmentRepository : IAttachmentRepository
{
    private readonly AgonDbContext _dbContext;

    public AttachmentRepository(AgonDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SessionAttachment> CreateAsync(
        SessionAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        var entity = new SessionAttachmentEntity
        {
            Id = attachment.AttachmentId,
            SessionId = attachment.SessionId,
            UserId = attachment.UserId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            BlobName = attachment.BlobName,
            BlobUri = attachment.BlobUri,
            AccessUrl = attachment.AccessUrl,
            ExtractedText = attachment.ExtractedText,
            UploadedAt = attachment.UploadedAt.UtcDateTime,
            ExtractionStatus = attachment.ExtractionStatus,
            ExtractionProgressPercent = Math.Clamp(attachment.ExtractionProgressPercent, 0, 100),
            ExtractionError = attachment.ExtractionError,
            ExtractionUpdatedAt = (attachment.ExtractionUpdatedAt ?? attachment.UploadedAt).UtcDateTime
        };

        _dbContext.SessionAttachments.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return attachment;
    }

    public async Task UpdateExtractionAsync(
        Guid attachmentId,
        string extractionStatus,
        int extractionProgressPercent,
        string? extractedText,
        string? extractionError,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.SessionAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        var normalizedStatus = NormalizeStatus(extractionStatus);
        entity.ExtractionStatus = normalizedStatus;
        entity.ExtractionProgressPercent = Math.Clamp(extractionProgressPercent, 0, 100);
        entity.ExtractionError = extractionError;
        entity.ExtractionUpdatedAt = DateTime.UtcNow;

        if (extractedText is not null || normalizedStatus == AttachmentExtractionStatus.Ready)
        {
            entity.ExtractedText = extractedText;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionAttachment>> ListBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.SessionAttachments
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.UploadedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return entities.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<SessionAttachment>> ListExpiredAsync(
        DateTimeOffset uploadedBefore,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Array.Empty<SessionAttachment>();
        }

        var entities = await _dbContext.SessionAttachments
            .Where(a => a.UploadedAt < uploadedBefore.UtcDateTime)
            .OrderBy(a => a.UploadedAt)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return entities.Select(ToModel).ToList();
    }

    public async Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        await _dbContext.SessionAttachments
            .Where(a => a.Id == attachmentId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static SessionAttachment ToModel(SessionAttachmentEntity entity) =>
        new(
            entity.Id,
            entity.SessionId,
            entity.UserId,
            entity.FileName,
            entity.ContentType,
            entity.SizeBytes,
            entity.BlobName,
            entity.BlobUri,
            entity.AccessUrl,
            entity.ExtractedText,
            DateTime.SpecifyKind(entity.UploadedAt, DateTimeKind.Utc),
            NormalizeStatus(entity.ExtractionStatus),
            Math.Clamp(entity.ExtractionProgressPercent, 0, 100),
            entity.ExtractionError,
            entity.ExtractionUpdatedAt is null
                ? null
                : DateTime.SpecifyKind(entity.ExtractionUpdatedAt.Value, DateTimeKind.Utc));

    private static string NormalizeStatus(string? extractionStatus)
    {
        if (string.IsNullOrWhiteSpace(extractionStatus))
        {
            return AttachmentExtractionStatus.Ready;
        }

        var normalized = extractionStatus.Trim().ToLowerInvariant();
        return AttachmentExtractionStatus.IsKnown(normalized)
            ? normalized
            : AttachmentExtractionStatus.Ready;
    }
}
