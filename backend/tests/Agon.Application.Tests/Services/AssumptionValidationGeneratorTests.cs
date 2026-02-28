using Agon.Application.Interfaces;
using Agon.Application.Services;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Application.Tests.Services;

public class AssumptionValidationGeneratorTests
{
    private readonly AssumptionValidationGenerator _generator = new();

    // ─── Interface Implementation Tests ────────────────────────────────────────

    [Fact]
    public void Generator_ShouldImplementIArtifactGenerator()
    {
        // Assert
        _generator.Should().BeAssignableTo<IArtifactGenerator>();
    }

    [Fact]
    public void Type_ShouldReturnAssumptions()
    {
        // Assert
        _generator.Type.Should().Be(ArtifactType.Assumptions);
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
    public void Generate_ShouldIncludeAssumptionValidationHeader()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("# Assumption Validation");
    }

    // ─── Assumption Table Tests ────────────────────────────────────────────────

    [Fact]
    public void Generate_WithAssumptions_ShouldIncludeAssumptionTable()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Users prefer mobile apps",
            ValidationStep = "Survey 100 target users",
            Status = AssumptionStatus.Unvalidated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("| ID | Assumption | Validation Step | Status |");
        result.Should().Contain("| asm-001 |");
        result.Should().Contain("Users prefer mobile apps");
        result.Should().Contain("Survey 100 target users");
        result.Should().Contain("⚠️ Unvalidated");
    }

    [Fact]
    public void Generate_WithValidatedAssumption_ShouldShowValidatedStatus()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Market demand exists",
            ValidationStep = "Market research completed",
            Status = AssumptionStatus.Validated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("✅ Validated");
    }

    [Fact]
    public void Generate_WithInvalidatedAssumption_ShouldShowInvalidatedStatus()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Users will pay premium prices",
            ValidationStep = "Pricing survey conducted",
            Status = AssumptionStatus.Invalidated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("❌ Invalidated");
    }

    [Fact]
    public void Generate_WithMissingValidationStep_ShouldShowTbd()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Some assumption",
            ValidationStep = null,
            Status = AssumptionStatus.Unvalidated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("| TBD |");
    }

    // ─── Status Summary Tests ──────────────────────────────────────────────────

    [Fact]
    public void Generate_WithAssumptions_ShouldIncludeStatusSummary()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Assumption 1",
            Status = AssumptionStatus.Validated
        });
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-002",
            Text = "Assumption 2",
            Status = AssumptionStatus.Unvalidated
        });
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-003",
            Text = "Assumption 3",
            Status = AssumptionStatus.Unvalidated
        });
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-004",
            Text = "Assumption 4",
            Status = AssumptionStatus.Invalidated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Status Summary");
        result.Should().Contain("| Status | Count |");
        result.Should().Contain("| ✅ Validated | 1 |");
        result.Should().Contain("| ⚠️ Unvalidated | 2 |");
        result.Should().Contain("| ❌ Invalidated | 1 |");
    }

    // ─── Critical Assumptions Section Tests ────────────────────────────────────

    [Fact]
    public void Generate_WithUnvalidatedAssumptions_ShouldHighlightCriticalAssumptions()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Critical assumption needing validation",
            Status = AssumptionStatus.Unvalidated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## ⚠️ Critical: Unvalidated Assumptions");
        result.Should().Contain("Critical assumption needing validation");
    }

    [Fact]
    public void Generate_WithAllValidated_ShouldNotShowCriticalSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Validated assumption",
            Status = AssumptionStatus.Validated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## ⚠️ Critical");
    }

    // ─── Empty State Tests ─────────────────────────────────────────────────────

    [Fact]
    public void Generate_WithNoAssumptions_ShouldIncludeNoAssumptionsMessage()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("No assumptions have been documented");
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
