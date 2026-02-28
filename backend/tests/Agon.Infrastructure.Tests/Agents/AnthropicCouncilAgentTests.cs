using System.Net;
using System.Text;
using Agon.Application.Orchestration;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agon.Infrastructure.Tests.Agents;

public class AnthropicCouncilAgentTests
{
    [Fact]
    public async Task RunAsync_ReturnsTextContent_WhenRequestSucceeds()
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
                      "content": [
                        { "type": "text", "text": "Define success metrics before building." }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var sut = new AnthropicCouncilAgent(
            new HttpClient(handler),
            new AnthropicCouncilAgentOptions("gpt_agent", "anthropic-key", "claude-opus-4-6", 256, 0.25),
            NullLogger<AnthropicCouncilAgent>.Instance);

        var response = await sut.RunAsync(CreateContext(), CancellationToken.None);

        response.Message.Should().Be("Define success metrics before building.");
        requestBody.Should().Contain("\"temperature\":0.25");
    }

    [Fact]
    public async Task RunAsync_ReturnsOnlyMessageSection_WhenContentContainsPatchSection()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "content": [
                    { "type": "text", "text": "## MESSAGE\nUser-visible answer.\n\n## PATCH\n{\"ops\":[]}" }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var sut = new AnthropicCouncilAgent(
            new HttpClient(handler),
            new AnthropicCouncilAgentOptions("gpt_agent", "anthropic-key", "claude-opus-4-6", 256),
            NullLogger<AnthropicCouncilAgent>.Instance);

        var response = await sut.RunAsync(CreateContext(), CancellationToken.None);

        response.Message.Should().Be("User-visible answer.");
        response.Patch.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_Throws_OnNonSuccessStatus()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """
                {
                  "error": {
                    "type": "invalid_request_error",
                    "message": "credit balance is too low"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var sut = new AnthropicCouncilAgent(
            new HttpClient(handler),
            new AnthropicCouncilAgentOptions("gpt_agent", "bad", "claude-opus-4-6", 256),
            NullLogger<AnthropicCouncilAgent>.Instance);

        var act = () => sut.RunAsync(CreateContext(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<HttpRequestException>();
        exception.Which.Message.Should().Contain("credit balance is too low");
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
