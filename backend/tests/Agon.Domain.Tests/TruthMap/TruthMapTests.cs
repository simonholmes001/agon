using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.TruthMap;

public class TruthMapTests
{
    [Fact]
    public void NewTruthMap_HasEmptyCollections()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());

        map.Version.Should().Be(0);
        map.Round.Should().Be(0);
        map.CoreIdea.Should().BeEmpty();
        map.Claims.Should().BeEmpty();
        map.Assumptions.Should().BeEmpty();
        map.Decisions.Should().BeEmpty();
        map.Risks.Should().BeEmpty();
        map.OpenQuestions.Should().BeEmpty();
        map.Evidence.Should().BeEmpty();
        map.Personas.Should().BeEmpty();
        map.ConfidenceTransitions.Should().BeEmpty();
        map.Constraints.Should().NotBeNull();
        map.Convergence.Should().NotBeNull();
    }

    [Fact]
    public void NewTruthMap_HasSessionId()
    {
        var sessionId = Guid.NewGuid();
        var map = TruthMapState.CreateNew(sessionId);

        map.SessionId.Should().Be(sessionId);
    }

    [Fact]
    public void TruthMap_IncrementVersion_BumpsVersion()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());

        map.IncrementVersion();

        map.Version.Should().Be(1);
    }

    [Fact]
    public void TruthMap_FindEntity_FindsClaimById()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim
        {
            Id = "c1",
            Agent = "product_strategist",
            Round = 1,
            Text = "MVP should be mobile-first.",
            Confidence = 0.8f
        });

        var found = map.EntityExists("c1");

        found.Should().BeTrue();
    }

    [Fact]
    public void TruthMap_FindEntity_ReturnsFalseForMissingId()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());

        map.EntityExists("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void TruthMap_FindEntity_FindsRiskById()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Risks.Add(new Risk
        {
            Id = "r1",
            Agent = "contrarian",
            Text = "Token costs may be prohibitive."
        });

        map.EntityExists("r1").Should().BeTrue();
    }

    [Fact]
    public void TruthMap_FindEntity_FindsAssumptionById()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Assumptions.Add(new Assumption
        {
            Id = "a1",
            Text = "Users want this."
        });

        map.EntityExists("a1").Should().BeTrue();
    }

    [Fact]
    public void TruthMap_FindEntity_FindsDecisionById()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Decisions.Add(new Decision
        {
            Id = "d1",
            Text = "Use Postgres.",
            Rationale = "JSONB support.",
            Owner = "technical_architect"
        });

        map.EntityExists("d1").Should().BeTrue();
    }

    [Fact]
    public void TruthMap_FindEntity_FindsEvidenceById()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Evidence.Add(new Evidence
        {
            Id = "e1",
            Title = "Study",
            Source = "https://example.com"
        });

        map.EntityExists("e1").Should().BeTrue();
    }

    [Fact]
    public void TruthMap_FindEntity_FindsOpenQuestionById()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.OpenQuestions.Add(new OpenQuestion
        {
            Id = "q1",
            Text = "What market?",
            RaisedBy = "socratic_clarifier"
        });

        map.EntityExists("q1").Should().BeTrue();
    }

    [Fact]
    public void TruthMap_FindClaimById_ReturnsClaimOrNull()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        var claim = new Claim
        {
            Id = "c1",
            Agent = "product_strategist",
            Round = 1,
            Text = "Test",
            Confidence = 0.5f
        };
        map.Claims.Add(claim);

        map.FindClaim("c1").Should().BeSameAs(claim);
        map.FindClaim("missing").Should().BeNull();
    }

    [Fact]
    public void TruthMap_SuccessMetrics_DefaultsToEmptyList()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());

        map.SuccessMetrics.Should().BeEmpty();
    }
}
