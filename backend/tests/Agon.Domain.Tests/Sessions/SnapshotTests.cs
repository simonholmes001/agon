using Agon.Domain.Snapshots;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.Sessions;

public class SnapshotTests
{
    [Fact]
    public void SessionSnapshot_CanBeCreated_WithAllFields()
    {
        var sessionId = Guid.NewGuid();
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Test idea";

        var snapshot = SessionSnapshot.Create(sessionId, round: 1, map);

        snapshot.SnapshotId.Should().NotBe(Guid.Empty);
        snapshot.SessionId.Should().Be(sessionId);
        snapshot.Round.Should().Be(1);
        snapshot.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        snapshot.TruthMapHash.Should().NotBeNullOrEmpty();
        snapshot.TruthMap.Should().NotBeSameAs(map);
        snapshot.TruthMap.CoreIdea.Should().Be("Test idea");
    }

    [Fact]
    public void SessionSnapshot_TruthMap_IsImmutableCopy()
    {
        var sessionId = Guid.NewGuid();
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Initial idea";

        var snapshot = SessionSnapshot.Create(sessionId, round: 1, map);

        map.CoreIdea = "Mutated after snapshot";

        snapshot.TruthMap.CoreIdea.Should().Be("Initial idea");
    }

    [Fact]
    public void SessionSnapshot_DifferentMaps_ProduceDifferentHashes()
    {
        var sessionId = Guid.NewGuid();

        var map1 = TruthMapState.CreateNew(sessionId);
        map1.CoreIdea = "Idea A";
        var snapshot1 = SessionSnapshot.Create(sessionId, round: 1, map1);

        var map2 = TruthMapState.CreateNew(sessionId);
        map2.CoreIdea = "Idea B";
        var snapshot2 = SessionSnapshot.Create(sessionId, round: 1, map2);

        snapshot1.TruthMapHash.Should().NotBe(snapshot2.TruthMapHash);
    }

    [Fact]
    public void SessionSnapshot_Hash_ChangesWhenDeepFieldsChange()
    {
        var sessionId = Guid.NewGuid();

        var map1 = TruthMapState.CreateNew(sessionId);
        map1.CoreIdea = "Consistent idea";
        map1.Constraints.Budget = "100k";
        map1.Claims.Add(new Claim
        {
            Id = "c1",
            Agent = "product_strategist",
            Round = 1,
            Text = "Claim",
            Confidence = 0.8f,
            DerivedFrom = ["a1"],
            ChallengedBy = ["r1"]
        });

        var map2 = map1.DeepCopy();
        map2.Constraints.Budget = "200k";

        var snapshot1 = SessionSnapshot.Create(sessionId, round: 1, map1);
        var snapshot2 = SessionSnapshot.Create(sessionId, round: 1, map2);

        snapshot1.TruthMapHash.Should().NotBe(snapshot2.TruthMapHash);
    }

    [Fact]
    public void SessionSnapshot_SameMap_ProducesSameHash()
    {
        var sessionId = Guid.NewGuid();
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Consistent idea";

        var snapshot1 = SessionSnapshot.Create(sessionId, round: 1, map);
        var snapshot2 = SessionSnapshot.Create(sessionId, round: 1, map);

        snapshot1.TruthMapHash.Should().Be(snapshot2.TruthMapHash);
    }

    [Fact]
    public void ForkRequest_CanBeCreated_WithAllFields()
    {
        var parentSessionId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();

        var fork = new ForkRequest
        {
            ParentSessionId = parentSessionId,
            SnapshotId = snapshotId,
            Label = "What if budget is halved?",
            InitialPatches = new List<TruthMapPatch>()
        };

        fork.ParentSessionId.Should().Be(parentSessionId);
        fork.SnapshotId.Should().Be(snapshotId);
        fork.Label.Should().Be("What if budget is halved?");
        fork.InitialPatches.Should().BeEmpty();
    }

    [Fact]
    public void ForkRequest_InitialPatches_DefaultsToEmptyList()
    {
        var fork = new ForkRequest
        {
            ParentSessionId = Guid.NewGuid(),
            SnapshotId = Guid.NewGuid(),
            Label = "Test fork"
        };

        fork.InitialPatches.Should().BeEmpty();
    }
}
