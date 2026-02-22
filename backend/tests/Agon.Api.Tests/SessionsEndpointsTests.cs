using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Agon.Api.Tests;

public class SessionsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public SessionsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateSession_ReturnsCreatedSession()
    {
        var response = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "A service that validates startup ideas with an AI council.",
            mode = "Deep",
            frictionLevel = 50
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var payload = await response.Content.ReadFromJsonAsync<SessionResponse>();
        payload.Should().NotBeNull();
        payload!.Phase.Should().Be("Clarification");
    }

    [Fact]
    public async Task StartSession_TransitionsToPostDelivery()
    {
        var create = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "A service that validates startup ideas with an AI council.",
            mode = "Deep",
            frictionLevel = 50
        });
        var created = await create.Content.ReadFromJsonAsync<SessionResponse>();

        var startResponse = await client.PostAsync($"/sessions/{created!.SessionId}/start", content: null);

        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await startResponse.Content.ReadFromJsonAsync<SessionResponse>();
        started.Should().NotBeNull();
        started!.Phase.Should().Be("PostDelivery");
    }

    [Fact]
    public async Task StartSession_ReturnsCorrelationIdHeader_WhenProvidedByClient()
    {
        var create = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "A service that validates startup ideas with an AI council.",
            mode = "Deep",
            frictionLevel = 50
        });
        var created = await create.Content.ReadFromJsonAsync<SessionResponse>();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/sessions/{created!.SessionId}/start");
        request.Headers.Add("X-Correlation-ID", "corr-api-123");

        var startResponse = await client.SendAsync(request);

        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        startResponse.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values.Should().Contain("corr-api-123");
    }

    [Fact]
    public async Task GetTruthMap_ReturnsCurrentMapForSession()
    {
        var create = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "A service that validates startup ideas with an AI council.",
            mode = "Deep",
            frictionLevel = 50
        });
        var created = await create.Content.ReadFromJsonAsync<SessionResponse>();

        var response = await client.GetAsync($"/sessions/{created!.SessionId}/truthmap");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var map = await response.Content.ReadFromJsonAsync<TruthMapResponse>();
        map.Should().NotBeNull();
        map!.CoreIdea.Should().Contain("validates startup ideas");
    }

    [Fact]
    public async Task SignalRHub_NegotiateEndpoint_IsExposed()
    {
        var response = await client.PostAsync("/hubs/debate/negotiate?negotiateVersion=1", new StringContent(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SignalRHub_NegotiateEndpoint_AllowsLocalFrontendOrigin()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/hubs/debate/negotiate?negotiateVersion=1")
        {
            Content = new StringContent(string.Empty)
        };
        request.Headers.Add("Origin", "http://localhost:3000");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values).Should().BeTrue();
        values.Should().Contain("http://localhost:3000");
    }

    [Fact]
    public async Task GetTranscript_ReturnsSystemKickoffAndAgentOutcomeMessages_AfterSessionStart()
    {
        var create = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "A service that validates startup ideas with an AI council.",
            mode = "Deep",
            frictionLevel = 50
        });
        var created = await create.Content.ReadFromJsonAsync<SessionResponse>();
        await client.PostAsync($"/sessions/{created!.SessionId}/start", content: null);

        var response = await client.GetAsync($"/sessions/{created.SessionId}/transcript");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var transcript = await response.Content.ReadFromJsonAsync<List<TranscriptMessageResponse>>();
        transcript.Should().NotBeNull();
        transcript!.Should().NotBeEmpty();
        transcript.Should().Contain(message =>
            message.Type == "system"
            && message.Content.Contains("Round 1"));
        transcript.Should().Contain(message =>
            (message.Type == "agent" && message.AgentId == "product-strategist")
            || (message.Type == "system" && message.Content.Contains("product-strategist")));
        transcript.Should().Contain(message =>
            message.Type == "agent"
            && message.AgentId == "synthesis-validation");
    }

    [Fact]
    public async Task PostMessage_ReturnsModeratorReply_InClarification()
    {
        var create = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "A service that validates startup ideas with an AI council.",
            mode = "Deep",
            frictionLevel = 50
        });
        var created = await create.Content.ReadFromJsonAsync<SessionResponse>();

        var response = await client.PostAsJsonAsync($"/sessions/{created!.SessionId}/messages", new
        {
            message = "My target user is startup founders."
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<MessageResponse>();
        payload.Should().NotBeNull();
        payload!.SessionId.Should().Be(created.SessionId);
        payload.Phase.Should().Be("Clarification");
        payload.RoutedAgentId.Should().Be("socratic_clarifier");
        payload.Reply.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostMessage_AfterStart_RoutesInPostDelivery()
    {
        var create = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "A service that validates startup ideas with an AI council.",
            mode = "Deep",
            frictionLevel = 50
        });
        var created = await create.Content.ReadFromJsonAsync<SessionResponse>();
        await client.PostAsync($"/sessions/{created!.SessionId}/start", content: null);

        var response = await client.PostAsJsonAsync($"/sessions/{created.SessionId}/messages", new
        {
            message = "Can you explain the technical architecture trade-offs?"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<MessageResponse>();
        payload.Should().NotBeNull();
        payload!.Phase.Should().Be("PostDelivery");
        payload.RoutedAgentId.Should().Be("technical_architect");
    }

    [Fact]
    public async Task PostMessage_ReturnsBadRequest_ForBlankMessage()
    {
        var create = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "A service that validates startup ideas with an AI council.",
            mode = "Deep",
            frictionLevel = 50
        });
        var created = await create.Content.ReadFromJsonAsync<SessionResponse>();

        var response = await client.PostAsJsonAsync($"/sessions/{created!.SessionId}/messages", new
        {
            message = "   "
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMessage_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        var response = await client.PostAsJsonAsync($"/sessions/{Guid.NewGuid()}/messages", new
        {
            message = "Can we review assumptions?"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class SessionResponse
    {
        public Guid SessionId { get; init; }
        public string Phase { get; init; } = string.Empty;
    }

    private sealed class TruthMapResponse
    {
        public string CoreIdea { get; init; } = string.Empty;
    }

    private sealed class TranscriptMessageResponse
    {
        public string Type { get; init; } = string.Empty;
        public string AgentId { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
    }

    private sealed class MessageResponse
    {
        public Guid SessionId { get; init; }
        public string Phase { get; init; } = string.Empty;
        public string RoutedAgentId { get; init; } = string.Empty;
        public string Reply { get; init; } = string.Empty;
    }
}
