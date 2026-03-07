using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.TruthMap.Entities;

public class AssumptionTests
{
    [Fact]
    public void Assumption_CanBeCreatedWithAllProperties()
    {
        // Arrange & Act
        var assumption = new Assumption(
            Id: "assumption-1",
            Text: "Users will prefer monthly subscriptions over annual",
            ValidationStep: "Conduct user surveys and analyze competitor pricing",
            DerivedFrom: new[] { "requirement-1", "market-research-2" },
            Status: AssumptionStatus.Unvalidated
        );

        // Assert
        assumption.Id.Should().Be("assumption-1");
        assumption.Text.Should().Be("Users will prefer monthly subscriptions over annual");
        assumption.ValidationStep.Should().Be("Conduct user surveys and analyze competitor pricing");
        assumption.DerivedFrom.Should().Equal("requirement-1", "market-research-2");
        assumption.Status.Should().Be(AssumptionStatus.Unvalidated);
    }

    [Fact]
    public void Assumption_WithEmptyDerivedFrom_IsValid()
    {
        // Arrange & Act
        var assumption = new Assumption(
            "assumption-2",
            "Standalone assumption without explicit source",
            "Validate through prototype testing",
            Array.Empty<string>(),
            AssumptionStatus.Unvalidated
        );

        // Assert
        assumption.DerivedFrom.Should().BeEmpty();
    }

    [Fact]
    public void Assumption_WithMultipleDerivedFrom_PreservesOrder()
    {
        // Arrange
        var derivedFrom = new[] { "claim-1", "evidence-2", "decision-3" };

        // Act
        var assumption = new Assumption(
            "assumption-3",
            "Multi-source assumption",
            "Cross-validate with all sources",
            derivedFrom,
            AssumptionStatus.Validated
        );

        // Assert
        assumption.DerivedFrom.Should().Equal(derivedFrom);
        assumption.DerivedFrom.Should().HaveCount(3);
    }

    [Fact]
    public void Assumption_RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var derivedFrom = new[] { "claim-1" };
        var assumption1 = new Assumption(
            "assumption-1",
            "Test assumption",
            "Test validation",
            derivedFrom,
            AssumptionStatus.Validated
        );

        var assumption2 = new Assumption(
            "assumption-1",
            "Test assumption",
            "Test validation",
            derivedFrom, // Same array reference
            AssumptionStatus.Validated
        );

        // Act & Assert
        assumption1.Should().Be(assumption2);
        (assumption1 == assumption2).Should().BeTrue();
    }

    [Fact]
    public void Assumption_RecordEquality_DifferentIds_AreNotEqual()
    {
        // Arrange
        var assumption1 = new Assumption("assumption-1", "text", "validation", Array.Empty<string>(), AssumptionStatus.Unvalidated);
        var assumption2 = new Assumption("assumption-2", "text", "validation", Array.Empty<string>(), AssumptionStatus.Unvalidated);

        // Act & Assert
        assumption1.Should().NotBe(assumption2);
        (assumption1 == assumption2).Should().BeFalse();
    }

    [Fact]
    public void Assumption_RecordEquality_DifferentStatus_AreNotEqual()
    {
        // Arrange
        var assumption1 = new Assumption("assumption-1", "text", "validation", Array.Empty<string>(), AssumptionStatus.Unvalidated);
        var assumption2 = new Assumption("assumption-1", "text", "validation", Array.Empty<string>(), AssumptionStatus.Validated);

        // Act & Assert
        assumption1.Should().NotBe(assumption2);
        assumption1.Status.Should().NotBe(assumption2.Status);
    }

    [Fact]
    public void Assumption_WithModification_CreatesNewInstance()
    {
        // Arrange
        var original = new Assumption(
            "assumption-1",
            "Original text",
            "Original validation",
            new[] { "claim-1" },
            AssumptionStatus.Unvalidated
        );

        // Act
        var modified = original with 
        { 
            Status = AssumptionStatus.Validated,
            ValidationStep = "Updated validation after testing"
        };

        // Assert
        modified.Should().NotBe(original);
        modified.Status.Should().Be(AssumptionStatus.Validated);
        modified.ValidationStep.Should().Be("Updated validation after testing");
        modified.Id.Should().Be(original.Id); // Other properties preserved
        modified.Text.Should().Be(original.Text);
    }

    [Fact]
    public void Assumption_Deconstruction_WorksCorrectly()
    {
        // Arrange
        var assumption = new Assumption(
            "assumption-1",
            "Test assumption",
            "Test validation",
            new[] { "claim-1" },
            AssumptionStatus.Invalidated
        );

        // Act
        var (id, text, validationStep, derivedFrom, status) = assumption;

        // Assert
        id.Should().Be("assumption-1");
        text.Should().Be("Test assumption");
        validationStep.Should().Be("Test validation");
        derivedFrom.Should().Equal("claim-1");
        status.Should().Be(AssumptionStatus.Invalidated);
    }

    // ── AssumptionStatus Enum Tests ──────────────────────────────────────

    [Fact]
    public void AssumptionStatus_AllValuesAreDefined()
    {
        // Arrange & Act
        var statuses = Enum.GetValues<AssumptionStatus>();

        // Assert
        statuses.Should().Contain(AssumptionStatus.Unvalidated);
        statuses.Should().Contain(AssumptionStatus.Validated);
        statuses.Should().Contain(AssumptionStatus.Invalidated);
        statuses.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(AssumptionStatus.Unvalidated)]
    [InlineData(AssumptionStatus.Validated)]
    [InlineData(AssumptionStatus.Invalidated)]
    public void AssumptionStatus_CanBeUsedInAssumptionCreation(AssumptionStatus status)
    {
        // Arrange & Act
        var assumption = new Assumption("id", "text", "validation", Array.Empty<string>(), status);

        // Assert
        assumption.Status.Should().Be(status);
    }

    [Fact]
    public void Assumption_Unvalidated_IsInitialState()
    {
        // Arrange & Act
        var assumption = new Assumption(
            "assumption-new",
            "New assumption pending validation",
            "Schedule validation session",
            Array.Empty<string>(),
            AssumptionStatus.Unvalidated
        );

        // Assert
        assumption.Status.Should().Be(AssumptionStatus.Unvalidated);
    }

    [Fact]
    public void Assumption_Validated_IndicatesConfirmation()
    {
        // Arrange & Act
        var assumption = new Assumption(
            "assumption-confirmed",
            "Confirmed through testing",
            "Completed: User testing validated this assumption",
            new[] { "evidence-1" },
            AssumptionStatus.Validated
        );

        // Assert
        assumption.Status.Should().Be(AssumptionStatus.Validated);
        assumption.DerivedFrom.Should().NotBeEmpty(); // Validated assumptions typically have evidence
    }

    [Fact]
    public void Assumption_Invalidated_IndicatesRejection()
    {
        // Arrange & Act
        var assumption = new Assumption(
            "assumption-rejected",
            "Disproven by market research",
            "Failed: Survey results contradicted this assumption",
            new[] { "evidence-counter-1" },
            AssumptionStatus.Invalidated
        );

        // Assert
        assumption.Status.Should().Be(AssumptionStatus.Invalidated);
        assumption.ValidationStep.Should().Contain("Failed");
    }

    [Fact]
    public void Assumption_StatusTransition_FromUnvalidatedToValidated()
    {
        // Arrange
        var unvalidated = new Assumption(
            "assumption-1",
            "Pending validation",
            "Conduct user interviews",
            Array.Empty<string>(),
            AssumptionStatus.Unvalidated
        );

        // Act
        var validated = unvalidated with 
        { 
            Status = AssumptionStatus.Validated,
            ValidationStep = "Completed: Users confirmed preference",
            DerivedFrom = new[] { "evidence-1" }
        };

        // Assert
        unvalidated.Status.Should().Be(AssumptionStatus.Unvalidated);
        validated.Status.Should().Be(AssumptionStatus.Validated);
        validated.DerivedFrom.Should().NotBeEmpty();
    }

    [Fact]
    public void Assumption_StatusTransition_FromUnvalidatedToInvalidated()
    {
        // Arrange
        var unvalidated = new Assumption(
            "assumption-1",
            "Assumption to be tested",
            "Run A/B test",
            Array.Empty<string>(),
            AssumptionStatus.Unvalidated
        );

        // Act
        var invalidated = unvalidated with 
        { 
            Status = AssumptionStatus.Invalidated,
            ValidationStep = "Failed: A/B test showed opposite result",
            DerivedFrom = new[] { "evidence-counter-1" }
        };

        // Assert
        unvalidated.Status.Should().Be(AssumptionStatus.Unvalidated);
        invalidated.Status.Should().Be(AssumptionStatus.Invalidated);
        invalidated.ValidationStep.Should().Contain("Failed");
    }

    [Fact]
    public void Assumption_ValidationStep_CanBeLong()
    {
        // Arrange
        var longValidation = string.Join(" ", Enumerable.Repeat("Step details with comprehensive validation methodology.", 10));

        // Act
        var assumption = new Assumption(
            "assumption-detailed",
            "Complex assumption requiring detailed validation",
            longValidation,
            Array.Empty<string>(),
            AssumptionStatus.Unvalidated
        );

        // Assert
        assumption.ValidationStep.Should().HaveLength(longValidation.Length);
        assumption.ValidationStep.Should().Contain("comprehensive validation");
    }
}
