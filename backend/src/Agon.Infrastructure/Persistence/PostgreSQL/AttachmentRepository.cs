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
            UploadedAt = attachment.UploadedAt.UtcDateTime
        };

        _dbContext.SessionAttachments.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return attachment;
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
            DateTime.SpecifyKind(entity.UploadedAt, DateTimeKind.Utc));
}
