using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agon.Api.Configuration;
using Agon.Api.Services;
using Agon.Application.Interfaces;
using Agon.Infrastructure.Persistence.Entities;
using Agon.Infrastructure.Persistence.PostgreSQL;
using Agon.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agon.Integration.Tests;

public sealed class TrialAccessIntegrationTests : IClassFixture<AgonWebApplicationFactory>
{
    private const string RequiredTesterGroupId = "11111111-1111-1111-1111-111111111111";

    private readonly AgonWebApplicationFactory _baseFactory;

    public TrialAccessIntegrationTests(AgonWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task CreateSession_Should_Deny_NonAllowlisted_User_When_TrialAccess_Enabled()
    {
        using var factory = CreateTrialEnabledFactory();
        var userClient = CreateUserClient(factory, Guid.NewGuid());

        var response = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "Trial gating test for non-allowlisted user",
            frictionLevel = 50
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("errorCode").GetString().Should().Be("TRIAL_NOT_ALLOWLISTED");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgonDbContext>();
        var deniedAudit = dbContext.TrialAuditEvents
            .Where(eventRow => eventRow.ReasonCode == "TRIAL_NOT_ALLOWLISTED")
            .ToList();

        deniedAudit.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateSession_Should_Allow_Allowlisted_User_And_Write_Allow_Audit_Event()
    {
        using var factory = CreateTrialEnabledFactory();
        var userId = Guid.NewGuid();
        var userClient = CreateUserClient(factory, userId, [RequiredTesterGroupId]);

        var response = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "Trial gating test for allowlisted user",
            frictionLevel = 50
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgonDbContext>();
        var allowAudit = dbContext.TrialAuditEvents
            .Where(eventRow => eventRow.UserId == userId && eventRow.ReasonCode == "TRIAL_ALLOWED")
            .ToList();

        allowAudit.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateSession_Should_Deny_User_InWrongEntraGroup()
    {
        using var factory = CreateTrialEnabledFactory();
        var userId = Guid.NewGuid();
        var userClient = CreateUserClient(factory, userId, ["22222222-2222-2222-2222-222222222222"]);

        var response = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "Expired tester should be denied",
            frictionLevel = 50
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("errorCode").GetString().Should().Be("TRIAL_NOT_ALLOWLISTED");
    }

    [Fact]
    public async Task CreateSession_Should_Deny_User_WithLegacyTrialGrant_WhenGroupMissing()
    {
        using var factory = CreateTrialEnabledFactory();
        var userId = Guid.NewGuid();
        await SeedGrantAsync(factory, userId, DateTimeOffset.UtcNow.AddDays(7));
        var userClient = CreateUserClient(factory, userId);

        var response = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "Legacy trial grant should not bypass Entra group access",
            frictionLevel = 50
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("errorCode").GetString().Should().Be("TRIAL_NOT_ALLOWLISTED");
    }

    [Fact]
    public async Task StartDebate_Should_Deny_WhenUserIsOverQuota_BeforeExecution()
    {
        using var factory = CreateTrialEnabledFactory(tokenLimit: 100, requestsPerMinute: 100, burstCapacity: 100);
        var userId = Guid.NewGuid();
        var userClient = CreateUserClient(factory, userId, [RequiredTesterGroupId]);

        var createResponse = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "Quota enforcement test",
            frictionLevel = 50
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var sessionId = await ReadSessionIdAsync(createResponse);
        await SeedTokenUsageAsync(factory, userId, sessionId, totalTokens: 100, promptTokens: 30, completionTokens: 70);

        var response = await userClient.PostAsJsonAsync($"/sessions/{sessionId}/start", new { });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("errorCode").GetString().Should().Be("TRIAL_QUOTA_EXCEEDED");
        payload.RootElement.GetProperty("limitType").GetString().Should().Be("quota");
        payload.RootElement.GetProperty("remainingTokens").GetInt64().Should().Be(0);
        payload.RootElement.GetProperty("windowResetAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task CreateSession_Should_ReturnDeterministic429_WhenRateLimited()
    {
        using var factory = CreateTrialEnabledFactory(tokenLimit: 10000, requestsPerMinute: 1, burstCapacity: 1);
        var userId = Guid.NewGuid();
        var userClient = CreateUserClient(factory, userId, [RequiredTesterGroupId]);

        var first = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "First request should pass",
            frictionLevel = 50
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "Second request should be throttled",
            frictionLevel = 50
        });

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        second.Headers.TryGetValues("Retry-After", out var retryAfterValues).Should().BeTrue();
        retryAfterValues.Should().NotBeNullOrEmpty();
        var retryAfterSeconds = int.Parse(retryAfterValues!.First());
        retryAfterSeconds.Should().BeGreaterThan(0);

        using var payload = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("errorCode").GetString().Should().Be("TRIAL_RATE_LIMIT_EXCEEDED");
        payload.RootElement.GetProperty("limitType").GetString().Should().Be("rate");
    }

    [Fact]
    public async Task CreateSession_Should_ThrottleConcurrentRequests_ForSameUser()
    {
        using var factory = CreateTrialEnabledFactory(tokenLimit: 10000, requestsPerMinute: 1, burstCapacity: 1);
        var userId = Guid.NewGuid();

        var clientA = CreateUserClient(factory, userId, [RequiredTesterGroupId]);
        var clientB = CreateUserClient(factory, userId, [RequiredTesterGroupId]);

        var requestA = clientA.PostAsJsonAsync("/sessions", new { idea = "Concurrent request A", frictionLevel = 50 });
        var requestB = clientB.PostAsJsonAsync("/sessions", new { idea = "Concurrent request B", frictionLevel = 50 });
        var responses = await Task.WhenAll(requestA, requestB);
        responses.Count(response => response.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests).Should().Be(1);
    }

    [Fact]
    public async Task SubmitMessage_Should_Deny_WhenUserIsOverQuota()
    {
        using var factory = CreateTrialEnabledFactory(tokenLimit: 120, requestsPerMinute: 100, burstCapacity: 100);
        var userId = Guid.NewGuid();
        var userClient = CreateUserClient(factory, userId, [RequiredTesterGroupId]);

        var createResponse = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "Submit message quota test",
            frictionLevel = 50
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var sessionId = await ReadSessionIdAsync(createResponse);

        var startResponse = await userClient.PostAsJsonAsync($"/sessions/{sessionId}/start", new { });
        startResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await SeedTokenUsageAsync(factory, userId, sessionId, totalTokens: 120, promptTokens: 40, completionTokens: 80);

        var response = await userClient.PostAsJsonAsync($"/sessions/{sessionId}/messages", new
        {
            content = "This should be blocked by quota before processing."
        });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("errorCode").GetString().Should().Be("TRIAL_QUOTA_EXCEEDED");
        payload.RootElement.GetProperty("limitType").GetString().Should().Be("quota");
    }

    [Fact]
    public async Task UsageEndpoint_Should_ReturnRemainingQuotaAndProviderBreakdown()
    {
        using var factory = CreateTrialEnabledFactory(tokenLimit: 500, requestsPerMinute: 100, burstCapacity: 100);
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await SeedTokenUsageAsync(factory, userId, sessionId, totalTokens: 180, promptTokens: 60, completionTokens: 120);
        var userClient = CreateUserClient(factory, userId, [RequiredTesterGroupId]);

        var response = await userClient.GetAsync("/usage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("quota").GetProperty("tokenLimit").GetInt32().Should().Be(500);
        payload.RootElement.GetProperty("quota").GetProperty("usedTokens").GetInt64().Should().Be(180);
        payload.RootElement.GetProperty("quota").GetProperty("remainingTokens").GetInt64().Should().Be(320);
        payload.RootElement.GetProperty("windowStart").ValueKind.Should().NotBe(JsonValueKind.Null);
        payload.RootElement.GetProperty("windowEnd").ValueKind.Should().NotBe(JsonValueKind.Null);

        var usageRows = payload.RootElement.GetProperty("usageByProviderModel");
        usageRows.GetArrayLength().Should().Be(1);
        usageRows[0].GetProperty("provider").GetString().Should().Be("OpenAI");
        usageRows[0].GetProperty("model").GetString().Should().Be("gpt-5");
        usageRows[0].GetProperty("totalTokens").GetInt32().Should().Be(180);
    }

    [Fact]
    public async Task AdminEndpoints_Should_GrantAndRevokeTester_WithAuditTrail()
    {
        using var factory = CreateTrialEnabledFactory();
        var adminClient = CreateAdminClient(factory);
        var targetUserId = Guid.NewGuid();
        var expiresAtUtc = DateTimeOffset.UtcNow.AddDays(5);

        var grantResponse = await adminClient.PutAsJsonAsync(
            $"/admin/trial/testers/{targetUserId}",
            new { expiresAtUtc });

        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var revokeRequest = new HttpRequestMessage(HttpMethod.Delete, $"/admin/trial/testers/{targetUserId}")
        {
            Content = JsonContent.Create(new { reason = "test cleanup" })
        };
        var revokeResponse = await adminClient.SendAsync(revokeRequest);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgonDbContext>();
        var grant = dbContext.TrialTesterGrants.Single(grantRow => grantRow.UserId == targetUserId);
        grant.RevokedAt.Should().NotBeNull();
        grant.RevokeReason.Should().Be("test cleanup");

        var auditCodes = dbContext.TrialAuditEvents
            .Where(eventRow => eventRow.UserId == targetUserId)
            .Select(eventRow => eventRow.ReasonCode)
            .ToList();
        auditCodes.Should().Contain("TRIAL_TESTER_GRANTED");
        auditCodes.Should().Contain("TRIAL_TESTER_REVOKED");
    }

    [Fact]
    public async Task ResetQuota_Should_DeleteWindowUsage_AndEmitAudit()
    {
        using var factory = CreateTrialEnabledFactory();
        var adminClient = CreateAdminClient(factory);
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await SeedTokenUsageAsync(factory, userId, sessionId, totalTokens: 180, promptTokens: 60, completionTokens: 120);

        var resetResponse = await adminClient.PostAsJsonAsync($"/admin/trial/quotas/{userId}/reset", new { });
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgonDbContext>();
        dbContext.TokenUsageRecords.Where(record => record.UserId == userId).Should().BeEmpty();
        dbContext.TrialAuditEvents
            .Any(eventRow => eventRow.UserId == userId && eventRow.ReasonCode == "TRIAL_QUOTA_RESET")
            .Should().BeTrue();
    }

    [Fact]
    public async Task KillSwitch_Should_BlockAndRestoreTrialTraffic()
    {
        using var factory = CreateTrialEnabledFactory();
        var adminClient = CreateAdminClient(factory);
        var userId = Guid.NewGuid();
        var userClient = CreateUserClient(factory, userId, [RequiredTesterGroupId]);

        var disableResponse = await adminClient.PostAsJsonAsync("/admin/trial/kill-switch", new { enabled = true });
        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var denied = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "Kill switch should block this.",
            frictionLevel = 50
        });
        denied.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using (var payload = JsonDocument.Parse(await denied.Content.ReadAsStringAsync()))
        {
            payload.RootElement.GetProperty("errorCode").GetString().Should().Be("TRIAL_TRAFFIC_DISABLED");
        }

        var enableResponse = await adminClient.PostAsJsonAsync("/admin/trial/kill-switch", new { enabled = false });
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var allowed = await userClient.PostAsJsonAsync("/sessions", new
        {
            idea = "Traffic restored.",
            frictionLevel = 50
        });
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private WebApplicationFactory<Program> CreateTrialEnabledFactory(
        int tokenLimit = 5000,
        int requestsPerMinute = 20,
        int burstCapacity = 10)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Enabled"] = "true",
                    ["Authentication:AzureAd:Authority"] = "https://example.local/tenant/v2.0",
                    ["Authentication:AzureAd:Audience"] = "api://agon-tests",
                    ["TrialAccess:Enabled"] = "true",
                    ["TrialAccess:DefaultTrialDays"] = "7",
                    ["TrialAccess:AdminApiKey"] = "trial-admin-secret",
                    ["TrialAccess:EnforceEntraGroupMembership"] = "true",
                    ["TrialAccess:RequiredEntraGroupObjectIds:0"] = RequiredTesterGroupId,
                    ["TrialAccess:Quota:Enabled"] = "true",
                    ["TrialAccess:Quota:TokenLimit"] = tokenLimit.ToString(),
                    ["TrialAccess:Quota:WindowDays"] = "7",
                    ["TrialAccess:RequestRateLimit:Enabled"] = "true",
                    ["TrialAccess:RequestRateLimit:RequestsPerMinute"] = requestsPerMinute.ToString(),
                    ["TrialAccess:RequestRateLimit:BurstCapacity"] = burstCapacity.ToString()
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITokenUsageRepository>();
                services.AddScoped<ITokenUsageRepository, TokenUsageRepository>();
                services.RemoveAll<TrialAccessConfiguration>();
                services.AddSingleton(new TrialAccessConfiguration
                {
                    Enabled = true,
                    DefaultTrialDays = 7,
                    AdminApiKey = "trial-admin-secret",
                    EnforceEntraGroupMembership = true,
                    RequiredEntraGroupObjectIds = [RequiredTesterGroupId],
                    Quota = new TrialQuotaConfiguration
                    {
                        Enabled = true,
                        TokenLimit = tokenLimit,
                        WindowDays = 7
                    },
                    RequestRateLimit = new TrialRequestRateLimitConfiguration
                    {
                        Enabled = true,
                        RequestsPerMinute = requestsPerMinute,
                        BurstCapacity = burstCapacity
                    }
                });
                services.RemoveAll<TrialRequestRateLimiter>();
                services.AddSingleton<TrialRequestRateLimiter>();

                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

                services.AddAuthorization(options =>
                {
                    options.FallbackPolicy = new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                        .RequireAuthenticatedUser()
                        .Build();
                });
            });
        });
    }

    private static HttpClient CreateUserClient(
        WebApplicationFactory<Program> factory,
        Guid userId,
        IReadOnlyList<string>? groups = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        if (groups is { Count: > 0 })
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserGroupsHeader, string.Join(',', groups));
        }

        return client;
    }

    private static HttpClient CreateAdminClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agon-Admin-Key", "trial-admin-secret");
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        return client;
    }

    private static async Task SeedGrantAsync(WebApplicationFactory<Program> factory, Guid userId, DateTimeOffset expiresAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgonDbContext>();
        dbContext.TrialTesterGrants.Add(new TrialTesterGrantEntity
        {
            UserId = userId,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedBy = "test",
            ExpiresAt = expiresAt,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedTokenUsageAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        Guid sessionId,
        int totalTokens,
        int promptTokens,
        int completionTokens)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgonDbContext>();
        dbContext.TokenUsageRecords.Add(new TokenUsageRecordEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionId = sessionId,
            AgentId = "gpt_agent",
            Provider = "OpenAI",
            Model = "gpt-5",
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            Source = "provider",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> ReadSessionIdAsync(HttpResponseMessage createResponse)
    {
        using var payload = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("id").GetGuid();
    }
}
