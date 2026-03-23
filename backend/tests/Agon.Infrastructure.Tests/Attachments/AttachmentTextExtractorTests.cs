using Agon.Infrastructure.Attachments;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;
using System.Text;
using System.Text.Json;

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

    [Fact]
    public async Task ExtractAsync_ImageVisionPrimaryModelFails_UsesFallbackModel()
    {
        var handler = new SequenceHandler([
            // Primary model fails.
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"message\":\"model not found\"}}", Encoding.UTF8, "application/json")
            },
            // Fallback model succeeds.
            request =>
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(body);
                var model = doc.RootElement.GetProperty("model").GetString();
                model.Should().Be("gpt-5.2");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "Invoice total: $124.00"
                          }
                        }
                      ]
                    }
                    """, Encoding.UTF8, "application/json")
                };
            }
        ]);

        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            OpenAiVision = new OpenAiVisionExtractionOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                Model = "gpt-4o-mini",
                FallbackModel = "gpt-5.2"
            }
        }, handler);

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var result = await extractor.ExtractAsync(bytes, "diagram.png", "image/png");

        result.Should().Contain("Invoice total");
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ExtractAsync_ImageWithoutVisionKey_FallsBackToDocumentIntelligence()
    {
        var handler = new SequenceHandler([
            // Start analyze call.
            _ => new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Headers =
                {
                    Location = new Uri("https://example.cognitiveservices.azure.com/ops/123")
                },
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            },
            // Poll call.
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "status": "succeeded",
                  "analyzeResult": {
                    "content": "Detected chart title: Q1 Revenue"
                  }
                }
                """, Encoding.UTF8, "application/json")
            }
        ]);

        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            DocumentIntelligence = new DocumentIntelligenceExtractionOptions
            {
                Enabled = true,
                Endpoint = "https://example.cognitiveservices.azure.com",
                UseManagedIdentity = false,
                ApiKey = "test-doc-key",
                PollIntervalMs = 1,
                MaxPollAttempts = 2
            },
            OpenAiVision = new OpenAiVisionExtractionOptions
            {
                Enabled = true,
                ApiKey = ""
            }
        }, handler);

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var result = await extractor.ExtractAsync(bytes, "diagram.png", "image/png");

        result.Should().Contain("Q1 Revenue");
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ExtractAsync_ImageVisionWithObjectContent_ParsesText()
    {
        var handler = new SequenceHandler([
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": {
                          "type": "output_text",
                          "text": "Screenshot shows a blue dashboard with status cards."
                        }
                      }
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            }
        ]);

        var extractor = CreateExtractor(new AttachmentExtractionOptions
        {
            OpenAiVision = new OpenAiVisionExtractionOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                Model = "gpt-4o-mini"
            }
        }, handler);

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var result = await extractor.ExtractAsync(bytes, "snapshot.jpeg", "image/jpeg");

        result.Should().Contain("blue dashboard");
        handler.CallCount.Should().Be(1);
    }

    private static AttachmentTextExtractor CreateExtractor(AttachmentExtractionOptions options, HttpMessageHandler? handler = null)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler ?? new DummyHandler()));
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

    private sealed class SequenceHandler(IReadOnlyList<Func<HttpRequestMessage, HttpResponseMessage>> responses) : HttpMessageHandler
    {
        private int _index;

        public int CallCount => _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_index >= responses.Count)
            {
                throw new InvalidOperationException($"Unexpected request #{_index + 1} ({request.Method} {request.RequestUri}).");
            }

            var response = responses[_index](request);
            _index++;
            return Task.FromResult(response);
        }
    }
}
