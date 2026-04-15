using Agon.Api.Services;
using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;
using Agon.Infrastructure.Attachments;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Agon.Integration.Tests;

public class AttachmentExtractionProcessorTests
{
    [Fact]
    public async Task ProcessAsync_Should_MarkReady_When_ExtractionSucceeds()
    {
        // Arrange
        var attachment = BuildAttachment();
        var sessionService = Substitute.For<ISessionService>();
        var storage = Substitute.For<IAttachmentStorageService>();
        var extractor = Substitute.For<IAttachmentTextExtractor>();
        var options = BuildOptions();
        var logger = Substitute.For<ILogger<AttachmentExtractionProcessor>>();

        storage.OpenReadAsync(attachment.BlobName, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test payload")));
        extractor.ExtractAsync(
                Arg.Any<byte[]>(),
                attachment.FileName,
                attachment.ContentType,
                Arg.Any<CancellationToken>())
            .Returns("extracted text");

        var sut = new AttachmentExtractionProcessor(sessionService, storage, extractor, options, logger);

        // Act
        await sut.ProcessAsync(attachment, CancellationToken.None);

        // Assert
        await sessionService.Received().UpdateAttachmentExtractionAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Extracting,
            20,
            null,
            null,
            Arg.Any<CancellationToken>());

        await sessionService.Received().UpdateAttachmentExtractionAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Ready,
            100,
            "extracted text",
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_Should_MarkFailed_When_BlobMissing()
    {
        // Arrange
        var attachment = BuildAttachment();
        var sessionService = Substitute.For<ISessionService>();
        var storage = Substitute.For<IAttachmentStorageService>();
        var extractor = Substitute.For<IAttachmentTextExtractor>();
        var options = BuildOptions();
        var logger = Substitute.For<ILogger<AttachmentExtractionProcessor>>();

        storage.OpenReadAsync(attachment.BlobName, Arg.Any<CancellationToken>())
            .Returns((Stream?)null);

        var sut = new AttachmentExtractionProcessor(sessionService, storage, extractor, options, logger);

        // Act
        await sut.ProcessAsync(attachment, CancellationToken.None);

        // Assert
        await sessionService.Received().UpdateAttachmentExtractionAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Failed,
            100,
            Arg.Any<string?>(),
            Arg.Is<string>(error => !string.IsNullOrWhiteSpace(error)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_Should_MarkFailed_When_ExtractorThrows()
    {
        // Arrange
        var attachment = BuildAttachment();
        var sessionService = Substitute.For<ISessionService>();
        var storage = Substitute.For<IAttachmentStorageService>();
        var extractor = Substitute.For<IAttachmentTextExtractor>();
        var options = BuildOptions();
        var logger = Substitute.For<ILogger<AttachmentExtractionProcessor>>();

        storage.OpenReadAsync(attachment.BlobName, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test payload")));
        extractor.ExtractAsync(
                Arg.Any<byte[]>(),
                attachment.FileName,
                attachment.ContentType,
                Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(x => throw new InvalidOperationException("extract failure"));

        var sut = new AttachmentExtractionProcessor(sessionService, storage, extractor, options, logger);

        // Act
        await sut.ProcessAsync(attachment, CancellationToken.None);

        // Assert
        await sessionService.Received().UpdateAttachmentExtractionAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Failed,
            100,
            Arg.Is<string?>(value => value == null),
            Arg.Is<string>(error => error.Contains("Extraction failed", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_Should_MarkFailed_When_File_Exceeds_Extraction_Size_Limit()
    {
        var attachment = BuildAttachment() with
        {
            SizeBytes = 5 * 1024 * 1024
        };
        var sessionService = Substitute.For<ISessionService>();
        var storage = Substitute.For<IAttachmentStorageService>();
        var extractor = Substitute.For<IAttachmentTextExtractor>();
        var options = BuildOptions(maxExtractionFileBytes: 1024);
        var logger = Substitute.For<ILogger<AttachmentExtractionProcessor>>();

        var sut = new AttachmentExtractionProcessor(sessionService, storage, extractor, options, logger);

        await sut.ProcessAsync(attachment, CancellationToken.None);

        await storage.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await sessionService.Received().UpdateAttachmentExtractionAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Failed,
            100,
            Arg.Is<string?>(value => value == null),
            Arg.Is<string>(error => error.Contains("extraction limit", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_Should_MarkFailed_With_Sanitized_Message_When_Extractor_Throws()
    {
        var attachment = BuildAttachment();
        var sessionService = Substitute.For<ISessionService>();
        var storage = Substitute.For<IAttachmentStorageService>();
        var extractor = Substitute.For<IAttachmentTextExtractor>();
        var options = BuildOptions();
        var logger = Substitute.For<ILogger<AttachmentExtractionProcessor>>();

        storage.OpenReadAsync(attachment.BlobName, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test payload")));
        extractor.ExtractAsync(
                Arg.Any<byte[]>(),
                attachment.FileName,
                attachment.ContentType,
                Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(x => throw new InvalidOperationException("sensitive internals"));

        var sut = new AttachmentExtractionProcessor(sessionService, storage, extractor, options, logger);

        await sut.ProcessAsync(attachment, CancellationToken.None);

        await sessionService.Received().UpdateAttachmentExtractionAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Failed,
            100,
            Arg.Is<string?>(value => value == null),
            Arg.Is<string>(error => error == "Extraction failed. Unsupported format or processing error."),
            Arg.Any<CancellationToken>());
    }

    private static SessionAttachment BuildAttachment() =>
        new(
            AttachmentId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            FileName: "doc.md",
            ContentType: "text/markdown",
            SizeBytes: 2048,
            BlobName: "session/blob-doc-md",
            BlobUri: "https://storage.example/session/blob-doc-md",
            AccessUrl: "/sessions/1/attachments/1/content",
            ExtractedText: null,
            UploadedAt: DateTimeOffset.UtcNow,
            ExtractionStatus: AttachmentExtractionStatus.Queued,
            ExtractionProgressPercent: 0,
            ExtractionError: null,
            ExtractionUpdatedAt: DateTimeOffset.UtcNow);

    private static AttachmentExtractionOptions BuildOptions(int maxExtractionFileBytes = 2 * 1024 * 1024) =>
        new()
        {
            MaxExtractionFileBytes = maxExtractionFileBytes
        };
}
