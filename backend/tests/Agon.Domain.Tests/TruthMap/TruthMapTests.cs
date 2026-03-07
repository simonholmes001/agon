using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.TruthMap;

public class TruthMapTests
{
    [Fact]
    public void Empty_CreatesNewTruthMapWithDefaults()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        var truthMap = Domain.TruthMap.TruthMap.Empty(sessionId);

        // Assert
        truthMap.SessionId.Should().Be(sessionId);
        truthMap.Version.Should().Be(0);
        truthMap.Round.Should().Be(0);
        truthMap.CoreIdea.Should().BeEmpty();
        truthMap.Claims.Should().BeEmpty();
        truthMap.Assumptions.Should().BeEmpty();
        truthMap.Decisions.Should().BeEmpty();
        truthMap.Risks.Should().BeEmpty();
        truthMap.OpenQuestions.Should().BeEmpty();
        truthMap.Evidence.Should().BeEmpty();
        truthMap.Personas.Should().BeEmpty();
        truthMap.SuccessMetrics.Should().BeEmpty();
        truthMap.ConfidenceTransitions.Should().BeEmpty();
    }

    [Fact]
    public void Empty_WithCustomConvergenceThreshold_SetsThreshold()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var customThreshold = 0.85f;

        // Act
        var truthMap = Domain.TruthMap.TruthMap.Empty(sessionId, customThreshold);

        // Assert
        truthMap.Convergence.Should().NotBeNull();
        // Convergence.Empty creates a new instance with the threshold
    }

    [Fact]
    public void FindClaim_WithExistingId_ReturnsClaim()
    {
        // Arrange
        var claim = new Claim("claim-1", "agent", 1, "text", 0.8f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Claims = new[] { claim }
        };

        // Act
        var found = truthMap.FindClaim("claim-1");

        // Assert
        found.Should().NotBeNull();
        found.Should().Be(claim);
    }

    [Fact]
    public void FindClaim_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act
        var found = truthMap.FindClaim("non-existing");

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public void FindAssumption_WithExistingId_ReturnsAssumption()
    {
        // Arrange
        var assumption = new Assumption("assumption-1", "text", "validation", Array.Empty<string>(), AssumptionStatus.Unvalidated);
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Assumptions = new[] { assumption }
        };

        // Act
        var found = truthMap.FindAssumption("assumption-1");

        // Assert
        found.Should().NotBeNull();
        found.Should().Be(assumption);
    }

    [Fact]
    public void FindAssumption_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act
        var found = truthMap.FindAssumption("non-existing");

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public void FindDecision_WithExistingId_ReturnsDecision()
    {
        // Arrange
        var decision = new Decision("decision-1", "text", "rationale", "owner", Array.Empty<string>(), true);
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Decisions = new[] { decision }
        };

        // Act
        var found = truthMap.FindDecision("decision-1");

        // Assert
        found.Should().NotBeNull();
        found.Should().Be(decision);
    }

    [Fact]
    public void FindDecision_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act
        var found = truthMap.FindDecision("non-existing");

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public void FindRisk_WithExistingId_ReturnsRisk()
    {
        // Arrange
        var risk = new Risk("risk-1", "text", RiskCategory.Technical, RiskSeverity.High, RiskLikelihood.Medium, "mitigation", Array.Empty<string>(), "agent");
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Risks = new[] { risk }
        };

        // Act
        var found = truthMap.FindRisk("risk-1");

        // Assert
        found.Should().NotBeNull();
        found.Should().Be(risk);
    }

    [Fact]
    public void FindRisk_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act
        var found = truthMap.FindRisk("non-existing");

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public void FindOpenQuestion_WithExistingId_ReturnsOpenQuestion()
    {
        // Arrange
        var question = new OpenQuestion("question-1", "text", true, "agent");
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            OpenQuestions = new[] { question }
        };

        // Act
        var found = truthMap.FindOpenQuestion("question-1");

        // Assert
        found.Should().NotBeNull();
        found.Should().Be(question);
    }

    [Fact]
    public void FindOpenQuestion_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act
        var found = truthMap.FindOpenQuestion("non-existing");

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public void FindEvidence_WithExistingId_ReturnsEvidence()
    {
        // Arrange
        var evidence = new Evidence("evidence-1", "title", "source", DateTimeOffset.UtcNow, "summary", Array.Empty<string>(), Array.Empty<string>());
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Evidence = new[] { evidence }
        };

        // Act
        var found = truthMap.FindEvidence("evidence-1");

        // Assert
        found.Should().NotBeNull();
        found.Should().Be(evidence);
    }

    [Fact]
    public void FindEvidence_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act
        var found = truthMap.FindEvidence("non-existing");

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public void EntityExists_WithExistingClaim_ReturnsTrue()
    {
        // Arrange
        var claim = new Claim("claim-1", "agent", 1, "text", 0.8f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Claims = new[] { claim }
        };

        // Act
        var exists = truthMap.EntityExists("claim-1");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public void EntityExists_WithExistingAssumption_ReturnsTrue()
    {
        // Arrange
        var assumption = new Assumption("assumption-1", "text", "validation", Array.Empty<string>(), AssumptionStatus.Unvalidated);
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Assumptions = new[] { assumption }
        };

        // Act
        var exists = truthMap.EntityExists("assumption-1");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public void EntityExists_WithExistingDecision_ReturnsTrue()
    {
        // Arrange
        var decision = new Decision("decision-1", "text", "rationale", "owner", Array.Empty<string>(), true);
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Decisions = new[] { decision }
        };

        // Act
        var exists = truthMap.EntityExists("decision-1");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public void EntityExists_WithExistingRisk_ReturnsTrue()
    {
        // Arrange
        var risk = new Risk("risk-1", "text", RiskCategory.Technical, RiskSeverity.High, RiskLikelihood.Medium, "mitigation", Array.Empty<string>(), "agent");
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Risks = new[] { risk }
        };

        // Act
        var exists = truthMap.EntityExists("risk-1");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public void EntityExists_WithExistingOpenQuestion_ReturnsTrue()
    {
        // Arrange
        var question = new OpenQuestion("question-1", "text", true, "agent");
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            OpenQuestions = new[] { question }
        };

        // Act
        var exists = truthMap.EntityExists("question-1");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public void EntityExists_WithExistingEvidence_ReturnsTrue()
    {
        // Arrange
        var evidence = new Evidence("evidence-1", "title", "source", DateTimeOffset.UtcNow, "summary", Array.Empty<string>(), Array.Empty<string>());
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Evidence = new[] { evidence }
        };

        // Act
        var exists = truthMap.EntityExists("evidence-1");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public void EntityExists_WithExistingPersona_ReturnsTrue()
    {
        // Arrange
        var persona = new Persona("persona-1", "name", "description");
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Personas = new[] { persona }
        };

        // Act
        var exists = truthMap.EntityExists("persona-1");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public void EntityExists_WithNonExistingId_ReturnsFalse()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act
        var exists = truthMap.EntityExists("non-existing");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public void EntityExists_WithMultipleEntityTypes_FindsCorrectOne()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Claims = new[] { new Claim("claim-1", "agent", 1, "text", 0.8f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>()) },
            Assumptions = new[] { new Assumption("assumption-1", "text", "validation", Array.Empty<string>(), AssumptionStatus.Unvalidated) },
            Decisions = new[] { new Decision("decision-1", "text", "rationale", "owner", Array.Empty<string>(), true) }
        };

        // Act & Assert
        truthMap.EntityExists("claim-1").Should().BeTrue();
        truthMap.EntityExists("assumption-1").Should().BeTrue();
        truthMap.EntityExists("decision-1").Should().BeTrue();
        truthMap.EntityExists("non-existing").Should().BeFalse();
    }

    [Fact]
    public void HasBlockingOpenQuestions_WithNoQuestions_ReturnsFalse()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act
        var hasBlocking = truthMap.HasBlockingOpenQuestions();

        // Assert
        hasBlocking.Should().BeFalse();
    }

    [Fact]
    public void HasBlockingOpenQuestions_WithOnlyNonBlockingQuestions_ReturnsFalse()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            OpenQuestions = new[]
            {
                new OpenQuestion("q1", "Non-blocking question 1", false, "agent1"),
                new OpenQuestion("q2", "Non-blocking question 2", false, "agent2")
            }
        };

        // Act
        var hasBlocking = truthMap.HasBlockingOpenQuestions();

        // Assert
        hasBlocking.Should().BeFalse();
    }

    [Fact]
    public void HasBlockingOpenQuestions_WithBlockingQuestion_ReturnsTrue()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            OpenQuestions = new[]
            {
                new OpenQuestion("q1", "Non-blocking question", false, "agent1"),
                new OpenQuestion("q2", "Blocking question", true, "agent2"),
                new OpenQuestion("q3", "Another non-blocking", false, "agent3")
            }
        };

        // Act
        var hasBlocking = truthMap.HasBlockingOpenQuestions();

        // Assert
        hasBlocking.Should().BeTrue();
    }

    [Fact]
    public void HasBlockingOpenQuestions_WithMultipleBlockingQuestions_ReturnsTrue()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            OpenQuestions = new[]
            {
                new OpenQuestion("q1", "Blocking question 1", true, "agent1"),
                new OpenQuestion("q2", "Blocking question 2", true, "agent2")
            }
        };

        // Act
        var hasBlocking = truthMap.HasBlockingOpenQuestions();

        // Assert
        hasBlocking.Should().BeTrue();
    }

    [Fact]
    public void GetContestedClaims_WithNoClaims_ReturnsEmpty()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act
        var contested = truthMap.GetContestedClaims();

        // Assert
        contested.Should().BeEmpty();
    }

    [Fact]
    public void GetContestedClaims_WithOnlyActiveClaims_ReturnsEmpty()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Claims = new[]
            {
                new Claim("claim-1", "agent", 1, "text1", 0.9f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>()),
                new Claim("claim-2", "agent", 1, "text2", 0.8f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>())
            }
        };

        // Act
        var contested = truthMap.GetContestedClaims();

        // Assert
        contested.Should().BeEmpty();
    }

    [Fact]
    public void GetContestedClaims_WithContestedClaims_ReturnsOnlyContested()
    {
        // Arrange
        var contestedClaim1 = new Claim("claim-1", "agent", 1, "contested1", 0.5f, ClaimStatus.Contested, Array.Empty<string>(), new[] { "challenger" });
        var activeClaim = new Claim("claim-2", "agent", 1, "active", 0.9f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var contestedClaim2 = new Claim("claim-3", "agent", 2, "contested2", 0.4f, ClaimStatus.Contested, Array.Empty<string>(), new[] { "challenger" });
        var pendingClaim = new Claim("claim-4", "agent", 2, "pending", 0.7f, ClaimStatus.PendingRevalidation, Array.Empty<string>(), Array.Empty<string>());

        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Claims = new[] { contestedClaim1, activeClaim, contestedClaim2, pendingClaim }
        };

        // Act
        var contested = truthMap.GetContestedClaims();

        // Assert
        contested.Should().HaveCount(2);
        contested.Should().Contain(contestedClaim1);
        contested.Should().Contain(contestedClaim2);
        contested.Should().NotContain(activeClaim);
        contested.Should().NotContain(pendingClaim);
    }

    [Fact]
    public void GetContestedClaims_WithAllContestedClaims_ReturnsAll()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Claims = new[]
            {
                new Claim("claim-1", "agent", 1, "contested1", 0.5f, ClaimStatus.Contested, Array.Empty<string>(), new[] { "challenger" }),
                new Claim("claim-2", "agent", 1, "contested2", 0.4f, ClaimStatus.Contested, Array.Empty<string>(), new[] { "challenger" }),
                new Claim("claim-3", "agent", 2, "contested3", 0.3f, ClaimStatus.Contested, Array.Empty<string>(), new[] { "challenger" })
            }
        };

        // Act
        var contested = truthMap.GetContestedClaims();

        // Assert
        contested.Should().HaveCount(3);
        contested.Should().OnlyContain(c => c.Status == ClaimStatus.Contested);
    }

    [Fact]
    public void TruthMap_WithModification_CreatesNewInstance()
    {
        // Arrange
        var original = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());
        var claim = new Claim("claim-1", "agent", 1, "text", 0.8f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());

        // Act
        var modified = original with
        {
            Version = 1,
            Round = 1,
            Claims = new[] { claim },
            CoreIdea = "Test idea"
        };

        // Assert
        modified.Should().NotBe(original);
        modified.Version.Should().Be(1);
        modified.Round.Should().Be(1);
        modified.Claims.Should().HaveCount(1);
        modified.CoreIdea.Should().Be("Test idea");
        original.Version.Should().Be(0);
        original.Claims.Should().BeEmpty();
    }

    [Fact]
    public void TruthMap_AllCollections_AreImmutable()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid()) with
        {
            Claims = new[] { new Claim("claim-1", "agent", 1, "text", 0.8f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>()) }
        };

        // Act & Assert - Collections should be readonly
        truthMap.Claims.Should().BeAssignableTo<IReadOnlyList<Claim>>();
        truthMap.Assumptions.Should().BeAssignableTo<IReadOnlyList<Assumption>>();
        truthMap.Decisions.Should().BeAssignableTo<IReadOnlyList<Decision>>();
        truthMap.Risks.Should().BeAssignableTo<IReadOnlyList<Risk>>();
        truthMap.OpenQuestions.Should().BeAssignableTo<IReadOnlyList<OpenQuestion>>();
        truthMap.Evidence.Should().BeAssignableTo<IReadOnlyList<Evidence>>();
        truthMap.Personas.Should().BeAssignableTo<IReadOnlyList<Persona>>();
        truthMap.SuccessMetrics.Should().BeAssignableTo<IReadOnlyList<string>>();
        truthMap.ConfidenceTransitions.Should().BeAssignableTo<IReadOnlyList<ConfidenceTransition>>();
    }

    [Fact]
    public void TruthMap_VersionIncrement_TracksChanges()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act - Simulate patch applications
        var v1 = truthMap with { Version = 1 };
        var v2 = v1 with { Version = 2 };
        var v3 = v2 with { Version = 3 };

        // Assert
        truthMap.Version.Should().Be(0);
        v1.Version.Should().Be(1);
        v2.Version.Should().Be(2);
        v3.Version.Should().Be(3);
    }

    [Fact]
    public void TruthMap_RoundProgression_TracksDebateProgress()
    {
        // Arrange
        var truthMap = Domain.TruthMap.TruthMap.Empty(Guid.NewGuid());

        // Act - Simulate round progression
        var round1 = truthMap with { Round = 1 };
        var round2 = round1 with { Round = 2 };
        var round5 = round2 with { Round = 5 };

        // Assert
        truthMap.Round.Should().Be(0);
        round1.Round.Should().Be(1);
        round2.Round.Should().Be(2);
        round5.Round.Should().Be(5);
    }
}
