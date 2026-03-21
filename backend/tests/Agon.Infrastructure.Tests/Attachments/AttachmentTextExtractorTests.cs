using Agon.Infrastructure.Attachments;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;
using System.Text;

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

        var bytes = Encoding.UTF8.GetBytes("hello\r\nworld");
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

        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4");
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

    // ── Empty content ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_EmptyContent_ReturnsNull()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());

        var result = await extractor.ExtractAsync([], "notes.txt", "text/plain");

        result.Should().BeNull();
    }

    // ── Text file types ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_MarkdownFile_ReturnsText()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("# Title\n\nSome content");

        var result = await extractor.ExtractAsync(bytes, "readme.md", "text/markdown");

        result.Should().Contain("Title");
    }

    [Fact]
    public async Task ExtractAsync_JsonFile_ReturnsText()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("{\"key\": \"value\"}");

        var result = await extractor.ExtractAsync(bytes, "data.json", "application/json");

        result.Should().Contain("key");
    }

    [Fact]
    public async Task ExtractAsync_CsvFile_ReturnsText()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("header1,header2\nval1,val2");

        var result = await extractor.ExtractAsync(bytes, "data.csv", "text/csv");

        result.Should().Contain("header1");
    }

    [Fact]
    public async Task ExtractAsync_XmlFile_ReturnsText()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("<root><item>value</item></root>");

        var result = await extractor.ExtractAsync(bytes, "data.xml", "application/xml");

        result.Should().Contain("root");
    }

    [Fact]
    public async Task ExtractAsync_YamlFile_ReturnsText()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("name: John\nage: 30");

        var result = await extractor.ExtractAsync(bytes, "config.yaml", "application/x-yaml");

        result.Should().Contain("name");
    }

    [Fact]
    public async Task ExtractAsync_HtmlFile_ReturnsText()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("<html><body>Hello</body></html>");

        var result = await extractor.ExtractAsync(bytes, "page.html", "text/html");

        result.Should().Contain("Hello");
    }

    // ── Extension-based detection ──────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_TextFileByExtension_ReturnsText()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("Log content");

        // No content type matching, but extension .log is text-like
        var result = await extractor.ExtractAsync(bytes, "app.log", "application/octet-stream");

        result.Should().Contain("Log content");
    }

    [Fact]
    public async Task ExtractAsync_CsExtension_ReturnsSourceCode()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("public class Foo { }");

        var result = await extractor.ExtractAsync(bytes, "MyClass.cs", "text/plain");

        result.Should().Contain("class Foo");
    }

    // ── MaxExtractedTextChars truncation ───────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_ExceedsMaxChars_TruncatesText()
    {
        // MaxExtractedTextChars is enforced as Math.Max(1000, setting), so we need >1000 chars to test truncation
        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            MaxExtractedTextChars = 1200
        });

        var longText = new string('A', 2000); // 2000 chars > 1200 limit
        var bytes = Encoding.UTF8.GetBytes(longText);

        var result = await extractor.ExtractAsync(bytes, "notes.txt", "text/plain");

        result.Should().HaveLength(1200);
    }

    [Fact]
    public async Task ExtractAsync_TextWithinMaxChars_IsNotTruncated()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            MaxExtractedTextChars = 1000
        });

        const string text = "Short text";
        var bytes = Encoding.UTF8.GetBytes(text);

        var result = await extractor.ExtractAsync(bytes, "notes.txt", "text/plain");

        result.Should().Be(text);
    }

    // ── Whitespace normalization ───────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_CarriageReturnNewline_NormalizesToNewline()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("line1\r\nline2\r\nline3");

        var result = await extractor.ExtractAsync(bytes, "notes.txt", "text/plain");

        result.Should().Be("line1\nline2\nline3");
    }

    [Fact]
    public async Task ExtractAsync_TextWithControlChars_NormalizesToSpaces()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        // \x01 is a control character that should be replaced with space
        var bytes = Encoding.UTF8.GetBytes("hello\x01world");

        var result = await extractor.ExtractAsync(bytes, "notes.txt", "text/plain");

        result.Should().NotContain("\x01");
    }

    [Fact]
    public async Task ExtractAsync_AllWhitespaceText_ReturnsNull()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions());
        var bytes = Encoding.UTF8.GetBytes("   \n  \r\n  ");

        var result = await extractor.ExtractAsync(bytes, "notes.txt", "text/plain");

        result.Should().BeNull();
    }

    // ── Image detection by content type ───────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_JpegImageContentType_TriesToExtractVision()
    {
        // Image with vision disabled (no API key) → returns null
        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            OpenAiVision = new OpenAiVisionExtractionOptions { Enabled = false }
        });

        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic bytes
        var result = await extractor.ExtractAsync(bytes, "photo.jpg", "image/jpeg");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_PngImageContentType_TriesToExtractVision()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            OpenAiVision = new OpenAiVisionExtractionOptions { Enabled = false }
        });

        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        var result = await extractor.ExtractAsync(bytes, "image.png", "image/png");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ImageByExtension_TriesToExtractVision()
    {
        // Image detected by extension even with generic content type
        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            OpenAiVision = new OpenAiVisionExtractionOptions { Enabled = false }
        });

        var bytes = new byte[] { 1, 2, 3, 4 };
        var result = await extractor.ExtractAsync(bytes, "diagram.png", "application/octet-stream");

        result.Should().BeNull();
    }

    // ── Vision extraction with mock HTTP ──────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_Image_WhenVisionApiReturnsContent_ReturnsExtractedText()
    {
        var openAiResponse = """
            {
                "choices": [
                    {
                        "message": {
                            "content": "Extracted text from image"
                        }
                    }
                ]
            }
            """;

        var handler = new StaticResponseHandler(HttpStatusCode.OK, openAiResponse);
        var extractor = CreateExtractorWithHandler(handler, new AttachmentExtractionOptions
        {
            OpenAiVision = new OpenAiVisionExtractionOptions
            {
                Enabled = true,
                ApiKey = "sk-test-key",
                MaxTokens = 256,
                MaxImageBytes = 10_000_000
            }
        });

        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 }; // PNG bytes
        var result = await extractor.ExtractAsync(bytes, "diagram.png", "image/png");

        result.Should().Be("Extracted text from image");
    }

    [Fact]
    public async Task ExtractAsync_Image_WhenVisionApiReturnsError_ReturnsNull()
    {
        var handler = new StaticResponseHandler(HttpStatusCode.Unauthorized, "{\"error\": \"invalid key\"}");
        var extractor = CreateExtractorWithHandler(handler, new AttachmentExtractionOptions
        {
            OpenAiVision = new OpenAiVisionExtractionOptions
            {
                Enabled = true,
                ApiKey = "invalid-key",
                MaxImageBytes = 10_000_000
            }
        });

        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var result = await extractor.ExtractAsync(bytes, "photo.png", "image/png");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_Image_WhenExceedsMaxImageBytes_SkipsVision()
    {
        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            OpenAiVision = new OpenAiVisionExtractionOptions
            {
                Enabled = true,
                ApiKey = "sk-test-key",
                MaxImageBytes = 100 // Very small limit
            }
        });

        var bytes = new byte[1000]; // 1000 bytes - exceeds limit of 100
        var result = await extractor.ExtractAsync(bytes, "large.png", "image/png");

        result.Should().BeNull();
    }

    // ── Document extraction with mock HTTP ────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_Document_WhenApiReturnsContent_ReturnsExtractedText()
    {
        var docResponse = """
            {
                "status": "succeeded",
                "analyzeResult": {
                    "content": "Document text from intelligence service"
                }
            }
            """;

        // First call (analyze) returns 202 with operation location, second call returns success
        var handler = new TwoStepDocIntelHandler(
            analyzeStatusCode: HttpStatusCode.OK,
            analyzeBody: docResponse);

        var extractor = CreateExtractorWithHandler(handler, new AttachmentExtractionOptions
        {
            DocumentIntelligence = new DocumentIntelligenceExtractionOptions
            {
                Enabled = true,
                Endpoint = "https://my-doc-intelligence.cognitiveservices.azure.com",
                ApiKey = "test-api-key"
            }
        });

        var bytes = Encoding.UTF8.GetBytes("%PDF content");
        var result = await extractor.ExtractAsync(bytes, "doc.pdf", "application/pdf");

        result.Should().Be("Document text from intelligence service");
    }

    [Fact]
    public async Task ExtractAsync_Document_WhenApiReturnsFailure_ReturnsNull()
    {
        var handler = new StaticResponseHandler(HttpStatusCode.BadRequest, "{\"error\": \"bad request\"}");

        var extractor = CreateExtractorWithHandler(handler, new AttachmentExtractionOptions
        {
            DocumentIntelligence = new DocumentIntelligenceExtractionOptions
            {
                Enabled = true,
                Endpoint = "https://my-doc-intelligence.cognitiveservices.azure.com",
                ApiKey = "test-api-key"
            }
        });

        var bytes = Encoding.UTF8.GetBytes("%PDF content");
        var result = await extractor.ExtractAsync(bytes, "doc.pdf", "application/pdf");

        result.Should().BeNull();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AttachmentTextExtractor CreateExtractor(AttachmentExtractionOptions options)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(new StaticResponseHandler(HttpStatusCode.BadRequest, "{}")));
        return new AttachmentTextExtractor(httpClientFactory, options, NullLogger<AttachmentTextExtractor>.Instance);
    }

    private static AttachmentTextExtractor CreateExtractorWithHandler(HttpMessageHandler handler, AttachmentExtractionOptions options)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        return new AttachmentTextExtractor(httpClientFactory, options, NullLogger<AttachmentTextExtractor>.Instance);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StaticResponseHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body)
            });
        }
    }

    /// <summary>
    /// Handles the two-step document intelligence flow:
    /// First call returns the analyze result directly (200 OK).
    /// </summary>
    private sealed class TwoStepDocIntelHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _analyzeStatusCode;
        private readonly string _analyzeBody;

        public TwoStepDocIntelHandler(HttpStatusCode analyzeStatusCode, string analyzeBody)
        {
            _analyzeStatusCode = analyzeStatusCode;
            _analyzeBody = analyzeBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_analyzeStatusCode)
            {
                Content = new StringContent(_analyzeBody)
            });
        }
    }
}

