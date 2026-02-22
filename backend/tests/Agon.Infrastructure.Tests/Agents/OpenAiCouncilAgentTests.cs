using System.Net;
using System.Text;
using Agon.Application.Orchestration;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agon.Infrastructure.Tests.Agents;

public class OpenAiCouncilAgentTests
{
    [Fact]
    public async Task RunAsync_ReturnsOutputText_FromResponsesApi()
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
                      "output_text": "Use customer interviews to validate demand."
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var options = new OpenAiCouncilAgentOptions(
            AgentId: "research_librarian",
            ApiKey: "test-key",
            ModelName: "gpt-5.2",
            MaxOutputTokens: 256,
            Temperature: 0.35);
        var sut = new OpenAiCouncilAgent(httpClient, options, NullLogger<OpenAiCouncilAgent>.Instance);

        var response = await sut.RunAsync(CreateContext(), CancellationToken.None);

        response.Message.Should().Be("Use customer interviews to validate demand.");
        response.Patch.Should().BeNull();
        requestBody.Should().Contain("\"temperature\":0.35");
    }

    [Fact]
    public async Task RunAsync_Throws_WhenOpenAiReturnsNonSuccessStatus()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """
                {
                  "error": {
                    "message": "insufficient_quota",
                    "type": "billing_error"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var httpClient = new HttpClient(handler);
        var options = new OpenAiCouncilAgentOptions(
            AgentId: "research_librarian",
            ApiKey: "bad-key",
            ModelName: "gpt-5.2",
            MaxOutputTokens: 256);
        var sut = new OpenAiCouncilAgent(httpClient, options, NullLogger<OpenAiCouncilAgent>.Instance);

        var act = () => sut.RunAsync(CreateContext(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<HttpRequestException>();
        exception.Which.Message.Should().Contain("insufficient_quota");
    }

    private static AgentContext CreateContext()
    {
        var sessionId = Guid.NewGuid();
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Test core idea";

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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
