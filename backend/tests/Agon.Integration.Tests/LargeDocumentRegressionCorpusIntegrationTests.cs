using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Agon.Application.Interfaces;
using Agon.Application.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agon.Integration.Tests;

public sealed class LargeDocumentRegressionCorpusIntegrationTests : IClassFixture<AgonWebApplicationFactory>
{
    private readonly AgonWebApplicationFactory _baseFactory;

    public LargeDocumentRegressionCorpusIntegrationTests(AgonWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task LargeDocumentCorpus_Should_Process_Deterministically_Across_FormatFamilies()
    {
        var parser = new CorpusDocumentParser();
        using var factory = CreateFactoryWithDocumentParser(parser);
        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client, "Large-document corpus regression");
        var corpusEntries = LoadCorpusEntries();

        foreach (var entry in corpusEntries)
        {
            using var multipart = new MultipartFormDataContent();
            var payload = Encoding.UTF8.GetBytes(BuildPayload(entry.Template, entry.Repeat));
            payload.Length.Should().BeGreaterThan(14000, "corpus payloads should exercise large-document processing paths");

            var fileContent = new ByteArrayContent(payload);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(entry.ContentType);
            multipart.Add(fileContent, "file", entry.FileName);

            var uploadResponse = await client.PostAsync($"/sessions/{sessionId}/attachments", multipart);
            uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            using var uploadDoc = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync());
            var attachmentId = uploadDoc.RootElement.GetProperty("id").GetGuid();
            uploadDoc.RootElement.GetProperty("extractionStatus").GetString().Should().BeOneOf("uploaded", "extracting");

            var terminal = await WaitForTerminalStateAsync(client, sessionId, attachmentId);
            terminal.Status.Should().Be("ready");
            terminal.HasExtractedText.Should().BeTrue();
            terminal.FailureReason.Should().BeNull();
        }

        parser.CallCount.Should().Be(corpusEntries.Count);
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
            frictionLevel = 55
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<TerminalState> WaitForTerminalStateAsync(
        HttpClient client,
        Guid sessionId,
        Guid attachmentId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
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
                return new TerminalState(
                    status!,
                    attachment.GetProperty("hasExtractedText").GetBoolean(),
                    attachment.GetProperty("extractionFailureReason").GetString());
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Attachment {attachmentId} did not reach terminal extraction state in time.");
    }

    private static IReadOnlyList<CorpusEntry> LoadCorpusEntries()
    {
        var root = Path.GetDirectoryName(typeof(LargeDocumentRegressionCorpusIntegrationTests).Assembly.Location)
            ?? throw new InvalidOperationException("Cannot resolve integration test assembly location.");
        var fixturePath = Path.Combine(root, "Fixtures", "large-document-corpus.json");
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException("Large-document corpus fixture file is missing.", fixturePath);
        }

        var json = File.ReadAllText(fixturePath);
        var entries = JsonSerializer.Deserialize<List<CorpusEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (entries is null || entries.Count == 0)
        {
            throw new InvalidOperationException("Large-document corpus fixture is empty.");
        }

        return entries;
    }

    private static string BuildPayload(string template, int repeat)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < Math.Max(1, repeat); i++)
        {
            builder.Append(template);
            builder.Append("Iteration=");
            builder.Append(i + 1);
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private sealed record CorpusEntry(
        string FileName,
        string ContentType,
        string Template,
        int Repeat);

    private sealed record TerminalState(string Status, bool HasExtractedText, string? FailureReason);

    private sealed class CorpusDocumentParser : IDocumentParser
    {
        private int _callCount;
        public int CallCount => _callCount;

        public Task<DocumentParseResult> ParseAsync(DocumentParseRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            var extracted = Encoding.UTF8.GetString(request.Content).Trim();
            var sections =
                new List<DocumentParseSectionBoundary>
                {
                    new("full_document", 0, extracted.Length)
                };
            var chunkHints =
                new List<DocumentParseChunkHint>
                {
                    new(0, extracted.Length, Math.Max(1, (int)Math.Ceiling(extracted.Length / 4d)), "full_document")
                };

            return Task.FromResult(new DocumentParseResult(
                ContractVersion: "1.1",
                Route: DocumentParseRoute.Document,
                Success: true,
                Retryable: false,
                IsPartial: false,
                ExtractedText: extracted,
                ExtractedTextChars: extracted.Length,
                ErrorCode: null,
                FailureReason: null,
                StructureMetadata: new DocumentParseStructureMetadata(
                    EstimatedTokenCount: Math.Max(1, (int)Math.Ceiling(extracted.Length / 4d)),
                    HeadingCount: 0,
                    SectionCount: 1,
                    Sections: sections,
                    ChunkHints: chunkHints)));
        }
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
}
