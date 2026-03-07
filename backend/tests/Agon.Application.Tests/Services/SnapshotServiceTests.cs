using Agon.Application.Interfaces;
using Agon.Application.Services;
using Agon.Domain.Snapshots;
using Agon.Domain.TruthMap;
using FluentAssertions;
using NSubstitute;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Application.Tests.Services;

public class SnapshotServiceTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    private static TruthMapModel EmptyMap() => TruthMapModel.Empty(SessionId);

    private static ISnapshotStore StubStore(IReadOnlyList<SessionSnapshot>? snapshots = null)
    {
        var store = Substitute.For<ISnapshotStore>();
        store.ListBySessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(snapshots ?? (IReadOnlyList<SessionSnapshot>)[]));
        store.SaveAsync(Arg.Any<SessionSnapshot>(), Arg.Any<CancellationToken>())
             .Returns(call => Task.FromResult(call.ArgAt<SessionSnapshot>(0)));
        return store;
    }

    // ── GetLatestAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestAsync_ReturnsSnapshotWithHighestRound()
    {
        var snap1 = SessionSnapshot.Create(EmptyMap(), 1);
        var snap3 = SessionSnapshot.Create(EmptyMap(), 3);
        var snap2 = SessionSnapshot.Create(EmptyMap(), 2);

        var store = Substitute.For<ISnapshotStore>();
        store.ListBySessionAsync(SessionId, Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IReadOnlyList<SessionSnapshot>>([snap1, snap3, snap2]));

        var svc = new SnapshotService(store);
        var result = await svc.GetLatestAsync(SessionId);

        result.Should().NotBeNull();
        result!.Round.Should().Be(3);
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsNullWhenNoSnapshots()
    {
        var svc = new SnapshotService(StubStore([]));
        var result = await svc.GetLatestAsync(SessionId);
        result.Should().BeNull();
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_DelegatesToStore()
    {
        var snap = SessionSnapshot.Create(EmptyMap(), 1);
        var store = Substitute.For<ISnapshotStore>();
        store.GetAsync(snap.SnapshotId, Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<SessionSnapshot?>(snap));

        var svc = new SnapshotService(store);
        var result = await svc.GetByIdAsync(snap.SnapshotId);

        result.Should().NotBeNull();
        result!.SnapshotId.Should().Be(snap.SnapshotId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullWhenNotFound()
    {
        var store = Substitute.For<ISnapshotStore>();
        store.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<SessionSnapshot?>(null));

        var svc = new SnapshotService(store);
        var result = await svc.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    // ── VerifyIntegrityAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task VerifyIntegrityAsync_ReturnsTrueForValidSnapshot()
    {
        var snap = SessionSnapshot.Create(EmptyMap(), 1);
        var store = Substitute.For<ISnapshotStore>();
        store.GetAsync(snap.SnapshotId, Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<SessionSnapshot?>(snap));

        var svc = new SnapshotService(store);
        var result = await svc.VerifyIntegrityAsync(snap.SnapshotId);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyIntegrityAsync_ReturnsFalseWhenNotFound()
    {
        var store = Substitute.For<ISnapshotStore>();
        store.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<SessionSnapshot?>(null));

        var svc = new SnapshotService(store);
        var result = await svc.VerifyIntegrityAsync(Guid.NewGuid());
        result.Should().BeFalse();
    }

    // ── ListAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsSnapshotsOrderedByRoundAscending()
    {
        var snap2 = SessionSnapshot.Create(EmptyMap(), 2);
        var snap1 = SessionSnapshot.Create(EmptyMap(), 1);
        var snap3 = SessionSnapshot.Create(EmptyMap(), 3);

        var store = Substitute.For<ISnapshotStore>();
        store.ListBySessionAsync(SessionId, Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IReadOnlyList<SessionSnapshot>>([snap2, snap1, snap3]));

        var svc = new SnapshotService(store);
        var result = await svc.ListAsync(SessionId);

        result.Select(s => s.Round).Should().Equal(1, 2, 3);
    }

    // ── BuildForkContextAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task BuildForkContextAsync_ReturnsSnapshotTruthMapForValidSnapshot()
    {
        var map = EmptyMap() with { CoreIdea = "Fork idea", Version = 3 };
        var snap = SessionSnapshot.Create(map, 2);

        var store = Substitute.For<ISnapshotStore>();
        store.GetAsync(snap.SnapshotId, Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<SessionSnapshot?>(snap));

        var forkRequest = new ForkRequest(SessionId, snap.SnapshotId, [], "Halved budget scenario");

        var svc = new SnapshotService(store);
        var result = await svc.BuildForkContextAsync(forkRequest);

        result.Should().NotBeNull();
        result!.CoreIdea.Should().Be("Fork idea");
        result.Version.Should().Be(3);
    }

    [Fact]
    public async Task BuildForkContextAsync_ReturnsNullWhenSnapshotNotFound()
    {
        var store = Substitute.For<ISnapshotStore>();
        store.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<SessionSnapshot?>(null));

        var svc = new SnapshotService(store);
        var forkRequest = new ForkRequest(SessionId, Guid.NewGuid(), [], "Test");
        var result = await svc.BuildForkContextAsync(forkRequest);
        result.Should().BeNull();
    }
}
