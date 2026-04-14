using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agon.Integration.Tests;

public class AttachmentExtractionLifecycleIntegrationTests : IClassFixture<AgonWebApplicationFactory>
{
    private readonly AgonWebApplicationFactory _baseFactory;

    public AttachmentExtractionLifecycleIntegrationTests(AgonWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task POST_And_List_Attachments_Should_Expose_Ready_Status_When_Extraction_Succeeds()
    {
        using var factory = CreateFactoryWithAttachmentServices(new StubAttachmentTextExtractor(_ => "parsed-text"));
        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client, "Extraction lifecycle ready");

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        multipart.Add(fileContent, "file", "ready.txt");

        var uploadResponse = await client.PostAsync($"/sessions/{sessionId}/attachments", multipart);

        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var uploadDoc = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync());
        var attachmentId = uploadDoc.RootElement.GetProperty("id").GetGuid();
        uploadDoc.RootElement.GetProperty("extractionStatus").GetString().Should().BeOneOf("uploaded", "extracting");
        uploadDoc.RootElement.GetProperty("extractionFailureReason").ValueKind.Should().Be(JsonValueKind.Null);

        var finalized = await WaitForAttachmentTerminalStateAsync(client, sessionId, attachmentId);
        finalized.ExtractionStatus.Should().Be("ready");
        finalized.HasExtractedText.Should().BeTrue();
        finalized.ExtractionFailureReason.Should().BeNull();
    }

    [Fact]
    public async Task POST_And_List_Attachments_Should_Expose_Failed_Status_When_Extraction_Fails()
    {
        using var factory = CreateFactoryWithAttachmentServices(new StubAttachmentTextExtractor(_ => throw new InvalidOperationException("boom")));
        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client, "Extraction lifecycle failed");

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        multipart.Add(fileContent, "file", "failed.txt");

        var uploadResponse = await client.PostAsync($"/sessions/{sessionId}/attachments", multipart);

        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var uploadDoc = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync());
        var attachmentId = uploadDoc.RootElement.GetProperty("id").GetGuid();
        uploadDoc.RootElement.GetProperty("extractionStatus").GetString().Should().BeOneOf("uploaded", "extracting");
        uploadDoc.RootElement.GetProperty("extractionFailureReason").ValueKind.Should().Be(JsonValueKind.Null);

        var finalized = await WaitForAttachmentTerminalStateAsync(client, sessionId, attachmentId);
        finalized.ExtractionStatus.Should().Be("failed");
        finalized.HasExtractedText.Should().BeFalse();
        finalized.ExtractionFailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task POST_Attachment_Should_Use_Canonical_DocumentParser_And_Persist_Failure_Reason()
    {
        var parser = new StubDocumentParser(_ => new DocumentParseResult(
            ContractVersion: "1.0",
            Route: DocumentParseRoute.Text,
            Success: false,
            Retryable: false,
            IsPartial: false,
            ExtractedText: null,
            ExtractedTextChars: 0,
            ErrorCode: DocumentParseErrorCode.NoExtractableText,
            FailureReason: "No extractable text was produced for this attachment."));
        using var factory = CreateFactoryWithDocumentParser(parser);
        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client, "Canonical parser enforcement");

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        multipart.Add(fileContent, "file", "failure.txt");

        var uploadResponse = await client.PostAsync($"/sessions/{sessionId}/attachments", multipart);

        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var uploadDoc = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync());
        var attachmentId = uploadDoc.RootElement.GetProperty("id").GetGuid();
        uploadDoc.RootElement.GetProperty("extractionStatus").GetString().Should().BeOneOf("uploaded", "extracting");

        var finalized = await WaitForAttachmentTerminalStateAsync(client, sessionId, attachmentId);
        parser.CallCount.Should().Be(1);
        finalized.ExtractionStatus.Should().Be("failed");
        finalized.ExtractionFailureReason
            .Should().Be("No extractable text was produced for this attachment.");
    }


    [Fact]
    public async Task Async_Worker_Should_Requeue_Stale_Extracting_Attachments_And_Complete_Extraction()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AttachmentProcessing:AsyncExtraction:Enabled", "true");
            builder.UseSetting("AttachmentProcessing:AsyncExtraction:BatchSize", "8");
            builder.UseSetting("AttachmentProcessing:AsyncExtraction:PollIntervalMs", "75");
            builder.UseSetting("AttachmentProcessing:AsyncExtraction:RequeueStaleExtractingEnabled", "true");
            builder.UseSetting("AttachmentProcessing:AsyncExtraction:StaleExtractingAfterMinutes", "1");
            builder.UseSetting("AttachmentProcessing:AsyncExtraction:ReconcileIntervalMs", "75");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAttachmentStorageService>();
                services.RemoveAll<IAttachmentTextExtractor>();
                services.AddSingleton<IAttachmentStorageService, InMemoryAttachmentStorageService>();
                services.AddSingleton<IAttachmentTextExtractor>(new StubAttachmentTextExtractor(_ => "recovered text"));
            });
        });

        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client, "stale extracting recovery");

        using (var scope = factory.Services.CreateScope())
        {
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var storage = scope.ServiceProvider.GetRequiredService<IAttachmentStorageService>();
            var state = await sessionService.GetAsync(sessionId, CancellationToken.None);
            state.Should().NotBeNull();

            var attachmentId = Guid.NewGuid();
            var blobName = $"{sessionId:N}/stale-{attachmentId:N}.txt";
            await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("stale attachment payload"), writable: false))
            {
                _ = await storage.UploadAsync(blobName, stream, "text/plain", CancellationToken.None);
            }

            var staleAttachment = new SessionAttachment(
                AttachmentId: attachmentId,
                SessionId: sessionId,
                UserId: state!.UserId,
                FileName: "stale.txt",
                ContentType: "text/plain",
                SizeBytes: 24,
                BlobName: blobName,
                BlobUri: $"https://unit.test/{blobName}",
                AccessUrl: $"/sessions/{sessionId}/attachments/{attachmentId}/content",
                ExtractedText: null,
                UploadedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
                ExtractionStatus: AttachmentExtractionStatus.Extracting,
                ExtractionFailureReason: null);

            _ = await sessionService.SaveAttachmentAsync(staleAttachment, CancellationToken.None);

            var finalized = await WaitForAttachmentTerminalStateAsync(client, sessionId, attachmentId, TimeSpan.FromSeconds(10));
            finalized.ExtractionStatus.Should().Be("ready");
            finalized.HasExtractedText.Should().BeTrue();
            finalized.ExtractionFailureReason.Should().BeNull();
        }
    }
    private static async Task<AttachmentStateSnapshot> WaitForAttachmentTerminalStateAsync(
        HttpClient client,
        Guid sessionId,
        Guid attachmentId,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (DateTimeOffset.UtcNow <= deadline)
        {
            var listResponse = await client.GetAsync($"/sessions/{sessionId}/attachments");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            var attachment = listDoc.RootElement
                .EnumerateArray()
                .Single(a => a.GetProperty("id").GetGuid() == attachmentId);

            var status = attachment.GetProperty("extractionStatus").GetString();
            if (status is "ready" or "failed")
            {
                return new AttachmentStateSnapshot(
                    status,
                    attachment.GetProperty("hasExtractedText").GetBoolean(),
                    attachment.GetProperty("extractionFailureReason").GetString());
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"Attachment {attachmentId} did not reach terminal extraction state before timeout.");
    }

    private WebApplicationFactory<Program> CreateFactoryWithAttachmentServices(IAttachmentTextExtractor extractor)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAttachmentStorageService>();
                services.RemoveAll<IAttachmentTextExtractor>();
                services.AddSingleton<IAttachmentStorageService, InMemoryAttachmentStorageService>();
                services.AddSingleton(extractor);
            });
        });
    }

    private WebApplicationFactory<Program> CreateFactoryWithDocumentParser(IDocumentParser parser)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAttachmentStorageService>();
                services.RemoveAll<IDocumentParser>();
                services.AddSingleton<IAttachmentStorageService, InMemoryAttachmentStorageService>();
                services.AddSingleton(parser);
            });
        });
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client, string idea)
    {
        var response = await client.PostAsJsonAsync("/sessions", new
        {
            idea,
            frictionLevel = 50
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private sealed class InMemoryAttachmentStorageService : IAttachmentStorageService
    {
        private readonly Dictionary<string, byte[]> _contentByBlob = new(StringComparer.Ordinal);

        public async Task<AttachmentUploadResult> UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            _contentByBlob[blobName] = copy.ToArray();
            return new AttachmentUploadResult(blobName, $"https://unit.test/{blobName}", $"/blob/{blobName}");
        }

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default)
        {
            if (!_contentByBlob.TryGetValue(blobName, out var bytes))
            {
                return Task.FromResult<Stream?>(null);
            }

            return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
        }

        public Task DeleteIfExistsAsync(string blobName, CancellationToken cancellationToken = default)
        {
            _contentByBlob.Remove(blobName);
            return Task.CompletedTask;
        }
    }

    private sealed class StubAttachmentTextExtractor : IAttachmentTextExtractor
    {
        private readonly Func<byte[], string?> _extract;

        public StubAttachmentTextExtractor(Func<byte[], string?> extract)
        {
            _extract = extract;
        }

        public Task<string?> ExtractAsync(byte[] content, string fileName, string contentType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_extract(content));
        }
    }

    private sealed class StubDocumentParser : IDocumentParser
    {
        private readonly Func<DocumentParseRequest, DocumentParseResult> _parse;

        public StubDocumentParser(Func<DocumentParseRequest, DocumentParseResult> parse)
        {
            _parse = parse;
        }

        public int CallCount { get; private set; }

        public Task<DocumentParseResult> ParseAsync(DocumentParseRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_parse(request));
        }
    }

    private sealed record AttachmentStateSnapshot(
        string? ExtractionStatus,
        bool HasExtractedText,
        string? ExtractionFailureReason);
}
