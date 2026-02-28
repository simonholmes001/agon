using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.TruthMap;

public class EntityTests
{
    // --- Claim ---

    [Fact]
    public void Claim_CanBeCreated_WithRequiredProperties()
    {
        var claim = new Claim
        {
            Id = "c1",
            Agent = "gpt_agent",
            Round = 1,
            Text = "The MVP should focus on mobile-first.",
            Confidence = 0.8f
        };

        claim.Id.Should().Be("c1");
        claim.Agent.Should().Be("gpt_agent");
        claim.Round.Should().Be(1);
        claim.Text.Should().Be("The MVP should focus on mobile-first.");
        claim.Confidence.Should().Be(0.8f);
        claim.Status.Should().Be(ClaimStatus.Active);
        claim.DerivedFrom.Should().BeEmpty();
        claim.ChallengedBy.Should().BeEmpty();
    }

    [Fact]
    public void Claim_DerivedFrom_CanContainMultipleEntityIds()
    {
        var claim = new Claim
        {
            Id = "c2",
            Agent = "gpt_agent",
            Round = 1,
            Text = "Microservices are overkill for this scope.",
            Confidence = 0.7f,
            DerivedFrom = new List<string> { "c1", "a1" }
        };

        claim.DerivedFrom.Should().HaveCount(2);
        claim.DerivedFrom.Should().Contain("c1");
        claim.DerivedFrom.Should().Contain("a1");
    }

    [Fact]
    public void Claim_ChallengedBy_CanContainMultipleEntityIds()
    {
        var claim = new Claim
        {
            Id = "c1",
            Agent = "gpt_agent",
            Round = 1,
            Text = "Mobile-first is essential.",
            Confidence = 0.8f,
            ChallengedBy = new List<string> { "r1", "c3" }
        };

        claim.ChallengedBy.Should().HaveCount(2);
    }

    // --- Assumption ---

    [Fact]
    public void Assumption_CanBeCreated_WithRequiredProperties()
    {
        var assumption = new Assumption
        {
            Id = "a1",
            Text = "Users will pay for premium features.",
            ValidationStep = "Run a pricing survey with 100 target users."
        };

        assumption.Id.Should().Be("a1");
        assumption.Text.Should().NotBeEmpty();
        assumption.ValidationStep.Should().NotBeNullOrEmpty();
        assumption.Status.Should().Be(AssumptionStatus.Unvalidated);
        assumption.DerivedFrom.Should().BeEmpty();
    }

    [Fact]
    public void Assumption_ValidationStep_CanBeNull_BeforeRound2()
    {
        var assumption = new Assumption
        {
            Id = "a2",
            Text = "The market is growing.",
            ValidationStep = null
        };

        assumption.ValidationStep.Should().BeNull();
    }

    // --- Decision ---

    [Fact]
    public void Decision_CanBeCreated_WithRationale()
    {
        var decision = new Decision
        {
            Id = "d1",
            Text = "Use PostgreSQL for persistence.",
            Rationale = "Supports JSONB for Truth Map storage and pgvector for embeddings.",
            Owner = "gpt_agent",
            Binding = true
        };

        decision.Id.Should().Be("d1");
        decision.Rationale.Should().NotBeNullOrEmpty();
        decision.Owner.Should().Be("gpt_agent");
        decision.Binding.Should().BeTrue();
        decision.DerivedFrom.Should().BeEmpty();
    }

    // --- Risk ---

    [Fact]
    public void Risk_CanBeCreated_WithAllFields()
    {
        var risk = new Risk
        {
            Id = "r1",
            Text = "Token costs may exceed budget at scale.",
            Category = RiskCategory.Financial,
            Severity = Severity.High,
            Likelihood = Likelihood.Medium,
            Mitigation = "Implement per-session budget caps with graceful degradation.",
            Agent = "claude_agent"
        };

        risk.Id.Should().Be("r1");
        risk.Category.Should().Be(RiskCategory.Financial);
        risk.Severity.Should().Be(Severity.High);
        risk.Likelihood.Should().Be(Likelihood.Medium);
        risk.Agent.Should().Be("claude_agent");
        risk.DerivedFrom.Should().BeEmpty();
    }

    [Fact]
    public void RiskCategory_ContainsAllExpectedCategories()
    {
        var categories = Enum.GetValues<RiskCategory>();

        categories.Should().Contain(RiskCategory.Market);
        categories.Should().Contain(RiskCategory.Technical);
        categories.Should().Contain(RiskCategory.Execution);
        categories.Should().Contain(RiskCategory.Security);
        categories.Should().Contain(RiskCategory.Financial);
        categories.Should().HaveCount(5);
    }

    // --- Evidence ---

    [Fact]
    public void Evidence_CanBeCreated_WithSupportsAndContradicts()
    {
        var evidence = new Evidence
        {
            Id = "e1",
            Title = "Mobile-first market report 2026",
            Source = "https://example.com/report",
            RetrievedAt = new DateTimeOffset(2026, 2, 21, 0, 0, 0, TimeSpan.Zero),
            Summary = "72% of target demographic uses mobile as primary device.",
            Supports = new List<string> { "c1" },
            Contradicts = new List<string> { "c3" }
        };

        evidence.Supports.Should().Contain("c1");
        evidence.Contradicts.Should().Contain("c3");
    }

    // --- OpenQuestion ---

    [Fact]
    public void OpenQuestion_CanBeBlocking()
    {
        var question = new OpenQuestion
        {
            Id = "q1",
            Text = "What is the target launch market?",
            Blocking = true,
            RaisedBy = "moderator"
        };

        question.Blocking.Should().BeTrue();
        question.RaisedBy.Should().Be("moderator");
    }

    [Fact]
    public void OpenQuestion_DefaultsToNonBlocking()
    {
        var question = new OpenQuestion
        {
            Id = "q2",
            Text = "Should we consider B2B?",
            RaisedBy = "gemini_agent"
        };

        question.Blocking.Should().BeFalse();
    }

    // --- Persona ---

    [Fact]
    public void Persona_CanBeCreated()
    {
        var persona = new Persona
        {
            Id = "p1",
            Name = "Solo Founder",
            Description = "Technical founder building their first product."
        };

        persona.Id.Should().Be("p1");
        persona.Name.Should().Be("Solo Founder");
        persona.Description.Should().NotBeEmpty();
    }

    // --- Constraints ---

    [Fact]
    public void Constraints_CanBeCreated_WithAllFields()
    {
        var constraints = new Constraints
        {
            Budget = "$50k seed",
            Timeline = "3 months to MVP",
            TechStack = new List<string> { "Next.js", ".NET" },
            NonNegotiables = new List<string> { "Must work offline" }
        };

        constraints.Budget.Should().Be("$50k seed");
        constraints.Timeline.Should().Be("3 months to MVP");
        constraints.TechStack.Should().HaveCount(2);
        constraints.NonNegotiables.Should().HaveCount(1);
    }

    [Fact]
    public void Constraints_DefaultsToEmptyLists()
    {
        var constraints = new Constraints();

        constraints.TechStack.Should().BeEmpty();
        constraints.NonNegotiables.Should().BeEmpty();
    }

    // --- Convergence ---

    [Fact]
    public void Convergence_CanBeCreated_WithAllDimensions()
    {
        var convergence = new Convergence
        {
            ClaritySpecificity = 0.8f,
            Feasibility = 0.7f,
            RiskCoverage = 0.6f,
            AssumptionExplicitness = 0.75f,
            Coherence = 0.85f,
            Actionability = 0.7f,
            EvidenceQuality = 0.5f,
            Overall = 0.72f,
            Threshold = 0.75f,
            Status = ConvergenceStatus.InProgress
        };

        convergence.ClaritySpecificity.Should().Be(0.8f);
        convergence.Overall.Should().Be(0.72f);
        convergence.Status.Should().Be(ConvergenceStatus.InProgress);
    }

    [Fact]
    public void ConvergenceStatus_ContainsAllExpectedStatuses()
    {
        var statuses = Enum.GetValues<ConvergenceStatus>();

        statuses.Should().Contain(ConvergenceStatus.InProgress);
        statuses.Should().Contain(ConvergenceStatus.Converged);
        statuses.Should().Contain(ConvergenceStatus.GapsRemain);
        statuses.Should().HaveCount(3);
    }

    // --- ConfidenceTransition ---

    [Fact]
    public void ConfidenceTransition_CanBeCreated()
    {
        var transition = new ConfidenceTransition
        {
            ClaimId = "c1",
            Round = 2,
            From = 0.8f,
            To = 0.65f,
            Reason = ConfidenceTransitionReason.ChallengedNoDefense
        };

        transition.ClaimId.Should().Be("c1");
        transition.From.Should().Be(0.8f);
        transition.To.Should().Be(0.65f);
        transition.Reason.Should().Be(ConfidenceTransitionReason.ChallengedNoDefense);
    }

    [Fact]
    public void ConfidenceTransitionReason_ContainsAllExpectedReasons()
    {
        var reasons = Enum.GetValues<ConfidenceTransitionReason>();

        reasons.Should().Contain(ConfidenceTransitionReason.ChallengedNoDefense);
        reasons.Should().Contain(ConfidenceTransitionReason.EvidenceCorroboration);
        reasons.Should().Contain(ConfidenceTransitionReason.ManualOverride);
        reasons.Should().HaveCount(3);
    }
}
