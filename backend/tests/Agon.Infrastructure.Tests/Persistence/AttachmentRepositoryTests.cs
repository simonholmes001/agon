using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Infrastructure.Persistence.PostgreSQL;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Agon.Infrastructure.Tests.Persistence;

/// <summary>
/// Unit tests for AttachmentRepository using in-memory database.
/// </summary>
public class AttachmentRepositoryTests : IDisposable
{
    private readonly AgonDbContext _dbContext;
    private readonly IAttachmentRepository _repository;

    public AttachmentRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AgonDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AgonDbContext(options);
        _repository = new AttachmentRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsync_StoresAttachmentInDatabase()
    {
        // Arrange
        var attachment = BuildAttachment();

        // Act
        var result = await _repository.CreateAsync(attachment, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.AttachmentId.Should().Be(attachment.AttachmentId);

        var entity = await _dbContext.SessionAttachments.FirstOrDefaultAsync(a => a.Id == attachment.AttachmentId);
        entity.Should().NotBeNull();
        entity!.FileName.Should().Be("test-document.pdf");
        entity.ContentType.Should().Be("application/pdf");
        entity.SizeBytes.Should().Be(1024);
    }

    [Fact]
    public async Task CreateAsync_ReturnsOriginalAttachmentModel()
    {
        // Arrange
        var attachment = BuildAttachment();

        // Act
        var result = await _repository.CreateAsync(attachment, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(attachment);
    }

    [Fact]
    public async Task ListBySessionAsync_WhenNoAttachments_ReturnsEmptyList()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        var attachments = await _repository.ListBySessionAsync(sessionId, CancellationToken.None);

        // Assert
        attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task ListBySessionAsync_WhenAttachmentsExist_ReturnsSortedByUploadedAt()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow;

        var att1 = BuildAttachment(sessionId: sessionId, fileName: "first.pdf", uploadedAt: baseTime.AddMinutes(-10));
        var att2 = BuildAttachment(sessionId: sessionId, fileName: "second.pdf", uploadedAt: baseTime.AddMinutes(-5));
        var att3 = BuildAttachment(sessionId: sessionId, fileName: "third.pdf", uploadedAt: baseTime);

        // Add in reverse order to verify sorting
        await _repository.CreateAsync(att3, CancellationToken.None);
        await _repository.CreateAsync(att1, CancellationToken.None);
        await _repository.CreateAsync(att2, CancellationToken.None);

        // Act
        var attachments = await _repository.ListBySessionAsync(sessionId, CancellationToken.None);

        // Assert
        attachments.Should().HaveCount(3);
        attachments[0].FileName.Should().Be("first.pdf");  // earliest
        attachments[1].FileName.Should().Be("second.pdf");
        attachments[2].FileName.Should().Be("third.pdf");  // latest
    }

    [Fact]
    public async Task ListBySessionAsync_OnlyReturnsAttachmentsForSpecifiedSession()
    {
        // Arrange
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        await _repository.CreateAsync(BuildAttachment(sessionId: session1, fileName: "session1.pdf"), CancellationToken.None);
        await _repository.CreateAsync(BuildAttachment(sessionId: session2, fileName: "session2.pdf"), CancellationToken.None);

        // Act
        var attachments = await _repository.ListBySessionAsync(session1, CancellationToken.None);

        // Assert
        attachments.Should().HaveCount(1);
        attachments[0].SessionId.Should().Be(session1);
        attachments[0].FileName.Should().Be("session1.pdf");
    }

    [Fact]
    public async Task ListBySessionAsync_CorrectlyMapsAllFields()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var uploadedAt = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        var attachment = new SessionAttachment(
            AttachmentId: attachmentId,
            SessionId: sessionId,
            UserId: userId,
            FileName: "my-document.docx",
            ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            SizeBytes: 20480,
            BlobName: "blobs/my-document.docx",
            BlobUri: "https://storage.azure.com/container/blob",
            AccessUrl: "https://storage.azure.com/container/blob?sas=token",
            ExtractedText: "Document text content here",
            UploadedAt: uploadedAt);

        await _repository.CreateAsync(attachment, CancellationToken.None);

        // Act
        var attachments = await _repository.ListBySessionAsync(sessionId, CancellationToken.None);

        // Assert
        attachments.Should().HaveCount(1);
        var retrieved = attachments[0];
        retrieved.AttachmentId.Should().Be(attachmentId);
        retrieved.SessionId.Should().Be(sessionId);
        retrieved.UserId.Should().Be(userId);
        retrieved.FileName.Should().Be("my-document.docx");
        retrieved.ContentType.Should().Contain("wordprocessingml");
        retrieved.SizeBytes.Should().Be(20480);
        retrieved.ExtractedText.Should().Be("Document text content here");
    }

    [Fact]
    public async Task CreateAsync_WithNullExtractedText_Stores_And_ReturnsNull()
    {
        // Arrange
        var attachment = BuildAttachment(extractedText: null);

        // Act
        await _repository.CreateAsync(attachment, CancellationToken.None);
        var attachments = await _repository.ListBySessionAsync(attachment.SessionId, CancellationToken.None);

        // Assert
        attachments.Should().HaveCount(1);
        attachments[0].ExtractedText.Should().BeNull();
    }

    [Fact]
    public async Task ListBySessionAsync_WithMultipleAttachments_ReturnsAll()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow;

        for (int i = 0; i < 5; i++)
        {
            await _repository.CreateAsync(
                BuildAttachment(sessionId: sessionId, fileName: $"file-{i}.pdf", uploadedAt: baseTime.AddSeconds(i)),
                CancellationToken.None);
        }

        // Act
        var attachments = await _repository.ListBySessionAsync(sessionId, CancellationToken.None);

        // Assert
        attachments.Should().HaveCount(5);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SessionAttachment BuildAttachment(
        Guid? sessionId = null,
        string fileName = "test-document.pdf",
        string? extractedText = "Extracted text",
        DateTime? uploadedAt = null)
    {
        return new SessionAttachment(
            AttachmentId: Guid.NewGuid(),
            SessionId: sessionId ?? Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            FileName: fileName,
            ContentType: "application/pdf",
            SizeBytes: 1024,
            BlobName: $"blobs/{fileName}",
            BlobUri: $"https://storage.azure.com/container/{fileName}",
            AccessUrl: $"https://storage.azure.com/container/{fileName}?sas=token",
            ExtractedText: extractedText,
            UploadedAt: uploadedAt ?? DateTime.UtcNow);
    }
}
