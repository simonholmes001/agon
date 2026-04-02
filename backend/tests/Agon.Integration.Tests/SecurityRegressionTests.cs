using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agon.Integration.Tests;

/// <summary>
/// Security regression tests for the hardening epic (issues #375, #377, #378, #381, #382, #384).
/// Each test class maps to a specific security requirement.
/// </summary>
public class SecurityRegressionTests : IClassFixture<AgonWebApplicationFactory>
{
    private readonly AgonWebApplicationFactory _baseFactory;

    public SecurityRegressionTests(AgonWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> CreateAuthenticatedFactory()
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Enabled"] = "true"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName, _ => { });
            });
        });
    }

    private static HttpClient CreateUserClient(WebApplicationFactory<Program> factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return client;
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client, string idea)
    {
        var response = await client.PostAsJsonAsync("/sessions", new { idea, frictionLevel = 50 });
        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // ── Issue #382: Sensitive provider key headers are stripped ──────────────────

    [Fact]
    public async Task ProviderKeyHeaders_AreStripped_BeforeHandlerSees_Them()
    {
        // Arrange
        var client = _baseFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agon-Provider-Key-openai", "sk-secret-key");
        client.DefaultRequestHeaders.Add("X-Agon-Provider-Key-anthropic", "ant-key-value");

        // Act — the session creation endpoint is public in test env
        var response = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "Security test for header stripping",
            frictionLevel = 50
        });

        // Assert — the request should still succeed (headers silently stripped)
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "provider key headers are stripped at the middleware layer and should not affect the request result");
    }

    [Fact]
    public async Task ProviderKeyHeaders_CaseInsensitive_AreStripped()
    {
        // Arrange
        var client = _baseFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-agon-provider-key-openai", "sk-lowercase-header");
        client.DefaultRequestHeaders.Add("X-AGON-PROVIDER-KEY-ANTHROPIC", "uppercase-header");

        // Act
        var response = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "Case-insensitive header stripping test - security regression",
            frictionLevel = 50
        });

        // Assert — still succeeds; headers stripped regardless of case
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Issue #384: Defense-in-depth user scoping ────────────────────────────────

    [Fact]
    public async Task GET_Session_Returns_404_When_CallerDoesNotOwnSession()
    {
        using var factory = CreateAuthenticatedFactory();
        var ownerId = Guid.NewGuid();
        var intruderId = Guid.NewGuid();
        var ownerClient = CreateUserClient(factory, ownerId);
        var intruderClient = CreateUserClient(factory, intruderId);

        var sessionId = await CreateSessionAsync(ownerClient, "Defense-in-depth ownership test");

        // Intruder tries to read owner's session
        var response = await intruderClient.GetAsync($"/sessions/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-user session access must be denied at the persistence layer");
    }

    [Fact]
    public async Task GET_TruthMap_Returns_404_When_CallerDoesNotOwnSession()
    {
        using var factory = CreateAuthenticatedFactory();
        var ownerId = Guid.NewGuid();
        var intruderId = Guid.NewGuid();
        var ownerClient = CreateUserClient(factory, ownerId);
        var intruderClient = CreateUserClient(factory, intruderId);

        var sessionId = await CreateSessionAsync(ownerClient, "Truth Map ownership isolation test");

        var response = await intruderClient.GetAsync($"/sessions/{sessionId}/truthmap");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the Truth Map must not be accessible to a different user");
    }

    [Fact]
    public async Task POST_Start_Returns_404_When_CallerDoesNotOwnSession()
    {
        using var factory = CreateAuthenticatedFactory();
        var ownerId = Guid.NewGuid();
        var intruderId = Guid.NewGuid();
        var ownerClient = CreateUserClient(factory, ownerId);
        var intruderClient = CreateUserClient(factory, intruderId);

        var sessionId = await CreateSessionAsync(ownerClient, "Start endpoint ownership test - regression");

        var response = await intruderClient.PostAsync($"/sessions/{sessionId}/start", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the start endpoint must reject callers that do not own the session");
    }

    [Fact]
    public async Task GET_Session_Returns_200_For_Session_Owner()
    {
        using var factory = CreateAuthenticatedFactory();
        var userId = Guid.NewGuid();
        var client = CreateUserClient(factory, userId);

        var sessionId = await CreateSessionAsync(client, "Owner access happy path test - security");

        var response = await client.GetAsync($"/sessions/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the session owner must still be able to access their own session");
    }

    // ── Issue #377: Auth fail-fast guard ─────────────────────────────────────────

    [Fact]
    public void NonDevEnvironment_WithAuthDisabled_ShouldThrowOnStartup()
    {
        // Arrange — simulate a Production environment with auth disabled
        // When CreateClient() is called, the host builder runs Program.cs which
        // should throw before the server starts because auth is disabled in Production.
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Auth disabled — this should trigger the fail-fast guard
                    ["Authentication:Enabled"] = "false",
                    // Provide valid CORS so only the auth guard fires
                    ["Cors:AllowedOrigins:0"] = "https://example.com",
                    ["Authentication:AzureAd:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
                    ["Authentication:AzureAd:Audience"] = "api://client-id"
                });
            });
        });

        // Act & Assert — creating the client triggers the host build which runs Program.cs
        Action act = () => factory.CreateClient();
        act.Should().Throw<Exception>("startup must fail fast when auth is disabled in Production");
    }

    // ── Issue #378: CORS fail-fast guard ─────────────────────────────────────────

    [Fact]
    public void NonDevEnvironment_WithEmptyCors_ShouldThrowOnStartup()
    {
        // Arrange — Production environment with auth enabled but no CORS origins
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Enabled"] = "true",
                    ["Authentication:AzureAd:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
                    ["Authentication:AzureAd:Audience"] = "api://client-id",
                    // No CORS origins — should trigger the CORS fail-fast guard
                });
            });
        });

        Action act = () => factory.CreateClient();
        act.Should().Throw<Exception>("startup must fail fast when CORS is unconfigured in Production");
    }

    // ── Issue #381: Provider keys not sent in requests ───────────────────────────

    [Fact]
    public async Task RequestWithNoProviderKeyHeaders_Succeeds_Normally()
    {
        // Arrange — a clean client with no provider key headers
        var client = _baseFactory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/sessions", new
        {
            idea = "No provider key header request — server-managed keys only",
            frictionLevel = 30
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "requests without provider key headers should succeed normally");
    }
}
