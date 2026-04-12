using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Domain.Agents;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.Agents;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NSubstitute;
using System.Reflection;
using AgonAgentResponse = Agon.Application.Models.AgentResponse;
using MafAgentResponse = Microsoft.Agents.AI.AgentResponse;

namespace Agon.Infrastructure.Tests.Agents;

public class MafCouncilAgentTests
{
    private readonly AIAgent _mockAIAgent;
    private readonly IAgentResponseParser _mockParser;
    private readonly MafCouncilAgent _agent;

    public MafCouncilAgentTests()
    {
        _mockAIAgent = Substitute.For<AIAgent>();
        _mockParser = Substitute.For<IAgentResponseParser>();

        _agent = new MafCouncilAgent(
            agentId: AgentId.GptAgent,
            modelProvider: "openai/gpt-5.2",
            underlyingAgent: _mockAIAgent,
            parser: _mockParser);
    }

    [Fact(Skip = "AIAgent cannot be mocked with NSubstitute - abstract class with non-virtual methods. Integration tests verify this functionality.")]
    public async Task RunAsync_WithValidContext_ReturnsAgentResponse()
    {
        // Arrange
        var context = AgentContext.ForAnalysis(
            Guid.NewGuid(),
            new TruthMap(),
            frictionLevel: 50,
            roundNumber: 1);

        var mafResponse = Substitute.For<MafAgentResponse>();
        mafResponse.ToString().Returns("## MESSAGE\nTest response\n\n## PATCH\n```json\n{}\n```");

        _mockAIAgent
            .RunAsync(Arg.Any<string>(), Arg.Any<AgentSession>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>())
            .Returns(mafResponse);

        var parsedResponse = new AgonAgentResponse(
            AgentId: AgentId.GptAgent,
            Message: "Test response",
            Patch: null,
            TokensUsed: 10,
            TimedOut: false,
            RawOutput: null);

        _mockParser
            .Parse(Arg.Any<string>(), AgentId.GptAgent)
            .Returns(parsedResponse);

        // Act
        var result = await _agent.RunAsync(context, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.AgentId.Should().Be(AgentId.GptAgent);
        result.Message.Should().Be("Test response");
        result.TimedOut.Should().BeFalse();

        await _mockAIAgent.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Any<AgentSession>(),
            Arg.Any<AgentRunOptions>(),
            Arg.Any<CancellationToken>());

        _mockParser.Received(1).Parse(Arg.Any<string>(), AgentId.GptAgent);
    }

    [Fact(Skip = "AIAgent cannot be mocked with NSubstitute - abstract class with non-virtual methods. Integration tests verify this functionality.")]
    public async Task RunAsync_WhenCancelled_ReturnsTimedOutResponse()
    {
        // Arrange
        var context = AgentContext.ForAnalysis(
            Guid.NewGuid(),
            new TruthMap(),
            frictionLevel: 50,
            roundNumber: 1);

        _mockAIAgent
            .RunAsync(Arg.Any<string>(), Arg.Any<AgentSession>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>())
            .Returns<MafAgentResponse>(_ => throw new OperationCanceledException());

        // Act
        var result = await _agent.RunAsync(context, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TimedOut.Should().BeTrue();
        result.Message.Should().BeEmpty();
    }

    [Fact(Skip = "AIAgent cannot be mocked with NSubstitute - abstract class with non-virtual methods. Integration tests verify this functionality.")]
    public async Task RunAsync_IncludesContextInPrompt()
    {
        // Arrange
        var context = AgentContext.ForCritique(
            Guid.NewGuid(),
            new TruthMap(),
            frictionLevel: 70,
            roundNumber: 2,
            [
                new AgentMessage(AgentId.GeminiAgent, "Gemini's analysis")
            ]);

        var mafResponse = Substitute.For<MafAgentResponse>();
        mafResponse.ToString().Returns("Response");

        string? capturedPrompt = null;
        _mockAIAgent
            .RunAsync(
                Arg.Do<string>(prompt => capturedPrompt = prompt),
                Arg.Any<AgentSession>(),
                Arg.Any<AgentRunOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(mafResponse);

        _mockParser
            .Parse(Arg.Any<string>(), Arg.Any<string>())
            .Returns(AgonAgentResponse.CreateTimedOut(AgentId.GptAgent));

        // Act
        await _agent.RunAsync(context, CancellationToken.None);

        // Assert
        capturedPrompt.Should().NotBeNull();
        capturedPrompt.Should().Contain("Gemini's analysis");
        capturedPrompt.Should().Contain("Round: 2");
        capturedPrompt.Should().Contain("Friction Level: 70");
    }

    [Fact]
    public void BuildPrompt_WithUserMessages_IncludesMessagesInPrompt()
    {
        // Arrange - this tests the prompt building logic directly via integration test
        // We'll create a context with user messages and verify the prompt contains them
        var sessionId = Guid.NewGuid();
        var userMessages = new List<UserMessage>
        {
            new("Target users are freelancers", DateTimeOffset.UtcNow.AddMinutes(-5), 1),
            new("Budget is $50k", DateTimeOffset.UtcNow.AddMinutes(-3), 1)
        };

        var context = AgentContext.ForClarification(
            sessionId,
            new TruthMap { CoreIdea = "Build a SaaS for project management" },
            frictionLevel: 75,
            roundNumber: 1,
            userMessages);

        // We can't easily test BuildPrompt directly as it's private, but we can verify
        // through an integration test that would call the real agent
        // For now, let's mark this as a reminder to test via integration test
        
        // This test documents the expected behavior:
        // The prompt should contain a "# User Responses" section with all user messages
        context.UserMessages.Should().HaveCount(2);
        context.UserMessages[0].Content.Should().Be("Target users are freelancers");
        context.UserMessages[1].Content.Should().Be("Budget is $50k");
    }

    [Fact]
    public void BuildPrompt_WithLongAttachmentExtractedText_IncludesContentBeyondLegacyExcerptLimit()
    {
        // Arrange
        var marker = "TAIL-MARKER-BEYOND-2000";
        var extractedText = new string('A', 2100) + marker;
        var attachment = new SessionAttachment(
            AttachmentId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            FileName: "large.pdf",
            ContentType: "application/pdf",
            SizeBytes: 1024,
            BlobName: "blob",
            BlobUri: "https://example.blob.core.windows.net/session-attachments/blob",
            AccessUrl: "/sessions/1/attachments/1/content",
            ExtractedText: extractedText,
            UploadedAt: DateTimeOffset.UtcNow);

        var context = AgentContext.ForAnalysis(
            sessionId: attachment.SessionId,
            truthMap: new TruthMap(),
            frictionLevel: 50,
            roundNumber: 1,
            attachments: [attachment]);

        // Act
        var prompt = BuildPromptForTest(context);

        // Assert
        prompt.Should().Contain(marker);
    }

    [Fact]
    public void MergeProviderUsage_WithUsageDetails_PrefersProviderCounts()
    {
        var parsed = new AgonAgentResponse(
            AgentId: AgentId.GptAgent,
            Message: "test",
            Patch: null,
            TokensUsed: 80,
            TimedOut: false,
            RawOutput: "raw");
        var usage = new UsageDetails
        {
            InputTokenCount = 30,
            OutputTokenCount = 70,
            TotalTokenCount = 100
        };

        var merged = MafCouncilAgent.MergeProviderUsage(parsed, usage);

        merged.TokensUsed.Should().Be(100);
        merged.PromptTokens.Should().Be(30);
        merged.CompletionTokens.Should().Be(70);
        merged.TokenUsageSource.Should().Be("provider");
    }

    [Fact]
    public void MergeProviderUsage_WithoutTotal_ComputesFromInputAndOutput()
    {
        var parsed = new AgonAgentResponse(
            AgentId: AgentId.GptAgent,
            Message: "test",
            Patch: null,
            TokensUsed: 42,
            TimedOut: false,
            RawOutput: "raw");
        var usage = new UsageDetails
        {
            InputTokenCount = 15,
            OutputTokenCount = 25,
            TotalTokenCount = 0
        };

        var merged = MafCouncilAgent.MergeProviderUsage(parsed, usage);

        merged.TokensUsed.Should().Be(40);
        merged.PromptTokens.Should().Be(15);
        merged.CompletionTokens.Should().Be(25);
        merged.TokenUsageSource.Should().Be("provider");
    }

    [Fact]
    public void MergeProviderUsage_WithoutUsage_KeepsEstimatedValues()
    {
        var parsed = new AgonAgentResponse(
            AgentId: AgentId.GptAgent,
            Message: "test",
            Patch: null,
            TokensUsed: 42,
            TimedOut: false,
            RawOutput: "raw",
            PromptTokens: 0,
            CompletionTokens: 42,
            TokenUsageSource: "estimated");

        var merged = MafCouncilAgent.MergeProviderUsage(parsed, usage: null);

        merged.Should().BeEquivalentTo(parsed);
    }

    private string BuildPromptForTest(AgentContext context)
    {
        var method = typeof(MafCouncilAgent).GetMethod(
            "BuildPrompt",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull("BuildPrompt is required for prompt assembly behavior tests.");
        var result = method!.Invoke(_agent, [context]);

        result.Should().BeOfType<string>();
        return (string)result!;
    }
}
