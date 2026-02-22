using Agon.Infrastructure.SignalR;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Agon.Infrastructure.Tests.SignalR;

public class DebateHubTests
{
    [Fact]
    public async Task JoinSession_AddsConnectionToSessionGroup_WhenSessionIdIsValid()
    {
        var sessionId = Guid.NewGuid();
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        var groups = Substitute.For<IGroupManager>();
        var sut = CreateHub(context, groups);

        await sut.JoinSession(sessionId.ToString());

        await groups.Received(1).AddToGroupAsync(
            "conn-1",
            DebateHub.SessionGroupName(sessionId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinSession_ThrowsHubException_WhenSessionIdIsInvalid()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        var groups = Substitute.For<IGroupManager>();
        var sut = CreateHub(context, groups);

        var act = () => sut.JoinSession("not-a-guid");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Invalid session id.");
        await groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinSession_Rethrows_WhenGroupRegistrationFails()
    {
        var sessionId = Guid.NewGuid();
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        var groups = Substitute.For<IGroupManager>();
        groups.AddToGroupAsync(
                "conn-1",
                DebateHub.SessionGroupName(sessionId),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));
        var sut = CreateHub(context, groups);

        var act = () => sut.JoinSession(sessionId.ToString());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    [Fact]
    public async Task LeaveSession_RemovesConnectionFromSessionGroup_WhenSessionIdIsValid()
    {
        var sessionId = Guid.NewGuid();
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        var groups = Substitute.For<IGroupManager>();
        var sut = CreateHub(context, groups);

        await sut.LeaveSession(sessionId.ToString());

        await groups.Received(1).RemoveFromGroupAsync(
            "conn-1",
            DebateHub.SessionGroupName(sessionId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveSession_DoesNothing_WhenSessionIdIsInvalid()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        var groups = Substitute.For<IGroupManager>();
        var sut = CreateHub(context, groups);

        await sut.LeaveSession("not-a-guid");

        await groups.DidNotReceive().RemoveFromGroupAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveSession_Rethrows_WhenGroupRemovalFails()
    {
        var sessionId = Guid.NewGuid();
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        var groups = Substitute.For<IGroupManager>();
        groups.RemoveFromGroupAsync(
                "conn-1",
                DebateHub.SessionGroupName(sessionId),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));
        var sut = CreateHub(context, groups);

        var act = () => sut.LeaveSession(sessionId.ToString());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    [Fact]
    public async Task OnDisconnectedAsync_Completes_WhenExceptionIsNull()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        var groups = Substitute.For<IGroupManager>();
        var sut = CreateHub(context, groups);

        var act = () => sut.OnDisconnectedAsync(null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnDisconnectedAsync_Completes_WhenExceptionIsProvided()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        var groups = Substitute.For<IGroupManager>();
        var sut = CreateHub(context, groups);

        var act = () => sut.OnDisconnectedAsync(new InvalidOperationException("socket closed"));

        await act.Should().NotThrowAsync();
    }

    private static DebateHub CreateHub(HubCallerContext context, IGroupManager groups)
    {
        return new DebateHub(NullLogger<DebateHub>.Instance)
        {
            Context = context,
            Groups = groups
        };
    }
}
