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
        sessionService.UpdateAttachmentExtractionStateAsync(
                Arg.Any<Guid>(),
                Arg.Any<AttachmentExtractionStatus>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(attachment);

        var sut = new AttachmentExtractionProcessor(sessionService, storage, extractor, options, logger);

        // Act
        await sut.ProcessAsync(attachment, CancellationToken.None);

        // Assert
        await sessionService.Received().UpdateAttachmentExtractionStateAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Extracting,
            null,
            null,
            Arg.Any<CancellationToken>());

        await sessionService.Received().UpdateAttachmentExtractionStateAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Ready,
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
        sessionService.UpdateAttachmentExtractionStateAsync(
                Arg.Any<Guid>(),
                Arg.Any<AttachmentExtractionStatus>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(attachment);

        var sut = new AttachmentExtractionProcessor(sessionService, storage, extractor, options, logger);

        // Act
        await sut.ProcessAsync(attachment, CancellationToken.None);

        // Assert
        await sessionService.Received().UpdateAttachmentExtractionStateAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Failed,
            Arg.Is<string?>(v => v == null),
            Arg.Is<string?>(error => !string.IsNullOrWhiteSpace(error)),
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
        sessionService.UpdateAttachmentExtractionStateAsync(
                Arg.Any<Guid>(),
                Arg.Any<AttachmentExtractionStatus>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(attachment);

        var sut = new AttachmentExtractionProcessor(sessionService, storage, extractor, options, logger);

        // Act
        await sut.ProcessAsync(attachment, CancellationToken.None);

        // Assert
        await sessionService.Received().UpdateAttachmentExtractionStateAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Failed,
            Arg.Is<string?>(v => v == null),
            Arg.Is<string?>(error => error != null && error.Contains("Extraction failed", StringComparison.Ordinal)),
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
        sessionService.UpdateAttachmentExtractionStateAsync(
                Arg.Any<Guid>(),
                Arg.Any<AttachmentExtractionStatus>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(attachment);

        var sut = new AttachmentExtractionProcessor(sessionService, storage, extractor, options, logger);

        await sut.ProcessAsync(attachment, CancellationToken.None);

        await sessionService.Received().UpdateAttachmentExtractionStateAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Failed,
            Arg.Is<string?>(v => v == null),
            Arg.Is<string?>(error => error == "Extraction failed. Unsupported format or processing error."),
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
            ExtractionStatus: AttachmentExtractionStatus.Uploaded);

    private static AttachmentExtractionOptions BuildOptions(int maxExtractedTextChars = 200000) =>
        new()
        {
            MaxExtractedTextChars = maxExtractedTextChars
        };
}
