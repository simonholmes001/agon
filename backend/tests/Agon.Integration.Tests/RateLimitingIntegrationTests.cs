using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Agon.Integration.Tests;

public class RateLimitingIntegrationTests
{
    [Fact(Skip = "Rate limiter behavior is nondeterministic under current WebApplicationFactory test host setup; validate in environment integration tests.")]
    public async Task POST_Sessions_Should_Return_429_When_SessionCreate_RateLimit_Is_Exceeded()
    {
        using var factory = new RateLimitedAgonWebApplicationFactory(permitLimit: 2, windowSeconds: 60);
        var client = factory.CreateClient();

        var first = await PostSessionAsync(client, "rate-limit-1");
        var second = await PostSessionAsync(client, "rate-limit-2");
        var third = await PostSessionAsync(client, "rate-limit-3");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        third.Headers.Contains("Retry-After").Should().BeTrue();

        using var errorDoc = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        errorDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact(Skip = "Rate limiter behavior is nondeterministic under current WebApplicationFactory test host setup; validate in environment integration tests.")]
    public async Task POST_Sessions_Should_Not_Be_Throttled_Below_Configured_Limit()
    {
        using var factory = new RateLimitedAgonWebApplicationFactory(permitLimit: 3, windowSeconds: 60);
        var client = factory.CreateClient();

        var first = await PostSessionAsync(client, "under-limit-1");
        var second = await PostSessionAsync(client, "under-limit-2");
        var third = await PostSessionAsync(client, "under-limit-3");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        third.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static Task<HttpResponseMessage> PostSessionAsync(HttpClient client, string idea)
    {
        return client.PostAsJsonAsync("/sessions", new
        {
            idea,
            frictionLevel = 50
        });
    }

    private sealed class RateLimitedAgonWebApplicationFactory : AgonWebApplicationFactory
    {
        private readonly int _permitLimit;
        private readonly int _windowSeconds;

        public RateLimitedAgonWebApplicationFactory(int permitLimit, int windowSeconds)
        {
            _permitLimit = permitLimit;
            _windowSeconds = windowSeconds;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiRateLimiting:Enabled"] = "true",
                    ["ApiRateLimiting:ForceEnableInTesting"] = "true",
                    ["ApiRateLimiting:SessionCreate:PermitLimit"] = _permitLimit.ToString(),
                    ["ApiRateLimiting:SessionCreate:WindowSeconds"] = _windowSeconds.ToString(),
                    ["ApiRateLimiting:SessionCreate:QueueLimit"] = "0",
                    ["ApiRateLimiting:SessionMessage:PermitLimit"] = "100",
                    ["ApiRateLimiting:SessionMessage:WindowSeconds"] = "60",
                    ["ApiRateLimiting:SessionMessage:QueueLimit"] = "0",
                    ["ApiRateLimiting:AttachmentUpload:PermitLimit"] = "100",
                    ["ApiRateLimiting:AttachmentUpload:WindowSeconds"] = "60",
                    ["ApiRateLimiting:AttachmentUpload:QueueLimit"] = "0"
                });
            });
        }
    }
}
