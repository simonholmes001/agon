using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.TruthMap.Entities;

public class ClaimTests
{
    [Fact]
    public void Claim_CanBeCreatedWithAllProperties()
    {
        // Arrange & Act
        var claim = new Claim(
            Id: "claim-1",
            ProposedBy: "gpt-agent",
            Round: 1,
            Text: "PostgreSQL is the best choice for this application",
            Confidence: 0.85f,
            Status: ClaimStatus.Active,
            DerivedFrom: new[] { "requirement-1", "constraint-2" },
            ChallengedBy: new[] { "claim-5" }
        );

        // Assert
        claim.Id.Should().Be("claim-1");
        claim.ProposedBy.Should().Be("gpt-agent");
        claim.Round.Should().Be(1);
        claim.Text.Should().Be("PostgreSQL is the best choice for this application");
        claim.Confidence.Should().BeApproximately(0.85f, 0.001f);
        claim.Status.Should().Be(ClaimStatus.Active);
        claim.DerivedFrom.Should().Equal("requirement-1", "constraint-2");
        claim.ChallengedBy.Should().Equal("claim-5");
    }

    [Fact]
    public void Claim_WithEmptyDerivedFromAndChallengedBy_IsValid()
    {
        // Arrange & Act
        var claim = new Claim(
            "claim-2",
            "claude-agent",
            2,
            "Initial claim without derivation",
            0.70f,
            ClaimStatus.Active,
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Assert
        claim.DerivedFrom.Should().BeEmpty();
        claim.ChallengedBy.Should().BeEmpty();
    }

    [Fact]
    public void Claim_WithMultipleChallenges_PreservesOrder()
    {
        // Arrange
        var challengedBy = new[] { "claim-10", "claim-11", "claim-12" };

        // Act
        var claim = new Claim(
            "claim-contested",
            "gemini-agent",
            3,
            "Heavily contested claim",
            0.45f,
            ClaimStatus.Contested,
            Array.Empty<string>(),
            challengedBy
        );

        // Assert
        claim.ChallengedBy.Should().Equal(challengedBy);
        claim.ChallengedBy.Should().HaveCount(3);
        claim.Status.Should().Be(ClaimStatus.Contested);
    }

    [Fact]
    public void Claim_ConfidenceRange_0To1_IsValid()
    {
        // Arrange & Act
        var lowConfidence = new Claim("c1", "agent", 1, "text", 0.0f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var midConfidence = new Claim("c2", "agent", 1, "text", 0.5f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var highConfidence = new Claim("c3", "agent", 1, "text", 1.0f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());

        // Assert
        lowConfidence.Confidence.Should().Be(0.0f);
        midConfidence.Confidence.Should().Be(0.5f);
        highConfidence.Confidence.Should().Be(1.0f);
    }

    [Fact]
    public void Claim_RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var derivedFrom = new[] { "req-1" };
        var challengedBy = new[] { "claim-2" };

        var claim1 = new Claim(
            "claim-1",
            "agent",
            1,
            "Test claim",
            0.8f,
            ClaimStatus.Active,
            derivedFrom,
            challengedBy
        );

        var claim2 = new Claim(
            "claim-1",
            "agent",
            1,
            "Test claim",
            0.8f,
            ClaimStatus.Active,
            derivedFrom, // Same array reference
            challengedBy // Same array reference
        );

        // Act & Assert
        claim1.Should().Be(claim2);
        (claim1 == claim2).Should().BeTrue();
    }

    [Fact]
    public void Claim_RecordEquality_DifferentIds_AreNotEqual()
    {
        // Arrange
        var claim1 = new Claim("claim-1", "agent", 1, "text", 0.5f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var claim2 = new Claim("claim-2", "agent", 1, "text", 0.5f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());

        // Act & Assert
        claim1.Should().NotBe(claim2);
        (claim1 == claim2).Should().BeFalse();
    }

    [Fact]
    public void Claim_RecordEquality_DifferentConfidence_AreNotEqual()
    {
        // Arrange
        var claim1 = new Claim("claim-1", "agent", 1, "text", 0.5f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var claim2 = new Claim("claim-1", "agent", 1, "text", 0.6f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());

        // Act & Assert
        claim1.Should().NotBe(claim2);
        claim1.Confidence.Should().NotBe(claim2.Confidence);
    }

    [Fact]
    public void Claim_WithModification_CreatesNewInstance()
    {
        // Arrange
        var original = new Claim(
            "claim-1",
            "agent-1",
            1,
            "Original text",
            0.7f,
            ClaimStatus.Active,
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Act
        var modified = original with 
        { 
            Confidence = 0.9f,
            Status = ClaimStatus.Contested,
            ChallengedBy = new[] { "claim-2" }
        };

        // Assert
        modified.Should().NotBe(original);
        modified.Confidence.Should().Be(0.9f);
        modified.Status.Should().Be(ClaimStatus.Contested);
        modified.ChallengedBy.Should().HaveCount(1);
        modified.Id.Should().Be(original.Id); // Other properties preserved
    }

    [Fact]
    public void Claim_Deconstruction_WorksCorrectly()
    {
        // Arrange
        var claim = new Claim(
            "claim-1",
            "test-agent",
            5,
            "Test text",
            0.75f,
            ClaimStatus.PendingRevalidation,
            new[] { "req-1" },
            new[] { "claim-2" }
        );

        // Act
        var (id, proposedBy, round, text, confidence, status, derivedFrom, challengedBy) = claim;

        // Assert
        id.Should().Be("claim-1");
        proposedBy.Should().Be("test-agent");
        round.Should().Be(5);
        text.Should().Be("Test text");
        confidence.Should().BeApproximately(0.75f, 0.001f);
        status.Should().Be(ClaimStatus.PendingRevalidation);
        derivedFrom.Should().Equal("req-1");
        challengedBy.Should().Equal("claim-2");
    }

    // ── ClaimStatus Enum Tests ───────────────────────────────────────────

    [Fact]
    public void ClaimStatus_AllValuesAreDefined()
    {
        // Arrange & Act
        var statuses = Enum.GetValues<ClaimStatus>();

        // Assert
        statuses.Should().Contain(ClaimStatus.Active);
        statuses.Should().Contain(ClaimStatus.Contested);
        statuses.Should().Contain(ClaimStatus.PendingRevalidation);
        statuses.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(ClaimStatus.Active)]
    [InlineData(ClaimStatus.Contested)]
    [InlineData(ClaimStatus.PendingRevalidation)]
    public void ClaimStatus_CanBeUsedInClaimCreation(ClaimStatus status)
    {
        // Arrange & Act
        var claim = new Claim("id", "agent", 1, "text", 0.5f, status, Array.Empty<string>(), Array.Empty<string>());

        // Assert
        claim.Status.Should().Be(status);
    }

    [Fact]
    public void Claim_Active_HasNoChallenges()
    {
        // Arrange & Act
        var claim = new Claim(
            "claim-active",
            "agent",
            1,
            "Unchallenged claim",
            0.9f,
            ClaimStatus.Active,
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Assert
        claim.Status.Should().Be(ClaimStatus.Active);
        claim.ChallengedBy.Should().BeEmpty();
    }

    [Fact]
    public void Claim_Contested_HasChallenges()
    {
        // Arrange & Act
        var claim = new Claim(
            "claim-contested",
            "agent",
            2,
            "Challenged claim",
            0.6f,
            ClaimStatus.Contested,
            Array.Empty<string>(),
            new[] { "claim-challenger-1", "claim-challenger-2" }
        );

        // Assert
        claim.Status.Should().Be(ClaimStatus.Contested);
        claim.ChallengedBy.Should().NotBeEmpty();
        claim.ChallengedBy.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Claim_PendingRevalidation_AwaitingReview()
    {
        // Arrange & Act
        var claim = new Claim(
            "claim-revalidation",
            "agent",
            3,
            "Needs revalidation",
            0.7f,
            ClaimStatus.PendingRevalidation,
            new[] { "evidence-outdated-1" },
            Array.Empty<string>()
        );

        // Assert
        claim.Status.Should().Be(ClaimStatus.PendingRevalidation);
    }

    [Fact]
    public void Claim_StatusTransition_ActiveToContested()
    {
        // Arrange
        var active = new Claim(
            "claim-1",
            "agent",
            1,
            "Initially accepted",
            0.9f,
            ClaimStatus.Active,
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Act
        var contested = active with 
        { 
            Status = ClaimStatus.Contested,
            ChallengedBy = new[] { "claim-challenger" },
            Confidence = 0.6f
        };

        // Assert
        active.Status.Should().Be(ClaimStatus.Active);
        contested.Status.Should().Be(ClaimStatus.Contested);
        contested.ChallengedBy.Should().NotBeEmpty();
        contested.Confidence.Should().BeLessThan(active.Confidence);
    }

    [Fact]
    public void Claim_RoundProgression_IncreasesOverTime()
    {
        // Arrange & Act
        var round1Claim = new Claim("c1", "agent", 1, "text", 0.5f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var round5Claim = new Claim("c2", "agent", 5, "text", 0.7f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var round10Claim = new Claim("c3", "agent", 10, "text", 0.9f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());

        // Assert
        round1Claim.Round.Should().Be(1);
        round5Claim.Round.Should().Be(5);
        round10Claim.Round.Should().Be(10);
        round10Claim.Round.Should().BeGreaterThan(round5Claim.Round);
        round5Claim.Round.Should().BeGreaterThan(round1Claim.Round);
    }

    [Fact]
    public void Claim_ProposedBy_TracksOrigin()
    {
        // Arrange & Act
        var gptClaim = new Claim("c1", "gpt-agent", 1, "GPT proposal", 0.8f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var claudeClaim = new Claim("c2", "claude-agent", 1, "Claude proposal", 0.8f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());
        var geminiClaim = new Claim("c3", "gemini-agent", 1, "Gemini proposal", 0.8f, ClaimStatus.Active, Array.Empty<string>(), Array.Empty<string>());

        // Assert
        gptClaim.ProposedBy.Should().Contain("gpt");
        claudeClaim.ProposedBy.Should().Contain("claude");
        geminiClaim.ProposedBy.Should().Contain("gemini");
    }

    [Fact]
    public void Claim_ContestedWithHighConfidence_IsPossible()
    {
        // Arrange & Act - A claim can be contested but still maintain high confidence
        var claim = new Claim(
            "claim-contested-confident",
            "agent",
            5,
            "Contested but well-supported claim",
            0.85f,
            ClaimStatus.Contested,
            new[] { "evidence-strong-1", "evidence-strong-2" },
            new[] { "claim-weak-challenge" }
        );

        // Assert
        claim.Status.Should().Be(ClaimStatus.Contested);
        claim.Confidence.Should().BeGreaterThan(0.8f);
        claim.DerivedFrom.Should().NotBeEmpty();
        claim.ChallengedBy.Should().NotBeEmpty();
    }
}
