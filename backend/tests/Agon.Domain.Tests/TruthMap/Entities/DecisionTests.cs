using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.TruthMap.Entities;

public class DecisionTests
{
    [Fact]
    public void Decision_CanBeCreatedWithAllProperties()
    {
        // Arrange & Act
        var decision = new Decision(
            Id: "decision-1",
            Text: "We will use PostgreSQL as the primary database",
            Rationale: "Based on scalability requirements and team expertise",
            Owner: "architect-agent",
            DerivedFrom: new[] { "requirement-1", "constraint-2", "evidence-3" },
            Binding: true
        );

        // Assert
        decision.Id.Should().Be("decision-1");
        decision.Text.Should().Be("We will use PostgreSQL as the primary database");
        decision.Rationale.Should().Be("Based on scalability requirements and team expertise");
        decision.Owner.Should().Be("architect-agent");
        decision.DerivedFrom.Should().Equal("requirement-1", "constraint-2", "evidence-3");
        decision.Binding.Should().BeTrue();
    }

    [Fact]
    public void Decision_WithEmptyDerivedFrom_IsValid()
    {
        // Arrange & Act
        var decision = new Decision(
            "decision-2",
            "Quick tactical decision",
            "Immediate need without extensive analysis",
            "project-manager",
            Array.Empty<string>(),
            false
        );

        // Assert
        decision.DerivedFrom.Should().BeEmpty();
    }

    [Fact]
    public void Decision_WithMultipleDerivedFrom_PreservesOrder()
    {
        // Arrange
        var derivedFrom = new[] { "claim-1", "evidence-2", "assumption-3", "decision-4" };

        // Act
        var decision = new Decision(
            "decision-3",
            "Multi-source decision",
            "Based on extensive research and prior decisions",
            "council",
            derivedFrom,
            true
        );

        // Assert
        decision.DerivedFrom.Should().Equal(derivedFrom);
        decision.DerivedFrom.Should().HaveCount(4);
    }

    [Fact]
    public void Decision_BindingTrue_IsEnforced()
    {
        // Arrange & Act
        var decision = new Decision(
            "decision-binding",
            "Architectural principle",
            "Must be followed by all teams",
            "cto",
            Array.Empty<string>(),
            true
        );

        // Assert
        decision.Binding.Should().BeTrue();
    }

    [Fact]
    public void Decision_BindingFalse_IsRecommendation()
    {
        // Arrange & Act
        var decision = new Decision(
            "decision-recommendation",
            "Suggested approach",
            "Teams may choose alternatives if justified",
            "tech-lead",
            Array.Empty<string>(),
            false
        );

        // Assert
        decision.Binding.Should().BeFalse();
    }

    [Fact]
    public void Decision_RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var derivedFrom = new[] { "claim-1" };
        var decision1 = new Decision(
            "decision-1",
            "Use microservices",
            "Scalability and team autonomy",
            "architect",
            derivedFrom,
            true
        );

        var decision2 = new Decision(
            "decision-1",
            "Use microservices",
            "Scalability and team autonomy",
            "architect",
            derivedFrom, // Same array reference
            true
        );

        // Act & Assert
        decision1.Should().Be(decision2);
        (decision1 == decision2).Should().BeTrue();
    }

    [Fact]
    public void Decision_RecordEquality_DifferentIds_AreNotEqual()
    {
        // Arrange
        var decision1 = new Decision("decision-1", "text", "rationale", "owner", Array.Empty<string>(), true);
        var decision2 = new Decision("decision-2", "text", "rationale", "owner", Array.Empty<string>(), true);

        // Act & Assert
        decision1.Should().NotBe(decision2);
        (decision1 == decision2).Should().BeFalse();
    }

    [Fact]
    public void Decision_RecordEquality_DifferentBinding_AreNotEqual()
    {
        // Arrange
        var decision1 = new Decision("decision-1", "text", "rationale", "owner", Array.Empty<string>(), true);
        var decision2 = new Decision("decision-1", "text", "rationale", "owner", Array.Empty<string>(), false);

        // Act & Assert
        decision1.Should().NotBe(decision2);
        decision1.Binding.Should().NotBe(decision2.Binding);
    }

    [Fact]
    public void Decision_WithModification_CreatesNewInstance()
    {
        // Arrange
        var original = new Decision(
            "decision-1",
            "Original text",
            "Original rationale",
            "original-owner",
            new[] { "claim-1" },
            false
        );

        // Act
        var modified = original with 
        { 
            Text = "Modified text", 
            Binding = true 
        };

        // Assert
        modified.Should().NotBe(original);
        modified.Text.Should().Be("Modified text");
        modified.Binding.Should().BeTrue();
        modified.Id.Should().Be(original.Id); // Other properties preserved
        modified.Owner.Should().Be(original.Owner);
        modified.Rationale.Should().Be(original.Rationale);
    }

    [Fact]
    public void Decision_Owner_CanBeAnyString()
    {
        // Arrange & Act
        var agentDecision = new Decision("d1", "text", "rationale", "gpt-agent", Array.Empty<string>(), true);
        var humanDecision = new Decision("d2", "text", "rationale", "john.doe@example.com", Array.Empty<string>(), true);
        var councilDecision = new Decision("d3", "text", "rationale", "council-consensus", Array.Empty<string>(), true);

        // Assert
        agentDecision.Owner.Should().Contain("agent");
        humanDecision.Owner.Should().Contain("@");
        councilDecision.Owner.Should().Contain("council");
    }

    [Fact]
    public void Decision_Rationale_CanBeLong()
    {
        // Arrange
        var longRationale = string.Join(" ", Enumerable.Repeat("This decision is based on extensive research and analysis.", 10));

        // Act
        var decision = new Decision(
            "decision-detailed",
            "Complex architectural decision",
            longRationale,
            "architect",
            Array.Empty<string>(),
            true
        );

        // Assert
        decision.Rationale.Should().HaveLength(longRationale.Length);
        decision.Rationale.Should().Contain("extensive research");
    }

    [Fact]
    public void Decision_Deconstruction_WorksCorrectly()
    {
        // Arrange
        var decision = new Decision(
            "decision-1",
            "Test decision",
            "Test rationale",
            "test-owner",
            new[] { "claim-1" },
            true
        );

        // Act
        var (id, text, rationale, owner, derivedFrom, binding) = decision;

        // Assert
        id.Should().Be("decision-1");
        text.Should().Be("Test decision");
        rationale.Should().Be("Test rationale");
        owner.Should().Be("test-owner");
        derivedFrom.Should().Equal("claim-1");
        binding.Should().BeTrue();
    }

    [Fact]
    public void Decision_DerivedFrom_CanReferenceDifferentEntityTypes()
    {
        // Arrange
        var derivedFrom = new[] 
        { 
            "claim-1", 
            "evidence-2", 
            "assumption-3", 
            "risk-4", 
            "constraint-5",
            "decision-6" // Can reference other decisions
        };

        // Act
        var decision = new Decision(
            "decision-composite",
            "Decision based on multiple entity types",
            "Comprehensive analysis of all factors",
            "synthesizer",
            derivedFrom,
            true
        );

        // Assert
        decision.DerivedFrom.Should().Contain(x => x.StartsWith("claim-"));
        decision.DerivedFrom.Should().Contain(x => x.StartsWith("evidence-"));
        decision.DerivedFrom.Should().Contain(x => x.StartsWith("assumption-"));
        decision.DerivedFrom.Should().Contain(x => x.StartsWith("risk-"));
        decision.DerivedFrom.Should().Contain(x => x.StartsWith("constraint-"));
        decision.DerivedFrom.Should().Contain(x => x.StartsWith("decision-"));
    }

    [Fact]
    public void Decision_BindingDecisions_ShouldHaveStrongRationale()
    {
        // Arrange - Example of a binding decision pattern
        var decision = new Decision(
            "decision-binding-example",
            "All APIs must use REST",
            "Ensures consistency across teams and simplifies client integration",
            "technical-steering-committee",
            new[] { "requirement-1", "evidence-2" },
            true
        );

        // Assert
        decision.Binding.Should().BeTrue();
        decision.Rationale.Should().NotBeEmpty();
        decision.Owner.Should().NotBeEmpty();
        decision.DerivedFrom.Should().NotBeEmpty();
    }

    [Fact]
    public void Decision_NonBindingDecisions_CanBeFlexible()
    {
        // Arrange - Example of a non-binding decision pattern
        var decision = new Decision(
            "decision-recommendation-example",
            "Consider using TypeScript for new frontend projects",
            "Provides better type safety but not mandatory if team prefers JavaScript",
            "frontend-lead",
            Array.Empty<string>(),
            false
        );

        // Assert
        decision.Binding.Should().BeFalse();
        decision.Text.Should().Contain("Consider");
        decision.Rationale.Should().Contain("not mandatory");
    }
}
