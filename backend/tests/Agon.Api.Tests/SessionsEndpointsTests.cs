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
    public async Task StartSession_TransitionsToDebateRound1()
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
        started!.Phase.Should().Be("DebateRound1");
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

    private sealed class SessionResponse
    {
        public Guid SessionId { get; init; }
        public string Phase { get; init; } = string.Empty;
    }

    private sealed class TruthMapResponse
    {
        public string CoreIdea { get; init; } = string.Empty;
    }
}
