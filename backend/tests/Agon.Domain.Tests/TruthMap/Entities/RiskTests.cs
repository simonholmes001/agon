using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.TruthMap.Entities;

public class RiskTests
{
    [Fact]
    public void Risk_CanBeCreatedWithAllProperties()
    {
        // Arrange & Act
        var risk = new Risk(
            Id: "risk-1",
            Text: "Database scalability may become an issue",
            Category: RiskCategory.Technical,
            Severity: RiskSeverity.High,
            Likelihood: RiskLikelihood.Medium,
            Mitigation: "Plan for horizontal scaling from day one",
            DerivedFrom: new[] { "claim-1", "assumption-2" },
            RaisedBy: "claude-agent"
        );

        // Assert
        risk.Id.Should().Be("risk-1");
        risk.Text.Should().Be("Database scalability may become an issue");
        risk.Category.Should().Be(RiskCategory.Technical);
        risk.Severity.Should().Be(RiskSeverity.High);
        risk.Likelihood.Should().Be(RiskLikelihood.Medium);
        risk.Mitigation.Should().Be("Plan for horizontal scaling from day one");
        risk.DerivedFrom.Should().Equal("claim-1", "assumption-2");
        risk.RaisedBy.Should().Be("claude-agent");
    }

    [Fact]
    public void Risk_WithEmptyDerivedFrom_IsValid()
    {
        // Arrange & Act
        var risk = new Risk(
            Id: "risk-2",
            Text: "Market timing risk",
            Category: RiskCategory.Market,
            Severity: RiskSeverity.Medium,
            Likelihood: RiskLikelihood.High,
            Mitigation: "Monitor competitor launches closely",
            DerivedFrom: Array.Empty<string>(),
            RaisedBy: "gpt-agent"
        );

        // Assert
        risk.DerivedFrom.Should().BeEmpty();
    }

    [Fact]
    public void Risk_RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var derivedFrom = new[] { "constraint-1" };
        var risk1 = new Risk(
            "risk-1",
            "Regulatory compliance risk",
            RiskCategory.Regulatory,
            RiskSeverity.Critical,
            RiskLikelihood.Low,
            "Engage legal counsel early",
            derivedFrom,
            "gemini-agent"
        );

        var risk2 = new Risk(
            "risk-1",
            "Regulatory compliance risk",
            RiskCategory.Regulatory,
            RiskSeverity.Critical,
            RiskLikelihood.Low,
            "Engage legal counsel early",
            derivedFrom, // Same array reference - records compare by value for primitives but reference for arrays
            "gemini-agent"
        );

        // Act & Assert - Records compare each property; arrays are compared by reference
        risk1.Should().Be(risk2);
        (risk1 == risk2).Should().BeTrue();
    }

    [Fact]
    public void Risk_RecordEquality_DifferentIds_AreNotEqual()
    {
        // Arrange
        var risk1 = new Risk("risk-1", "text", RiskCategory.Financial, RiskSeverity.Low, RiskLikelihood.Low, "mitigation", Array.Empty<string>(), "agent");
        var risk2 = new Risk("risk-2", "text", RiskCategory.Financial, RiskSeverity.Low, RiskLikelihood.Low, "mitigation", Array.Empty<string>(), "agent");

        // Act & Assert
        risk1.Should().NotBe(risk2);
        (risk1 == risk2).Should().BeFalse();
    }

    [Fact]
    public void Risk_WithModification_CreatesNewInstance()
    {
        // Arrange
        var original = new Risk(
            "risk-1",
            "Original text",
            RiskCategory.Operational,
            RiskSeverity.Medium,
            RiskLikelihood.Medium,
            "Original mitigation",
            new[] { "claim-1" },
            "agent-1"
        );

        // Act
        var modified = original with { Text = "Modified text", Severity = RiskSeverity.High };

        // Assert
        modified.Should().NotBe(original);
        modified.Text.Should().Be("Modified text");
        modified.Severity.Should().Be(RiskSeverity.High);
        modified.Id.Should().Be(original.Id); // Other properties preserved
        modified.Category.Should().Be(original.Category);
    }

    // ── RiskCategory Enum Tests ──────────────────────────────────────────

    [Fact]
    public void RiskCategory_AllValuesAreDefined()
    {
        // Arrange & Act
        var categories = Enum.GetValues<RiskCategory>();

        // Assert
        categories.Should().Contain(RiskCategory.Market);
        categories.Should().Contain(RiskCategory.Technical);
        categories.Should().Contain(RiskCategory.Operational);
        categories.Should().Contain(RiskCategory.Financial);
        categories.Should().Contain(RiskCategory.Regulatory);
        categories.Should().HaveCount(5);
    }

    [Theory]
    [InlineData(RiskCategory.Market)]
    [InlineData(RiskCategory.Technical)]
    [InlineData(RiskCategory.Operational)]
    [InlineData(RiskCategory.Financial)]
    [InlineData(RiskCategory.Regulatory)]
    public void RiskCategory_CanBeUsedInRiskCreation(RiskCategory category)
    {
        // Arrange & Act
        var risk = new Risk("id", "text", category, RiskSeverity.Low, RiskLikelihood.Low, "mitigation", Array.Empty<string>(), "agent");

        // Assert
        risk.Category.Should().Be(category);
    }

    // ── RiskSeverity Enum Tests ──────────────────────────────────────────

    [Fact]
    public void RiskSeverity_AllValuesAreDefined()
    {
        // Arrange & Act
        var severities = Enum.GetValues<RiskSeverity>();

        // Assert
        severities.Should().Contain(RiskSeverity.Low);
        severities.Should().Contain(RiskSeverity.Medium);
        severities.Should().Contain(RiskSeverity.High);
        severities.Should().Contain(RiskSeverity.Critical);
        severities.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(RiskSeverity.Low)]
    [InlineData(RiskSeverity.Medium)]
    [InlineData(RiskSeverity.High)]
    [InlineData(RiskSeverity.Critical)]
    public void RiskSeverity_CanBeUsedInRiskCreation(RiskSeverity severity)
    {
        // Arrange & Act
        var risk = new Risk("id", "text", RiskCategory.Technical, severity, RiskLikelihood.Low, "mitigation", Array.Empty<string>(), "agent");

        // Assert
        risk.Severity.Should().Be(severity);
    }

    // ── RiskLikelihood Enum Tests ────────────────────────────────────────

    [Fact]
    public void RiskLikelihood_AllValuesAreDefined()
    {
        // Arrange & Act
        var likelihoods = Enum.GetValues<RiskLikelihood>();

        // Assert
        likelihoods.Should().Contain(RiskLikelihood.Low);
        likelihoods.Should().Contain(RiskLikelihood.Medium);
        likelihoods.Should().Contain(RiskLikelihood.High);
        likelihoods.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(RiskLikelihood.Low)]
    [InlineData(RiskLikelihood.Medium)]
    [InlineData(RiskLikelihood.High)]
    public void RiskLikelihood_CanBeUsedInRiskCreation(RiskLikelihood likelihood)
    {
        // Arrange & Act
        var risk = new Risk("id", "text", RiskCategory.Technical, RiskSeverity.Low, likelihood, "mitigation", Array.Empty<string>(), "agent");

        // Assert
        risk.Likelihood.Should().Be(likelihood);
    }

    // ── Integration Scenarios ────────────────────────────────────────────

    [Fact]
    public void Risk_AllCategorySeverityLikelihoodCombinations_AreValid()
    {
        // Arrange
        var categories = Enum.GetValues<RiskCategory>();
        var severities = Enum.GetValues<RiskSeverity>();
        var likelihoods = Enum.GetValues<RiskLikelihood>();

        // Act & Assert
        foreach (var category in categories)
        {
            foreach (var severity in severities)
            {
                foreach (var likelihood in likelihoods)
                {
                    var risk = new Risk(
                        $"risk-{category}-{severity}-{likelihood}",
                        $"Test risk: {category} {severity} {likelihood}",
                        category,
                        severity,
                        likelihood,
                        "Test mitigation",
                        Array.Empty<string>(),
                        "test-agent"
                    );

                    risk.Category.Should().Be(category);
                    risk.Severity.Should().Be(severity);
                    risk.Likelihood.Should().Be(likelihood);
                }
            }
        }
    }

    [Fact]
    public void Risk_WithMultipleDerivedFromReferences_PreservesOrder()
    {
        // Arrange
        var derivedFrom = new[] { "claim-1", "assumption-2", "constraint-3", "decision-4" };

        // Act
        var risk = new Risk(
            "risk-1",
            "Complex risk",
            RiskCategory.Operational,
            RiskSeverity.High,
            RiskLikelihood.High,
            "Multi-faceted mitigation strategy",
            derivedFrom,
            "synthesizer"
        );

        // Assert
        risk.DerivedFrom.Should().Equal(derivedFrom);
        risk.DerivedFrom.Should().HaveCount(4);
    }

    [Fact]
    public void Risk_Deconstruction_WorksCorrectly()
    {
        // Arrange
        var risk = new Risk(
            "risk-1",
            "Test risk",
            RiskCategory.Financial,
            RiskSeverity.Critical,
            RiskLikelihood.High,
            "Test mitigation",
            new[] { "claim-1" },
            "test-agent"
        );

        // Act
        var (id, text, category, severity, likelihood, mitigation, derivedFrom, raisedBy) = risk;

        // Assert
        id.Should().Be("risk-1");
        text.Should().Be("Test risk");
        category.Should().Be(RiskCategory.Financial);
        severity.Should().Be(RiskSeverity.Critical);
        likelihood.Should().Be(RiskLikelihood.High);
        mitigation.Should().Be("Test mitigation");
        derivedFrom.Should().Equal("claim-1");
        raisedBy.Should().Be("test-agent");
    }
}
