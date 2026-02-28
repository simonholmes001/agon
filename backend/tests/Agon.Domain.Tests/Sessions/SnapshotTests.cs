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
            Agent = "gpt_agent",
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
    public void SessionSnapshot_Hash_IsStableRegardlessOfCollectionInsertionOrder()
    {
        var sessionId = Guid.NewGuid();

        var mapA = TruthMapState.CreateNew(sessionId);
        mapA.CoreIdea = "Canonical hash test";
        mapA.Constraints.TechStack.AddRange(["dotnet", "postgres"]);
        mapA.Constraints.NonNegotiables.AddRange(["privacy", "speed"]);
        mapA.SuccessMetrics.AddRange(["retention", "activation"]);
        mapA.Personas.AddRange(
        [
            new Persona { Id = "p2", Name = "Buyer", Description = "Pays invoices" },
            new Persona { Id = "p1", Name = "User", Description = "Uses workflow daily" }
        ]);
        mapA.Claims.AddRange(
        [
            new Claim { Id = "c2", Agent = "claude_agent", Round = 1, Text = "Second claim", Confidence = 0.4f },
            new Claim { Id = "c1", Agent = "gpt_agent", Round = 1, Text = "First claim", Confidence = 0.7f }
        ]);
        mapA.Assumptions.AddRange(
        [
            new Assumption { Id = "a2", Text = "Assumption B" },
            new Assumption { Id = "a1", Text = "Assumption A" }
        ]);
        mapA.Decisions.AddRange(
        [
            new Decision { Id = "d2", Text = "Decision B", Rationale = "Because B", Owner = "pm" },
            new Decision { Id = "d1", Text = "Decision A", Rationale = "Because A", Owner = "pm" }
        ]);
        mapA.Risks.AddRange(
        [
            new Risk { Id = "r2", Agent = "claude_agent", Text = "Risk B" },
            new Risk { Id = "r1", Agent = "claude_agent", Text = "Risk A" }
        ]);
        mapA.OpenQuestions.AddRange(
        [
            new OpenQuestion { Id = "q2", Text = "Question B", RaisedBy = "moderator" },
            new OpenQuestion { Id = "q1", Text = "Question A", RaisedBy = "moderator" }
        ]);
        mapA.Evidence.AddRange(
        [
            new Evidence
            {
                Id = "e2",
                Title = "Evidence B",
                Source = "https://example.com/b",
                RetrievedAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.FromHours(1)),
                Summary = "B summary",
                Supports = ["c2", "c1"],
                Contradicts = ["r2", "r1"]
            },
            new Evidence
            {
                Id = "e1",
                Title = "Evidence A",
                Source = "https://example.com/a",
                RetrievedAt = new DateTimeOffset(2026, 2, 1, 11, 0, 0, TimeSpan.FromHours(-5)),
                Summary = "A summary",
                Supports = ["c1", "c2"],
                Contradicts = ["r1", "r2"]
            }
        ]);
        mapA.ConfidenceTransitions.AddRange(
        [
            new ConfidenceTransition
            {
                ClaimId = "c2",
                Round = 1,
                From = 0.4f,
                To = 0.3f,
                Reason = ConfidenceTransitionReason.ChallengedNoDefense
            },
            new ConfidenceTransition
            {
                ClaimId = "c1",
                Round = 1,
                From = 0.7f,
                To = 0.8f,
                Reason = ConfidenceTransitionReason.EvidenceCorroboration
            }
        ]);

        var mapB = TruthMapState.CreateNew(sessionId);
        mapB.CoreIdea = mapA.CoreIdea;
        mapB.Constraints.TechStack.AddRange(["postgres", "dotnet"]);
        mapB.Constraints.NonNegotiables.AddRange(["speed", "privacy"]);
        mapB.SuccessMetrics.AddRange(["activation", "retention"]);
        mapB.Personas.AddRange(mapA.Personas.AsEnumerable().Reverse());
        mapB.Claims.AddRange(mapA.Claims.AsEnumerable().Reverse());
        mapB.Assumptions.AddRange(mapA.Assumptions.AsEnumerable().Reverse());
        mapB.Decisions.AddRange(mapA.Decisions.AsEnumerable().Reverse());
        mapB.Risks.AddRange(mapA.Risks.AsEnumerable().Reverse());
        mapB.OpenQuestions.AddRange(mapA.OpenQuestions.AsEnumerable().Reverse());
        mapB.Evidence.AddRange(mapA.Evidence.AsEnumerable().Reverse());
        mapB.ConfidenceTransitions.AddRange(mapA.ConfidenceTransitions.AsEnumerable().Reverse());

        var snapshotA = SessionSnapshot.Create(sessionId, round: 1, mapA);
        var snapshotB = SessionSnapshot.Create(sessionId, round: 1, mapB);

        snapshotA.TruthMapHash.Should().Be(snapshotB.TruthMapHash);
    }

    [Fact]
    public void SessionSnapshot_Hash_ChangesWhenEvidenceTimestampChanges()
    {
        var sessionId = Guid.NewGuid();
        var mapA = TruthMapState.CreateNew(sessionId);
        var mapB = TruthMapState.CreateNew(sessionId);
        var evidenceId = "e1";

        mapA.Evidence.Add(new Evidence
        {
            Id = evidenceId,
            Title = "Timestamp test",
            Source = "https://example.com",
            RetrievedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)
        });
        mapB.Evidence.Add(new Evidence
        {
            Id = evidenceId,
            Title = "Timestamp test",
            Source = "https://example.com",
            RetrievedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 1, TimeSpan.Zero)
        });

        var snapshotA = SessionSnapshot.Create(sessionId, 1, mapA);
        var snapshotB = SessionSnapshot.Create(sessionId, 1, mapB);

        snapshotA.TruthMapHash.Should().NotBe(snapshotB.TruthMapHash);
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
