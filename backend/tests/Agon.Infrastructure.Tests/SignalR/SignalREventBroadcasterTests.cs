using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.SignalR;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Agon.Infrastructure.Tests.SignalR;

public class SignalREventBroadcasterTests
{
    [Fact]
    public async Task RoundProgressAsync_SendsRoundProgressEvent_ToSessionGroup()
    {
        var sessionId = Guid.NewGuid();
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var hubContext = Substitute.For<IHubContext<DebateHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(hubClients);
        hubClients.Group(DebateHub.SessionGroupName(sessionId)).Returns(proxy);
        var sut = new SignalREventBroadcaster(hubContext, NullLogger<SignalREventBroadcaster>.Instance);

        await sut.RoundProgressAsync(sessionId, SessionPhase.Construction, cancellationToken);

        await proxy.Received(1).SendCoreAsync(
            "RoundProgress",
            Arg.Is<object?[]>(args =>
                args.Length == 1
                && HasProperty(args[0], "SessionId", sessionId)
                && HasProperty(args[0], "Phase", SessionPhase.Construction.ToString())),
            cancellationToken);
    }

    [Fact]
    public async Task TruthMapPatchedAsync_SendsPatchEvent_ToSessionGroup()
    {
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch
        {
            Meta = new PatchMeta
            {
                Agent = "moderator",
                Round = 1,
                Reason = "add question",
                SessionId = sessionId
            },
            Ops =
            [
                new PatchOperation
                {
                    Op = PatchOperationType.Add,
                    Path = "/open_questions/0",
                    Value = new { id = Guid.NewGuid().ToString("N"), text = "Who is the user?" }
                }
            ]
        };
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var hubContext = Substitute.For<IHubContext<DebateHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(hubClients);
        hubClients.Group(DebateHub.SessionGroupName(sessionId)).Returns(proxy);
        var sut = new SignalREventBroadcaster(hubContext, NullLogger<SignalREventBroadcaster>.Instance);

        await sut.TruthMapPatchedAsync(sessionId, patch, version: 3, cancellationToken);

        await proxy.Received(1).SendCoreAsync(
            "TruthMapPatch",
            Arg.Is<object?[]>(args =>
                args.Length == 1
                && HasProperty(args[0], "SessionId", sessionId)
                && HasProperty(args[0], "Version", 3)
                && HasProperty(args[0], "Patch", patch)),
            cancellationToken);
    }

    [Fact]
    public async Task TranscriptMessageAppendedAsync_SendsTranscriptEvent_ToSessionGroup()
    {
        var sessionId = Guid.NewGuid();
        var message = new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Type = TranscriptMessageType.Agent,
            AgentId = "moderator",
            Content = "Use customer discovery interviews.",
            Round = 1,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var hubContext = Substitute.For<IHubContext<DebateHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(hubClients);
        hubClients.Group(DebateHub.SessionGroupName(sessionId)).Returns(proxy);
        var sut = new SignalREventBroadcaster(hubContext, NullLogger<SignalREventBroadcaster>.Instance);

        await sut.TranscriptMessageAppendedAsync(sessionId, message, cancellationToken);

        await proxy.Received(1).SendCoreAsync(
            "TranscriptMessage",
            Arg.Is<object?[]>(args =>
                args.Length == 1
                && HasProperty(args[0], "Id", message.Id)
                && HasProperty(args[0], "Type", "agent")
                && HasProperty(args[0], "AgentId", "moderator")
                && HasProperty(args[0], "Content", message.Content)
                && HasProperty(args[0], "Round", 1)
                && HasProperty(args[0], "IsStreaming", false)),
            cancellationToken);
    }

    [Fact]
    public async Task RoundProgressAsync_DoesNotThrow_WhenSendFails()
    {
        var sessionId = Guid.NewGuid();
        var hubContext = Substitute.For<IHubContext<DebateHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(hubClients);
        hubClients.Group(DebateHub.SessionGroupName(sessionId)).Returns(proxy);
        proxy.SendCoreAsync("RoundProgress", Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("transport down"));
        var sut = new SignalREventBroadcaster(hubContext, NullLogger<SignalREventBroadcaster>.Instance);

        var act = () => sut.RoundProgressAsync(sessionId, SessionPhase.Construction, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TruthMapPatchedAsync_DoesNotThrow_WhenSendFails()
    {
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch
        {
            Meta = new PatchMeta
            {
                Agent = "claude_agent",
                SessionId = sessionId
            }
        };
        var hubContext = Substitute.For<IHubContext<DebateHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(hubClients);
        hubClients.Group(DebateHub.SessionGroupName(sessionId)).Returns(proxy);
        proxy.SendCoreAsync("TruthMapPatch", Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("transport down"));
        var sut = new SignalREventBroadcaster(hubContext, NullLogger<SignalREventBroadcaster>.Instance);

        var act = () => sut.TruthMapPatchedAsync(sessionId, patch, version: 1, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TranscriptMessageAppendedAsync_DoesNotThrow_WhenSendFails()
    {
        var sessionId = Guid.NewGuid();
        var message = new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Type = TranscriptMessageType.System,
            Content = "Round complete",
            Round = 1,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var hubContext = Substitute.For<IHubContext<DebateHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(hubClients);
        hubClients.Group(DebateHub.SessionGroupName(sessionId)).Returns(proxy);
        proxy.SendCoreAsync("TranscriptMessage", Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("transport down"));
        var sut = new SignalREventBroadcaster(hubContext, NullLogger<SignalREventBroadcaster>.Instance);

        var act = () => sut.TranscriptMessageAppendedAsync(sessionId, message, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static bool HasProperty(object? instance, string propertyName, object? expectedValue)
    {
        instance.Should().NotBeNull();
        var property = instance!.GetType().GetProperty(propertyName);
        property.Should().NotBeNull($"payload should include '{propertyName}'");
        var actual = property!.GetValue(instance);
        return Equals(actual, expectedValue);
    }
}
