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
            ExtractionStatus = ToStorageStatus(attachment.ExtractionStatus),
            ExtractionFailureReason = attachment.ExtractionFailureReason,
            UploadedAt = attachment.UploadedAt.UtcDateTime
        };

        _dbContext.SessionAttachments.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return attachment;
    }

    public async Task<SessionAttachment> UpdateExtractionStateAsync(
        Guid attachmentId,
        AttachmentExtractionStatus extractionStatus,
        string? extractedText,
        string? extractionFailureReason,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.SessionAttachments
            .SingleOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (entity is null)
        {
            throw new InvalidOperationException($"Attachment {attachmentId} not found.");
        }

        var currentStatus = ParseStorageStatus(entity.ExtractionStatus);
        if (!CanTransition(currentStatus, extractionStatus))
        {
            return ToModel(entity);
        }

        entity.ExtractionStatus = ToStorageStatus(extractionStatus);
        entity.ExtractedText = extractedText;
        entity.ExtractionFailureReason = extractionFailureReason;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<bool> TryTransitionExtractionStateAsync(
        Guid attachmentId,
        AttachmentExtractionStatus expectedCurrentStatus,
        AttachmentExtractionStatus nextStatus,
        string? extractedText,
        string? extractionFailureReason,
        CancellationToken cancellationToken = default)
    {
        if (!CanTransition(expectedCurrentStatus, nextStatus))
        {
            return false;
        }

        if (!SupportsSetBasedUpdates())
        {
            var entity = await _dbContext.SessionAttachments
                .SingleOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
            if (entity is null)
            {
                return false;
            }

            if (!string.Equals(
                    entity.ExtractionStatus,
                    ToStorageStatus(expectedCurrentStatus),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            entity.ExtractionStatus = ToStorageStatus(nextStatus);
            entity.ExtractedText = extractedText;
            entity.ExtractionFailureReason = extractionFailureReason;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var expectedStorageStatus = ToStorageStatus(expectedCurrentStatus);
        var nextStorageStatus = ToStorageStatus(nextStatus);

        var affected = await _dbContext.SessionAttachments
            .Where(a => a.Id == attachmentId && a.ExtractionStatus == expectedStorageStatus)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.ExtractionStatus, nextStorageStatus)
                .SetProperty(a => a.ExtractedText, extractedText)
                .SetProperty(a => a.ExtractionFailureReason, extractionFailureReason),
                cancellationToken);

        return affected == 1;
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

    public async Task<IReadOnlyList<SessionAttachment>> ListByExtractionStatusAsync(
        AttachmentExtractionStatus status,
        int limit,
        DateTimeOffset? uploadedBefore = null,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Array.Empty<SessionAttachment>();
        }

        var statusValue = ToStorageStatus(status);
        var query = _dbContext.SessionAttachments
            .Where(a => a.ExtractionStatus == statusValue);

        if (uploadedBefore is not null)
        {
            query = query.Where(a => a.UploadedAt <= uploadedBefore.Value.UtcDateTime);
        }

        var entities = await query
            .OrderBy(a => a.UploadedAt)
            .Take(limit)
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
            ParseStorageStatus(entity.ExtractionStatus),
            entity.ExtractionFailureReason);

    private static string ToStorageStatus(AttachmentExtractionStatus status) =>
        status.ToString().ToLowerInvariant();

    private static AttachmentExtractionStatus ParseStorageStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AttachmentExtractionStatus.Uploaded;
        }

        return Enum.TryParse<AttachmentExtractionStatus>(value, ignoreCase: true, out var parsed)
            ? parsed
            : AttachmentExtractionStatus.Uploaded;
    }

    private static bool CanTransition(AttachmentExtractionStatus current, AttachmentExtractionStatus next)
    {
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            AttachmentExtractionStatus.Uploaded =>
                next is AttachmentExtractionStatus.Extracting or AttachmentExtractionStatus.Failed,
            AttachmentExtractionStatus.Extracting =>
                next is AttachmentExtractionStatus.Uploaded or AttachmentExtractionStatus.Ready or AttachmentExtractionStatus.Failed,
            AttachmentExtractionStatus.Ready => false,
            AttachmentExtractionStatus.Failed => false,
            _ => false
        };
    }

    private bool SupportsSetBasedUpdates()
    {
        var providerName = _dbContext.Database.ProviderName;
        return !string.IsNullOrWhiteSpace(providerName)
            && !providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase);
    }
}
