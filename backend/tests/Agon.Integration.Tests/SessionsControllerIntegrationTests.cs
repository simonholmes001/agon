using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Agon.Domain.Sessions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Agon.Integration.Tests;

/// <summary>
/// Integration tests for SessionsController HTTP endpoints.
/// These tests verify end-to-end behavior including routing, DI, serialization, and HTTP responses.
/// </summary>
public class SessionsControllerIntegrationTests : IClassFixture<AgonWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SessionsControllerIntegrationTests(AgonWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task POST_Sessions_Should_Create_New_Session()
    {
        // Arrange
        var request = new
        {
            idea = "Build a SaaS platform for small business inventory management",
            frictionLevel = 50
        };

        // Act
        var response = await _client.PostAsJsonAsync("/sessions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.GetProperty("id").GetGuid().Should().NotBeEmpty();
        root.GetProperty("phase").GetString().Should().Be("Intake");
        root.GetProperty("status").GetString().Should().Be("Active");
        root.GetProperty("frictionLevel").GetInt32().Should().Be(50);
        root.GetProperty("currentRound").GetInt32().Should().Be(0);
        root.GetProperty("tokensUsed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task POST_Sessions_Should_Return_400_When_Idea_Is_Empty()
    {
        // Arrange
        var request = new
        {
            idea = "",
            frictionLevel = 50
        };

        // Act
        var response = await _client.PostAsJsonAsync("/sessions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Idea is required");
    }

    [Fact]
    public async Task POST_Sessions_Should_Return_400_When_FrictionLevel_Is_Invalid()
    {
        // Arrange
        var request = new
        {
            idea = "Test idea",
            frictionLevel = 150  // Invalid: > 100
        };

        // Act
        var response = await _client.PostAsJsonAsync("/sessions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("FrictionLevel must be between 0 and 100");
    }

    [Fact]
    public async Task GET_Sessions_Should_Return_404_For_NonExistent_Session()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/sessions/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain($"Session {nonExistentId} not found");
    }

    [Fact]
    public async Task GET_Sessions_Should_Return_Session_After_Creation()
    {
        // Arrange - Create a session first
        var createRequest = new
        {
            idea = "Test idea for GET endpoint",
            frictionLevel = 75
        };

        var createResponse = await _client.PostAsJsonAsync("/sessions", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var sessionId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Act - Retrieve the session
        var getResponse = await _client.GetAsync($"/sessions/{sessionId}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getContent = await getResponse.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getContent);
        var root = getDoc.RootElement;

        root.GetProperty("id").GetGuid().Should().Be(sessionId);
        root.GetProperty("frictionLevel").GetInt32().Should().Be(75);
    }

    [Fact]
    public async Task GET_Sessions_Should_List_User_Sessions()
    {
        var request1 = new
        {
            idea = "Session one",
            frictionLevel = 40
        };
        var request2 = new
        {
            idea = "Session two",
            frictionLevel = 60
        };

        var response1 = await _client.PostAsJsonAsync("/sessions", request1);
        var response2 = await _client.PostAsJsonAsync("/sessions", request2);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await _client.GetAsync("/sessions");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await listResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GET_Sessions_TruthMap_Should_Return_Empty_TruthMap_For_New_Session()
    {
        // Arrange - Create a session first
        var createRequest = new
        {
            idea = "Test idea for TruthMap endpoint",
            frictionLevel = 50
        };

        var createResponse = await _client.PostAsJsonAsync("/sessions", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var sessionId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Act - Get TruthMap
        var response = await _client.GetAsync($"/sessions/{sessionId}/truthmap");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // New session should have empty collections
        root.GetProperty("claims").GetArrayLength().Should().Be(0);
        root.GetProperty("assumptions").GetArrayLength().Should().Be(0);
        root.GetProperty("risks").GetArrayLength().Should().Be(0);
        root.GetProperty("decisions").GetArrayLength().Should().Be(0);
        root.GetProperty("evidence").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task POST_Sessions_Start_Should_Return_202_Accepted()
    {
        // Arrange - Create a session first
        var createRequest = new
        {
            idea = "Test idea for start endpoint",
            frictionLevel = 50
        };

        var createResponse = await _client.PostAsJsonAsync("/sessions", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var sessionId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Act - Start clarification
        var response = await _client.PostAsync($"/sessions/{sessionId}/start", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task POST_Sessions_Messages_Should_Return_202_Accepted()
    {
        // Arrange - Create a session first
        var createRequest = new
        {
            idea = "Test idea for messages endpoint",
            frictionLevel = 50
        };

        var createResponse = await _client.PostAsJsonAsync("/sessions", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var sessionId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Start clarification phase (required before submitting messages)
        await _client.PostAsync($"/sessions/{sessionId}/start", null);

        var messageRequest = new
        {
            content = "Target customers are small retail businesses"
        };

        // Act - Submit message
        var response = await _client.PostAsJsonAsync($"/sessions/{sessionId}/messages", messageRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task POST_Sessions_Attachments_Should_Return_503_When_Storage_Not_Configured()
    {
        var createRequest = new
        {
            idea = "Test idea for attachments endpoint",
            frictionLevel = 50
        };

        var createResponse = await _client.PostAsJsonAsync("/sessions", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var sessionId = createDoc.RootElement.GetProperty("id").GetGuid();

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("sample doc content"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        multipart.Add(fileContent, "file", "sample.txt");

        var response = await _client.PostAsync($"/sessions/{sessionId}/attachments", multipart);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("errorCode").GetString().Should().BeOneOf(
            "ATTACHMENT_STORAGE_NOT_CONFIGURED",
            "ATTACHMENT_STORAGE_UNAVAILABLE");
        doc.RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("hint").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task POST_Sessions_Messages_Should_Return_404_If_Not_In_Clarification_Phase()
    {
        // Arrange - Create a session but DON'T start clarification
        var createRequest = new
        {
            idea = "Test idea for messages validation",
            frictionLevel = 50
        };

        var createResponse = await _client.PostAsJsonAsync("/sessions", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var sessionId = createDoc.RootElement.GetProperty("id").GetGuid();

        var messageRequest = new
        {
            content = "This should fail because we're still in Intake phase"
        };

        // Act - Try to submit message without starting clarification
        var response = await _client.PostAsJsonAsync($"/sessions/{sessionId}/messages", messageRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_Sessions_Snapshots_Should_Return_Empty_List_For_New_Session()
    {
        // Arrange - Create a session first
        var createRequest = new
        {
            idea = "Test idea for snapshots endpoint",
            frictionLevel = 50
        };

        var createResponse = await _client.PostAsJsonAsync("/sessions", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var sessionId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Act - Get snapshots
        var response = await _client.GetAsync($"/sessions/{sessionId}/snapshots");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.GetArrayLength().Should().Be(0, "new session should have no snapshots");
    }

    [Fact]
    public async Task POST_Sessions_Start_Should_Transition_Session_To_Clarification_Phase()
    {
        // Arrange - Create a session first
        var createRequest = new
        {
            idea = "Test orchestrator integration via start endpoint",
            frictionLevel = 50
        };

        var createResponse = await _client.PostAsJsonAsync("/sessions", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var sessionId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Verify initial phase is Intake
        var initialResponse = await _client.GetAsync($"/sessions/{sessionId}");
        var initialContent = await initialResponse.Content.ReadAsStringAsync();
        using var initialDoc = JsonDocument.Parse(initialContent);
        initialDoc.RootElement.GetProperty("phase").GetString().Should().Be("Intake");

        // Act - Start clarification (should invoke Orchestrator)
        var startResponse = await _client.PostAsync($"/sessions/{sessionId}/start", null);

        // Assert - Should return 202 Accepted
        startResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Verify phase changed to Clarification
        var afterResponse = await _client.GetAsync($"/sessions/{sessionId}");
        var afterContent = await afterResponse.Content.ReadAsStringAsync();
        using var afterDoc = JsonDocument.Parse(afterContent);
        afterDoc.RootElement.GetProperty("phase").GetString().Should().Be("Clarification");
    }

    [Fact]
    public async Task POST_Sessions_Start_Should_Complete_Successfully_Without_Errors()
    {
        // Arrange - Create a session
        var createRequest = new
        {
            idea = "Build a mobile app for fitness tracking",
            frictionLevel = 50
        };

        var createResponse = await _client.PostAsJsonAsync("/sessions", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var sessionId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Act - Start clarification
        var startResponse = await _client.PostAsync($"/sessions/{sessionId}/start", null);

        // Assert - Should return 202 Accepted and not throw exceptions
        startResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        
        // NOTE: In Testing environment, Orchestrator and agents are not registered
        // so this test verifies the API layer works correctly without actual agent execution.
        // Full agent execution is tested in manual/E2E tests with real LLM providers.
    }
}
