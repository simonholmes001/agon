using Agon.Application.Orchestration;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.Agents;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agon.Infrastructure.Tests.Agents;

/// <summary>
/// Tests for <see cref="MafCouncilAgent"/>.
/// Uses a <see cref="FakeChatClient"/> to verify provider-agnostic behaviour —
/// no HTTP, no real API calls, no provider-specific response parsing.
/// </summary>
public class MafCouncilAgentTests
{
    // -----------------------------------------------------------------------
    // RunAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_ReturnsMessage_WhenClientReturnsPlainText()
    {
        var fake = new FakeChatClient("Validate the riskiest assumption first.");
        var sut = CreateAgent("gpt-agent", "openai", fake);

        var response = await sut.RunAsync(CreateContext(), CancellationToken.None);

        response.Message.Should().Be("Validate the riskiest assumption first.");
        response.Patch.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_ParsesMessageAndPatch_WhenClientReturnsStructuredOutput()
    {
        var sessionId = Guid.NewGuid();
        var structured =
            $"## MESSAGE\nOnlyVisiblePart\n\n## PATCH\n{{\"ops\":[],\"meta\":{{\"agent\":\"gpt-agent\",\"round\":1,\"reason\":\"test\",\"sessionId\":\"{sessionId}\"}}}}";
        var fake = new FakeChatClient(structured);
        var sut = CreateAgent("gpt-agent", "openai", fake);

        var response = await sut.RunAsync(CreateContext(sessionId), CancellationToken.None);

        response.Message.Should().Be("OnlyVisiblePart");
        response.Patch.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_SetsAgentIdAndProvider()
    {
        var sut = CreateAgent("claude-agent", "anthropic", new FakeChatClient("ok"));

        sut.AgentId.Should().Be("claude-agent");
        sut.ModelProvider.Should().Be("anthropic");
    }

    [Fact]
    public async Task RunAsync_PropagatesException_WhenClientThrows()
    {
        var fake = new ThrowingChatClient(new InvalidOperationException("rate-limited"));
        var sut = CreateAgent("gemini-agent", "gemini", fake);

        var act = () => sut.RunAsync(CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("rate-limited");
    }

    [Fact]
    public async Task RunAsync_RawOutput_ContainsFullClientResponse()
    {
        const string raw = "Full raw output including PATCH section.";
        var sut = CreateAgent("moderator", "openai", new FakeChatClient(raw));

        var response = await sut.RunAsync(CreateContext(), CancellationToken.None);

        response.RawOutput.Should().Be(raw);
    }

    // -----------------------------------------------------------------------
    // RunStreamingAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunStreamingAsync_YieldsTokensFromClient()
    {
        var tokens = new[] { "Alpha", " Beta", " Gamma" };
        var fake = new StreamingFakeChatClient(tokens);
        var sut = CreateAgent("gpt-agent", "openai", fake);

        var collected = new List<string>();
        await foreach (var token in sut.RunStreamingAsync(CreateContext(), CancellationToken.None))
        {
            collected.Add(token);
        }

        collected.Should().Equal(tokens);
    }

    [Fact]
    public async Task RunStreamingAsync_YieldsNothing_WhenClientReturnsEmptyTokens()
    {
        var fake = new StreamingFakeChatClient([string.Empty, "  ", ""]);
        var sut = CreateAgent("gpt-agent", "openai", fake);

        var collected = new List<string>();
        await foreach (var token in sut.RunStreamingAsync(CreateContext(), CancellationToken.None))
        {
            collected.Add(token);
        }

        // Whitespace-only tokens are skipped
        collected.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static MafCouncilAgent CreateAgent(string agentId, string provider, IChatClient client) =>
        new(agentId, provider, client, maxOutputTokens: 512, NullLogger<MafCouncilAgent>.Instance);

    private static AgentContext CreateContext(Guid? sessionId = null)
    {
        var id = sessionId ?? Guid.NewGuid();
        var map = TruthMapState.CreateNew(id);
        map.CoreIdea = "Test idea";
        return new AgentContext
        {
            SessionId = id,
            Round = 1,
            Phase = SessionPhase.Construction,
            FrictionLevel = 50,
            TruthMap = map
        };
    }

    // -----------------------------------------------------------------------
    // Fake IChatClient implementations — no HTTP, no provider SDK dependency
    // -----------------------------------------------------------------------

    /// <summary>Returns a single fixed response text.</summary>
    private sealed class FakeChatClient(string responseText) : IChatClient
    {
        public ChatClientMetadata Metadata => new("fake", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, responseText);
        }

        public object? GetService(Type serviceType, object? key = null) => null;
        public void Dispose() { }
    }

    /// <summary>Yields individual token strings as streaming updates.</summary>
    private sealed class StreamingFakeChatClient(string[] tokens) : IChatClient
    {
        public ChatClientMetadata Metadata => new("fake-streaming", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var combined = string.Concat(tokens);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, combined)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var token in tokens)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, token);
            }
        }

        public object? GetService(Type serviceType, object? key = null) => null;
        public void Dispose() { }
    }

    /// <summary>Always throws the provided exception from GetResponseAsync.</summary>
    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public ChatClientMetadata Metadata => new("throwing", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(exception);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? key = null) => null;
        public void Dispose() { }
    }
}
