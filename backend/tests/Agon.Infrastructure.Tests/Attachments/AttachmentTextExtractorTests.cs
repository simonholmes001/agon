using Agon.Infrastructure.Attachments;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Agon.Infrastructure.Tests.Attachments;

public class AttachmentTextExtractorTests
{
    [Fact]
    public async Task ExtractAsync_TextFile_ReturnsNormalizedText()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            MaxExtractedTextChars = 50
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes("hello\r\nworld");
        var result = await extractor.ExtractAsync(bytes, "notes.txt", "text/plain");

        result.Should().Be("hello\nworld");
    }

    [Fact]
    public async Task ExtractAsync_DocumentWithoutEndpoint_ReturnsNull()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            DocumentIntelligence = new DocumentIntelligenceExtractionOptions
            {
                Enabled = true,
                Endpoint = ""
            }
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4");
        var result = await extractor.ExtractAsync(bytes, "spec.pdf", "application/pdf");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ImageWithoutOpenAiKey_ReturnsNull()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            OpenAiVision = new OpenAiVisionExtractionOptions
            {
                Enabled = true,
                ApiKey = ""
            }
        });

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var result = await extractor.ExtractAsync(bytes, "diagram.png", "image/png");

        result.Should().BeNull();
    }

    private static AttachmentTextExtractor CreateExtractor(AttachmentExtractionOptions options)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(new DummyHandler()));
        return new AttachmentTextExtractor(httpClientFactory, options, NullLogger<AttachmentTextExtractor>.Instance);
    }

    private sealed class DummyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
