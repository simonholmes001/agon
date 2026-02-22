using System.Net;
using System.Text;
using Agon.Application.Orchestration;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agon.Infrastructure.Tests.Agents;

public class DeepSeekCouncilAgentTests
{
    [Fact]
    public async Task RunAsync_ReturnsFirstChoiceMessage_WhenRequestSucceeds()
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
                      "choices": [
                        {
                          "message": {
                            "content": "Address technical risk with phased rollout."
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var sut = new DeepSeekCouncilAgent(
            new HttpClient(handler),
            new DeepSeekCouncilAgentOptions("technical_architect", "deepseek-key", "deepseek-chat", 256, 0.4),
            NullLogger<DeepSeekCouncilAgent>.Instance);

        var response = await sut.RunAsync(CreateContext(), CancellationToken.None);

        response.Message.Should().Be("Address technical risk with phased rollout.");
        requestBody.Should().Contain("\"temperature\":0.4");
    }

    [Fact]
    public async Task RunAsync_Throws_OnNonSuccessStatus()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """
                {
                  "error": {
                    "message": "insufficient balance"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var sut = new DeepSeekCouncilAgent(
            new HttpClient(handler),
            new DeepSeekCouncilAgentOptions("technical_architect", "bad", "deepseek-chat", 256),
            NullLogger<DeepSeekCouncilAgent>.Instance);

        var act = () => sut.RunAsync(CreateContext(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<HttpRequestException>();
        exception.Which.Message.Should().Contain("insufficient balance");
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
            Phase = SessionPhase.DebateRound1,
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
