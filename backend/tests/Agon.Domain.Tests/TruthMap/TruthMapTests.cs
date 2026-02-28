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
            Agent = "gpt_agent",
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
            Agent = "claude_agent",
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
            Owner = "gpt_agent"
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
            RaisedBy = "moderator"
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
            Agent = "gpt_agent",
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

    [Fact]
    public void TruthMap_FindEntity_FindsPersonaById()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Personas.Add(new Persona
        {
            Id = "p1",
            Name = "Founder",
            Description = "Primary decision maker"
        });

        map.EntityExists("p1").Should().BeTrue();
    }

    [Fact]
    public void TruthMap_DeepCopy_CreatesIndependentCopyOfAllMutableCollections()
    {
        var sessionId = Guid.NewGuid();
        var map = TruthMapState.CreateNew(sessionId);
        map.Round = 2;
        map.CoreIdea = "Original idea";
        map.Constraints.Budget = "$10k";
        map.Constraints.Timeline = "90 days";
        map.Constraints.TechStack.Add("dotnet");
        map.Constraints.NonNegotiables.Add("No outsourcing");
        map.SuccessMetrics.Add("Sign 10 customers");
        map.Personas.Add(new Persona { Id = "p1", Name = "Founder", Description = "Owns budget" });
        map.Claims.Add(new Claim
        {
            Id = "c1",
            Agent = "gpt_agent",
            Round = 2,
            Text = "Ship a narrow MVP",
            Confidence = 0.6f,
            DerivedFrom = ["a1"],
            ChallengedBy = ["r1"]
        });
        map.Assumptions.Add(new Assumption
        {
            Id = "a1",
            Text = "Users need async collaboration",
            ValidationStep = "Interview 10 users",
            DerivedFrom = ["c1"]
        });
        map.Decisions.Add(new Decision
        {
            Id = "d1",
            Text = "Use web-first delivery",
            Rationale = "Fastest to market",
            Owner = "gpt_agent",
            DerivedFrom = ["c1"]
        });
        map.Risks.Add(new Risk
        {
            Id = "r1",
            Agent = "claude_agent",
            Text = "Scope creep",
            Mitigation = "Weekly scope review",
            DerivedFrom = ["d1"]
        });
        map.OpenQuestions.Add(new OpenQuestion { Id = "q1", Text = "What is pricing?", RaisedBy = "moderator" });
        map.Evidence.Add(new Evidence
        {
            Id = "e1",
            Title = "User interview notes",
            Source = "https://example.com/research",
            Summary = "Users prioritize speed",
            Supports = ["c1"],
            Contradicts = ["r1"]
        });
        map.ConfidenceTransitions.Add(new ConfidenceTransition
        {
            ClaimId = "c1",
            Round = 2,
            From = 0.5f,
            To = 0.6f,
            Reason = ConfidenceTransitionReason.EvidenceCorroboration
        });
        map.IncrementVersion();

        var copy = map.DeepCopy();

        copy.Should().NotBeSameAs(map);
        copy.SessionId.Should().Be(sessionId);
        copy.Version.Should().Be(1);
        copy.Round.Should().Be(2);
        copy.CoreIdea.Should().Be("Original idea");

        copy.Constraints.Should().NotBeSameAs(map.Constraints);
        copy.SuccessMetrics.Should().NotBeSameAs(map.SuccessMetrics);
        copy.Personas.Should().NotBeSameAs(map.Personas);
        copy.Claims.Should().NotBeSameAs(map.Claims);
        copy.Assumptions.Should().NotBeSameAs(map.Assumptions);
        copy.Decisions.Should().NotBeSameAs(map.Decisions);
        copy.Risks.Should().NotBeSameAs(map.Risks);
        copy.OpenQuestions.Should().NotBeSameAs(map.OpenQuestions);
        copy.Evidence.Should().NotBeSameAs(map.Evidence);
        copy.Convergence.Should().NotBeSameAs(map.Convergence);
        copy.ConfidenceTransitions.Should().NotBeSameAs(map.ConfidenceTransitions);

        map.Constraints.Budget = "$20k";
        map.SuccessMetrics[0] = "Sign 20 customers";
        map.Personas[0].Name = "Changed";
        map.Claims[0].Text = "Changed claim";
        map.Assumptions[0].Text = "Changed assumption";
        map.Decisions[0].Text = "Changed decision";
        map.Risks[0].Text = "Changed risk";
        map.OpenQuestions[0].Text = "Changed question";
        map.Evidence[0] = new Evidence
        {
            Id = map.Evidence[0].Id,
            Title = "Changed evidence",
            Source = map.Evidence[0].Source,
            RetrievedAt = map.Evidence[0].RetrievedAt,
            Summary = map.Evidence[0].Summary,
            Supports = [.. map.Evidence[0].Supports],
            Contradicts = [.. map.Evidence[0].Contradicts]
        };
        map.Convergence.Overall = 0.95f;
        map.ConfidenceTransitions[0] = new ConfidenceTransition
        {
            ClaimId = map.ConfidenceTransitions[0].ClaimId,
            Round = map.ConfidenceTransitions[0].Round,
            From = map.ConfidenceTransitions[0].From,
            To = 0.1f,
            Reason = map.ConfidenceTransitions[0].Reason
        };

        copy.Constraints.Budget.Should().Be("$10k");
        copy.SuccessMetrics[0].Should().Be("Sign 10 customers");
        copy.Personas[0].Name.Should().Be("Founder");
        copy.Claims[0].Text.Should().Be("Ship a narrow MVP");
        copy.Assumptions[0].Text.Should().Be("Users need async collaboration");
        copy.Decisions[0].Text.Should().Be("Use web-first delivery");
        copy.Risks[0].Text.Should().Be("Scope creep");
        copy.OpenQuestions[0].Text.Should().Be("What is pricing?");
        copy.Evidence[0].Title.Should().Be("User interview notes");
        copy.Convergence.Overall.Should().NotBe(0.95f);
        copy.ConfidenceTransitions[0].To.Should().Be(0.6f);
    }
}
