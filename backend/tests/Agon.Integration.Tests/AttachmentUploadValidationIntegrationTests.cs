using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Agon.Integration.Tests;

public class AttachmentUploadValidationIntegrationTests : IClassFixture<AgonWebApplicationFactory>
{
    private readonly AgonWebApplicationFactory _baseFactory;

    public AttachmentUploadValidationIntegrationTests(AgonWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task POST_Sessions_Attachments_Should_Return_415_For_Unsupported_Format()
    {
        var client = _baseFactory.CreateClient();
        var sessionId = await CreateSessionAsync(client, "Unsupported format validation");

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("not-supported-binary"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        multipart.Add(fileContent, "file", "payload.exe");

        var response = await client.PostAsync($"/sessions/{sessionId}/attachments", multipart);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("ATTACHMENT_UNSUPPORTED_FORMAT");
        doc.RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("hint").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task POST_Sessions_Attachments_Should_Return_413_When_Route_Size_Limit_Is_Exceeded()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AttachmentProcessing:Validation:MaxUploadBytes", "1000");
            builder.UseSetting("AttachmentProcessing:Validation:MaxTextUploadBytes", "10");
            builder.UseSetting("AttachmentProcessing:Validation:MaxImageUploadBytes", "1000");
            builder.UseSetting("AttachmentProcessing:Validation:MaxDocumentUploadBytes", "1000");
        });

        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client, "Route limit validation");

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("this text is too large"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        multipart.Add(fileContent, "file", "notes.txt");

        var response = await client.PostAsync($"/sessions/{sessionId}/attachments", multipart);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("ATTACHMENT_SIZE_LIMIT_EXCEEDED");
        doc.RootElement.GetProperty("route").GetString().Should().Be("text");
        doc.RootElement.GetProperty("maxAllowedBytes").GetInt32().Should().Be(10);
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
}
