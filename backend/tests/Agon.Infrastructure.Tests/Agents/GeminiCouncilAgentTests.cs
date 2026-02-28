using System.Net;
using System.Text;
using Agon.Application.Orchestration;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agon.Infrastructure.Tests.Agents;

public class GeminiCouncilAgentTests
{
    [Fact]
    public async Task RunAsync_ReturnsCandidateText_WhenRequestSucceeds()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [
                              { "text": "Challenge the framing before committing." }
                            ]
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var sut = new GeminiCouncilAgent(
            new HttpClient(handler),
            new GeminiCouncilAgentOptions("gemini_agent", "gemini-key", "gemini-3.1-pro-preview", 256, 0.45),
            NullLogger<GeminiCouncilAgent>.Instance);

        var response = await sut.RunAsync(CreateContext(), CancellationToken.None);

        response.Message.Should().Be("Challenge the framing before committing.");
        requestBody.Should().Contain("\"temperature\":0.45");
    }

    [Fact]
    public async Task RunAsync_ReturnsOnlyMessageSection_WhenCandidateContainsPatchSection()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "## MESSAGE\nKeep this.\n\n## PATCH\n{\"decisions\":[{\"id\":\"d1\"}]}" }
                        ]
                      }
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var sut = new GeminiCouncilAgent(
            new HttpClient(handler),
            new GeminiCouncilAgentOptions("gemini_agent", "gemini-key", "gemini-3.1-pro-preview", 256),
            NullLogger<GeminiCouncilAgent>.Instance);

        var response = await sut.RunAsync(CreateContext(), CancellationToken.None);

        response.Message.Should().Be("Keep this.");
        response.Patch.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_Throws_OnNonSuccessStatus()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """
                {
                  "error": {
                    "message": "quota exceeded"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var sut = new GeminiCouncilAgent(
            new HttpClient(handler),
            new GeminiCouncilAgentOptions("gemini_agent", "bad", "gemini-3.1-pro-preview", 256),
            NullLogger<GeminiCouncilAgent>.Instance);

        var act = () => sut.RunAsync(CreateContext(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<HttpRequestException>();
        exception.Which.Message.Should().Contain("quota exceeded");
    }

    private static AgentContext CreateContext()
    {
        var sessionId = Guid.NewGuid();
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Idea";
        return new AgentContext
        {
            SessionId = sessionId,
            Round = 1,
            Phase = SessionPhase.DraftRound1,
            FrictionLevel = 50,
            TruthMap = map
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
