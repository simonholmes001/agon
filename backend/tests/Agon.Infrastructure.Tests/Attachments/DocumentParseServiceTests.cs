using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Infrastructure.Attachments;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agon.Infrastructure.Tests.Attachments;

public class DocumentParseServiceTests
{
    [Fact]
    public async Task ParseAsync_UnsupportedFormat_ReturnsUnsupportedFailure()
    {
        var parser = CreateParser(new StubExtractor(_ => "ignored"));

        var result = await parser.ParseAsync(new DocumentParseRequest(
            Content: [1, 2, 3],
            FileName: "archive.zip",
            ContentType: "application/zip",
            SizeBytes: 3));

        result.Success.Should().BeFalse();
        result.Route.Should().Be(DocumentParseRoute.Unsupported);
        result.ErrorCode.Should().Be(DocumentParseErrorCode.UnsupportedFormat);
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_Oversize_ReturnsOversizeFailure()
    {
        var parser = CreateParser(new StubExtractor(_ => "ignored"));

        var result = await parser.ParseAsync(new DocumentParseRequest(
            Content: [1, 2, 3],
            FileName: "notes.txt",
            ContentType: "text/plain",
            SizeBytes: 4096,
            MaxAllowedBytes: 1024));

        result.Success.Should().BeFalse();
        result.Route.Should().Be(DocumentParseRoute.Text);
        result.ErrorCode.Should().Be(DocumentParseErrorCode.Oversize);
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_NoExtractedText_ReturnsDeterministicFailure()
    {
        var parser = CreateParser(new StubExtractor(_ => "   "));

        var result = await parser.ParseAsync(new DocumentParseRequest(
            Content: [1, 2, 3],
            FileName: "paper.pdf",
            ContentType: "application/pdf",
            SizeBytes: 3));

        result.Success.Should().BeFalse();
        result.Route.Should().Be(DocumentParseRoute.Document);
        result.ErrorCode.Should().Be(DocumentParseErrorCode.NoExtractableText);
        result.FailureReason.Should().Be("No extractable text was produced for this attachment.");
    }

    [Fact]
    public async Task ParseAsync_HttpRequestException_ReturnsRetryableTransientFailure()
    {
        var parser = CreateParser(new StubExtractor(_ => throw new HttpRequestException("backend unavailable")));

        var result = await parser.ParseAsync(new DocumentParseRequest(
            Content: [1, 2, 3],
            FileName: "paper.pdf",
            ContentType: "application/pdf",
            SizeBytes: 3));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DocumentParseErrorCode.TransientBackendFailure);
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_TaskCanceledException_ReturnsRetryableTimeoutFailure()
    {
        var parser = CreateParser(new StubExtractor(_ => throw new TaskCanceledException("timeout")));

        var result = await parser.ParseAsync(new DocumentParseRequest(
            Content: [1, 2, 3],
            FileName: "paper.pdf",
            ContentType: "application/pdf",
            SizeBytes: 3));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DocumentParseErrorCode.Timeout);
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_Success_ReturnsVersionedContractPayload()
    {
        var parser = CreateParser(new StubExtractor(_ => "  parsed content  "));

        var result = await parser.ParseAsync(new DocumentParseRequest(
            Content: [1, 2, 3],
            FileName: "paper.pdf",
            ContentType: "application/pdf",
            SizeBytes: 3));

        result.Success.Should().BeTrue();
        result.ContractVersion.Should().Be("1.0");
        result.Route.Should().Be(DocumentParseRoute.Document);
        result.ExtractedText.Should().Be("parsed content");
        result.ExtractedTextChars.Should().Be("parsed content".Length);
        result.ErrorCode.Should().BeNull();
    }

    private static DocumentParseService CreateParser(IAttachmentTextExtractor extractor)
    {
        return new DocumentParseService(
            extractor,
            NullLogger<DocumentParseService>.Instance);
    }

    private sealed class StubExtractor : IAttachmentTextExtractor
    {
        private readonly Func<byte[], string?> _extract;

        public StubExtractor(Func<byte[], string?> extract)
        {
            _extract = extract;
        }

        public Task<string?> ExtractAsync(byte[] content, string fileName, string contentType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_extract(content));
        }
    }
}
