using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Agon.Api.Tests;

public class ArtifactEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ArtifactEndpointsTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<Guid> CreateAndStartSessionAsync()
    {
        var create = await _client.PostAsJsonAsync("/sessions", new
        {
            idea = "A service that validates startup ideas with an AI council.",
            mode = "Deep",
            frictionLevel = 50
        });
        var created = await create.Content.ReadFromJsonAsync<SessionResponse>();
        await _client.PostAsync($"/sessions/{created!.SessionId}/start", content: null);
        return created.SessionId;
    }

    #region GET /sessions/{id}/artifacts (list available artifacts)

    [Fact]
    public async Task ListArtifacts_ReturnsAvailableTypes_AfterSessionComplete()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.GetAsync($"/sessions/{sessionId}/artifacts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var artifacts = await response.Content.ReadFromJsonAsync<ArtifactListResponse>();
        artifacts.Should().NotBeNull();
        artifacts!.SessionId.Should().Be(sessionId);
        artifacts.AvailableTypes.Should().NotBeEmpty();
        artifacts.AvailableTypes.Should().Contain("copilot");
        artifacts.AvailableTypes.Should().Contain("architecture");
        artifacts.AvailableTypes.Should().Contain("prd");
        artifacts.AvailableTypes.Should().Contain("risks");
        artifacts.AvailableTypes.Should().Contain("assumptions");
    }

    [Fact]
    public async Task ListArtifacts_ReturnsNotFound_ForNonExistentSession()
    {
        var response = await _client.GetAsync($"/sessions/{Guid.NewGuid()}/artifacts");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /sessions/{id}/artifacts/{type} (get specific artifact)

    [Fact]
    public async Task GetArtifact_ReturnsCopilotInstructions_WhenTypeIsCopilot()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.GetAsync($"/sessions/{sessionId}/artifacts/copilot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var artifact = await response.Content.ReadFromJsonAsync<ArtifactResponse>();
        artifact.Should().NotBeNull();
        artifact!.SessionId.Should().Be(sessionId);
        artifact.Type.Should().Be("copilot");
        artifact.Content.Should().Contain("applyTo:");
        artifact.Content.Should().Contain("---");
    }

    [Fact]
    public async Task GetArtifact_ReturnsArchitectureInstructions_WhenTypeIsArchitecture()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.GetAsync($"/sessions/{sessionId}/artifacts/architecture");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var artifact = await response.Content.ReadFromJsonAsync<ArtifactResponse>();
        artifact.Should().NotBeNull();
        artifact!.Type.Should().Be("architecture");
        artifact.Content.Should().Contain("applyTo:");
    }

    [Fact]
    public async Task GetArtifact_ReturnsPrd_WhenTypeIsPrd()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.GetAsync($"/sessions/{sessionId}/artifacts/prd");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var artifact = await response.Content.ReadFromJsonAsync<ArtifactResponse>();
        artifact.Should().NotBeNull();
        artifact!.Type.Should().Be("prd");
        artifact.Content.Should().Contain("applyTo:");
    }

    [Fact]
    public async Task GetArtifact_ReturnsRiskRegistry_WhenTypeIsRisks()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.GetAsync($"/sessions/{sessionId}/artifacts/risks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var artifact = await response.Content.ReadFromJsonAsync<ArtifactResponse>();
        artifact.Should().NotBeNull();
        artifact!.Type.Should().Be("risks");
        artifact.Content.Should().Contain("applyTo:");
    }

    [Fact]
    public async Task GetArtifact_ReturnsAssumptionValidation_WhenTypeIsAssumptions()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.GetAsync($"/sessions/{sessionId}/artifacts/assumptions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var artifact = await response.Content.ReadFromJsonAsync<ArtifactResponse>();
        artifact.Should().NotBeNull();
        artifact!.Type.Should().Be("assumptions");
        artifact.Content.Should().Contain("applyTo:");
    }

    [Fact]
    public async Task GetArtifact_ReturnsBadRequest_ForInvalidType()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.GetAsync($"/sessions/{sessionId}/artifacts/invalid-type");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetArtifact_ReturnsNotFound_ForNonExistentSession()
    {
        var response = await _client.GetAsync($"/sessions/{Guid.NewGuid()}/artifacts/copilot");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetArtifact_IsCaseInsensitive_ForType()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.GetAsync($"/sessions/{sessionId}/artifacts/COPILOT");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var artifact = await response.Content.ReadFromJsonAsync<ArtifactResponse>();
        artifact!.Type.Should().Be("copilot");
    }

    #endregion

    #region POST /sessions/{id}/artifacts/export (export all as ZIP)

    [Fact]
    public async Task ExportArtifacts_ReturnsZipFile_WithAllArtifacts()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.PostAsync($"/sessions/{sessionId}/artifacts/export", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("artifacts");
        response.Content.Headers.ContentDisposition.FileName.Should().EndWith(".zip");

        var content = await response.Content.ReadAsByteArrayAsync();
        content.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportArtifacts_ReturnsNotFound_ForNonExistentSession()
    {
        var response = await _client.PostAsync($"/sessions/{Guid.NewGuid()}/artifacts/export", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportArtifacts_IncludesCorrelationIdHeader_WhenProvided()
    {
        var sessionId = await CreateAndStartSessionAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/sessions/{sessionId}/artifacts/export");
        request.Headers.Add("X-Correlation-ID", "corr-export-123");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values.Should().Contain("corr-export-123");
    }

    #endregion

    #region Selective Export

    [Fact]
    public async Task ExportArtifacts_ReturnsOnlyRequestedTypes_WhenTypesSpecified()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/artifacts/export",
            new { types = new[] { "copilot", "risks" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
    }

    [Fact]
    public async Task ExportArtifacts_ReturnsBadRequest_WhenInvalidTypeInList()
    {
        var sessionId = await CreateAndStartSessionAsync();

        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/artifacts/export",
            new { types = new[] { "copilot", "invalid-type" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Response DTOs for deserialization

    private sealed class SessionResponse
    {
        public Guid SessionId { get; init; }
        public string Phase { get; init; } = string.Empty;
    }

    private sealed class ArtifactListResponse
    {
        public Guid SessionId { get; init; }
        public IReadOnlyList<string> AvailableTypes { get; init; } = [];
    }

    private sealed class ArtifactResponse
    {
        public Guid SessionId { get; init; }
        public string Type { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public DateTimeOffset GeneratedAtUtc { get; init; }
    }

    #endregion
}
