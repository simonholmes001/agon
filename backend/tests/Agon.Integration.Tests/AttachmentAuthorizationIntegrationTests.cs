using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agon.Infrastructure.Persistence.PostgreSQL;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agon.Integration.Tests;

public class AttachmentAuthorizationIntegrationTests : IClassFixture<AgonWebApplicationFactory>
{
    private readonly AgonWebApplicationFactory _baseFactory;

    public AttachmentAuthorizationIntegrationTests(AgonWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task GET_Attachments_Should_Return_Own_Session_Attachments_When_Authenticated()
    {
        using var factory = CreateAuthenticatedFactory();
        var userId = Guid.NewGuid();
        var client = CreateUserClient(factory, userId);

        var sessionId = await CreateSessionAsync(client, "Attachment auth test - own list");
        var attachmentId = await SeedAttachmentAsync(factory, sessionId, userId, "brief.md");

        var response = await client.GetAsync($"/sessions/{sessionId}/attachments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("id").GetGuid().Should().Be(attachmentId);
        doc.RootElement[0].GetProperty("accessUrl").GetString()
            .Should().Be($"/sessions/{sessionId}/attachments/{attachmentId}/content");
    }

    [Fact]
    public async Task GET_Attachments_Should_Return_404_For_Different_Authenticated_User()
    {
        using var factory = CreateAuthenticatedFactory();
        var ownerId = Guid.NewGuid();
        var intruderId = Guid.NewGuid();
        var ownerClient = CreateUserClient(factory, ownerId);
        var intruderClient = CreateUserClient(factory, intruderId);

        var sessionId = await CreateSessionAsync(ownerClient, "Attachment auth test - cross user");
        await SeedAttachmentAsync(factory, sessionId, ownerId, "private.txt");

        var response = await intruderClient.GetAsync($"/sessions/{sessionId}/attachments");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_Attachment_Content_Should_Return_404_When_Attachment_Belongs_To_Different_Session()
    {
        using var factory = CreateAuthenticatedFactory();
        var userId = Guid.NewGuid();
        var client = CreateUserClient(factory, userId);

        var sessionA = await CreateSessionAsync(client, "Attachment auth test - session A");
        var sessionB = await CreateSessionAsync(client, "Attachment auth test - session B");
        var attachmentInB = await SeedAttachmentAsync(factory, sessionB, userId, "cross-session.pdf");

        var response = await client.GetAsync($"/sessions/{sessionA}/attachments/{attachmentInB}/content");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private WebApplicationFactory<Program> CreateAuthenticatedFactory()
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Enabled"] = "true",
                    ["Authentication:AzureAd:Authority"] = "https://example.local/tenant/v2.0",
                    ["Authentication:AzureAd:Audience"] = "api://agon-tests"
                });
            });

            builder.ConfigureServices(services =>
            {
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

    private static HttpClient CreateUserClient(WebApplicationFactory<Program> factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return client;
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client, string idea)
    {
        var response = await client.PostAsJsonAsync("/sessions", new
        {
            idea,
            frictionLevel = 50
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> SeedAttachmentAsync(
        WebApplicationFactory<Program> factory,
        Guid sessionId,
        Guid userId,
        string fileName)
    {
        var attachmentId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgonDbContext>();
        dbContext.SessionAttachments.Add(new SessionAttachmentEntity
        {
            Id = attachmentId,
            SessionId = sessionId,
            UserId = userId,
            FileName = fileName,
            ContentType = "text/plain",
            SizeBytes = 42,
            BlobName = $"{sessionId:N}/{attachmentId:N}-{fileName}",
            BlobUri = "https://storage.test/session-attachments/blob",
            AccessUrl = $"/sessions/{sessionId}/attachments/{attachmentId}/content",
            ExtractedText = "seeded text",
            UploadedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
        return attachmentId;
    }
}
