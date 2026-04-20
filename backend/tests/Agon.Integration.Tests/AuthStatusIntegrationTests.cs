using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agon.Integration.Tests;

/// <summary>
/// Integration tests for the GET /auth/status endpoint.
///
/// This endpoint is always anonymous and tells clients whether authentication
/// is required by this backend deployment.  The CLI uses it to decide whether
/// to prompt the user for a bearer token before making any API calls.
/// </summary>
public class AuthStatusIntegrationTests : IClassFixture<AgonWebApplicationFactory>
{
    private const string RequiredVersion = "999.0.0";
    private readonly HttpClient _client;
    private readonly AgonWebApplicationFactory _factory;

    public AuthStatusIntegrationTests(AgonWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_Auth_Status_Returns_200_Without_Credentials()
    {
        var response = await _client.GetAsync("/auth/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_Auth_Status_Returns_Required_False_When_Auth_Disabled()
    {
        // The AgonWebApplicationFactory does not set Authentication:Enabled,
        // so the default value of false applies.
        var response = await _client.GetAsync("/auth/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.GetProperty("required").GetBoolean().Should().BeFalse(
            "authentication is disabled by default in the test environment");
    }

    [Fact]
    public async Task GET_Auth_Status_Returns_Scheme_Field()
    {
        var response = await _client.GetAsync("/auth/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.TryGetProperty("scheme", out _).Should().BeTrue(
            "the response must include a 'scheme' field so clients know which auth mechanism to use");
    }

    [Fact]
    public async Task GET_Auth_Status_Returns_Scheme_None_When_Auth_Disabled()
    {
        var response = await _client.GetAsync("/auth/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.GetProperty("scheme").GetString().Should().Be("none",
            "scheme should be 'none' when authentication is disabled");
    }

    [Fact]
    public async Task GET_Auth_Status_Contains_Discovery_Fields_When_Auth_Disabled()
    {
        var response = await _client.GetAsync("/auth/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.TryGetProperty("scope", out var scopeProperty).Should().BeTrue();
        root.TryGetProperty("tenantId", out var tenantIdProperty).Should().BeTrue();
        root.TryGetProperty("authority", out var authorityProperty).Should().BeTrue();
        root.TryGetProperty("audience", out var audienceProperty).Should().BeTrue();
        root.TryGetProperty("interactiveClientId", out var interactiveClientIdProperty).Should().BeTrue();

        scopeProperty.ValueKind.Should().Be(JsonValueKind.Null);
        tenantIdProperty.ValueKind.Should().Be(JsonValueKind.Null);
        authorityProperty.ValueKind.Should().Be(JsonValueKind.Null);
        audienceProperty.ValueKind.Should().Be(JsonValueKind.Null);
        interactiveClientIdProperty.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GET_Auth_Status_Bypasses_Minimum_Cli_Version_Enforcement()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Agon:MinCliVersion"] = RequiredVersion
                });
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/auth/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_Auth_Verify_Returns_200_For_Authenticated_Request()
    {
        using var factory = CreateAuthenticatedFactory();
        using var client = CreateUserClient(factory, Guid.NewGuid());

        var response = await client.GetAsync("/auth/verify");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private WebApplicationFactory<Program> CreateAuthenticatedFactory()
    {
        return _factory.WithWebHostBuilder(builder =>
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

}
