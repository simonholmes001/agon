using Agon.Application.Interfaces;
using Agon.Application.Services;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Application.Tests.Services;

public class RiskRegistryGeneratorTests
{
    private readonly RiskRegistryGenerator _generator = new();

    // ─── Interface Implementation Tests ────────────────────────────────────────

    [Fact]
    public void Generator_ShouldImplementIArtifactGenerator()
    {
        // Assert
        _generator.Should().BeAssignableTo<IArtifactGenerator>();
    }

    [Fact]
    public void Type_ShouldReturnRisks()
    {
        // Assert
        _generator.Type.Should().Be(ArtifactType.Risks);
    }

    // ─── YAML Frontmatter Tests ────────────────────────────────────────────────

    [Fact]
    public void Generate_ShouldIncludeYamlFrontmatter()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().StartWith("---\napplyTo: '**'\n---\n");
    }

    // ─── Header Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Generate_ShouldIncludeRiskRegistryHeader()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("# Risk Registry");
    }

    // ─── Risk Table Tests ──────────────────────────────────────────────────────

    [Fact]
    public void Generate_WithRisks_ShouldIncludeRiskTable()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-001",
            Text = "Database scaling bottleneck",
            Category = RiskCategory.Technical,
            Severity = Severity.High,
            Likelihood = Likelihood.Medium,
            Mitigation = "Implement read replicas",
            Agent = "claude"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("| ID | Risk | Category | Severity | Likelihood | Mitigation | Source |");
        result.Should().Contain("| risk-001 |");
        result.Should().Contain("Database scaling bottleneck");
        result.Should().Contain("Technical");
        result.Should().Contain("High");
        result.Should().Contain("Medium");
        result.Should().Contain("Implement read replicas");
    }

    [Fact]
    public void Generate_WithMultipleRisks_ShouldSortBySeverityDescending()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-low",
            Text = "Minor UX issue",
            Severity = Severity.Low,
            Agent = "gpt"
        });
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-critical",
            Text = "Security vulnerability",
            Severity = Severity.Critical,
            Agent = "claude"
        });
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-medium",
            Text = "Performance concern",
            Severity = Severity.Medium,
            Agent = "gemini"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        var criticalIndex = result.IndexOf("Security vulnerability");
        var mediumIndex = result.IndexOf("Performance concern");
        var lowIndex = result.IndexOf("Minor UX issue");

        criticalIndex.Should().BeLessThan(mediumIndex);
        mediumIndex.Should().BeLessThan(lowIndex);
    }

    // ─── Category Summary Tests ────────────────────────────────────────────────

    [Fact]
    public void Generate_WithRisks_ShouldIncludeCategorySummary()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-001",
            Text = "Technical risk 1",
            Category = RiskCategory.Technical,
            Agent = "claude"
        });
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-002",
            Text = "Technical risk 2",
            Category = RiskCategory.Technical,
            Agent = "claude"
        });
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-003",
            Text = "Market risk",
            Category = RiskCategory.Market,
            Agent = "gpt"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Summary by Category");
        result.Should().Contain("| Category | Count |");
        result.Should().Contain("| Technical | 2 |");
        result.Should().Contain("| Market | 1 |");
    }

    // ─── Severity Summary Tests ────────────────────────────────────────────────

    [Fact]
    public void Generate_WithRisks_ShouldIncludeSeveritySummary()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-001",
            Text = "Critical risk",
            Severity = Severity.Critical,
            Agent = "claude"
        });
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-002",
            Text = "High risk",
            Severity = Severity.High,
            Agent = "gpt"
        });
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-003",
            Text = "High risk 2",
            Severity = Severity.High,
            Agent = "gemini"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Summary by Severity");
        result.Should().Contain("| Severity | Count |");
        result.Should().Contain("| Critical | 1 |");
        result.Should().Contain("| High | 2 |");
    }

    // ─── Empty State Tests ─────────────────────────────────────────────────────

    [Fact]
    public void Generate_WithNoRisks_ShouldIncludeNoRisksMessage()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("No risks have been identified");
    }

    // ─── Null Tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_WithNullTruthMap_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => _generator.Generate(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    // ─── Helper Methods ────────────────────────────────────────────────────────

    private static TruthMapState CreateMinimalTruthMap()
    {
        return TruthMapState.CreateNew(Guid.NewGuid());
    }
}
