using Agon.Application.Interfaces;
using Agon.Application.Services;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Application.Tests.Services;

public class PrdInstructionGeneratorTests
{
    private readonly PrdInstructionGenerator _generator = new();

    // ─── Interface Implementation Tests ────────────────────────────────────────

    [Fact]
    public void Generator_ShouldImplementIArtifactGenerator()
    {
        // Assert
        _generator.Should().BeAssignableTo<IArtifactGenerator>();
    }

    [Fact]
    public void Type_ShouldReturnPrd()
    {
        // Assert
        _generator.Type.Should().Be(ArtifactType.Prd);
    }

    // ─── YAML Frontmatter Tests ────────────────────────────────────────────────

    [Fact]
    public void Generate_ShouldIncludeYamlFrontmatterWithApplyToDirective()
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
    public void Generate_ShouldIncludePrdHeader()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("# Product Requirements Document");
    }

    // ─── Executive Summary Section Tests ───────────────────────────────────────

    [Fact]
    public void Generate_WithCoreIdea_ShouldIncludeExecutiveSummary()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.CoreIdea = "A B2B marketplace connecting local organic farmers directly with restaurant buyers";

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Executive Summary");
        result.Should().Contain("A B2B marketplace connecting local organic farmers directly with restaurant buyers");
    }

    [Fact]
    public void Generate_WithEmptyCoreIdea_ShouldOmitExecutiveSummary()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## Executive Summary");
    }

    // ─── Problem Statement Section Tests ───────────────────────────────────────

    [Fact]
    public void Generate_WithPersonas_ShouldIncludeProblemStatement()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Personas.Add(new Persona
        {
            Id = "persona-001",
            Name = "Chef Maria",
            Description = "Head chef at a mid-size restaurant struggling to find reliable suppliers of fresh, local produce"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Problem Statement");
        result.Should().Contain("### Target Users");
        result.Should().Contain("**Chef Maria**");
        result.Should().Contain("Head chef at a mid-size restaurant");
    }

    // ─── Success Metrics Section Tests ─────────────────────────────────────────

    [Fact]
    public void Generate_WithSuccessMetrics_ShouldIncludeMetricsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.SuccessMetrics.AddRange([
            "10,000 monthly active users within 6 months",
            "95% order fulfillment rate",
            "Average delivery time under 24 hours"
        ]);

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Success Metrics");
        result.Should().Contain("| Metric | Target |");
        result.Should().Contain("10,000 monthly active users within 6 months");
    }

    [Fact]
    public void Generate_WithNoSuccessMetrics_ShouldOmitMetricsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## Success Metrics");
    }

    // ─── Constraints Section Tests ─────────────────────────────────────────────

    [Fact]
    public void Generate_WithConstraints_ShouldIncludeConstraintsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Constraints = new Constraints
        {
            Budget = "$150,000",
            Timeline = "9 months to MVP",
            TechStack = ["Next.js", ".NET"],
            NonNegotiables = ["GDPR compliance", "iOS and Android support"]
        };

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Constraints");
        result.Should().Contain("**Budget:** $150,000");
        result.Should().Contain("**Timeline:** 9 months to MVP");
    }

    // ─── Assumptions Section Tests ─────────────────────────────────────────────

    [Fact]
    public void Generate_WithAssumptions_ShouldIncludeAssumptionsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Restaurants prefer local produce over cheaper imported alternatives",
            ValidationStep = "Survey 50 restaurant owners in target market",
            Status = AssumptionStatus.Unvalidated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Key Assumptions");
        result.Should().Contain("Restaurants prefer local produce");
        result.Should().Contain("Survey 50 restaurant owners");
    }

    // ─── Risks Section Tests ───────────────────────────────────────────────────

    [Fact]
    public void Generate_WithRisks_ShouldIncludeRisksSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-001",
            Text = "Supply chain disruption due to weather events",
            Category = RiskCategory.Execution,
            Severity = Severity.High,
            Likelihood = Likelihood.Medium,
            Mitigation = "Build relationships with multiple suppliers per region",
            Agent = "claude"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Risks");
        result.Should().Contain("Supply chain disruption");
        result.Should().Contain("**Severity:** High");
    }

    // ─── Open Questions Section Tests ──────────────────────────────────────────

    [Fact]
    public void Generate_WithOpenQuestions_ShouldIncludeOpenQuestionsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.OpenQuestions.Add(new OpenQuestion
        {
            Id = "q-001",
            Text = "What is the minimum order size that makes delivery economically viable?",
            Blocking = true,
            RaisedBy = "claude"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Open Questions");
        result.Should().Contain("What is the minimum order size");
        result.Should().Contain("🚫 Blocking");
    }

    [Fact]
    public void Generate_WithNonBlockingQuestion_ShouldNotShowBlockingIndicator()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.OpenQuestions.Add(new OpenQuestion
        {
            Id = "q-001",
            Text = "Should we support multiple currencies?",
            Blocking = false,
            RaisedBy = "gpt"
        });

        // Act
        var result = _generator.Generate(truthMap);


        // Assert
        result.Should().Contain("Should we support multiple currencies");
        result.Should().NotContain("🚫 Blocking");
    }

    // ─── Full Document Structure Tests ─────────────────────────────────────────

    [Fact]
    public void Generate_WithCompleteTruthMap_ShouldProduceSectionInCorrectOrder()
    {
        // Arrange
        var truthMap = CreateCompleteTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        var headerIndex = result.IndexOf("# Product Requirements Document");
        var summaryIndex = result.IndexOf("## Executive Summary");
        var problemIndex = result.IndexOf("## Problem Statement");
        var metricsIndex = result.IndexOf("## Success Metrics");
        var constraintsIndex = result.IndexOf("## Constraints");
        var assumptionsIndex = result.IndexOf("## Key Assumptions");
        var risksIndex = result.IndexOf("## Risks");
        var questionsIndex = result.IndexOf("## Open Questions");

        headerIndex.Should().BeGreaterThan(-1);
        summaryIndex.Should().BeGreaterThan(headerIndex);
        problemIndex.Should().BeGreaterThan(summaryIndex);
        metricsIndex.Should().BeGreaterThan(problemIndex);
        constraintsIndex.Should().BeGreaterThan(metricsIndex);
        assumptionsIndex.Should().BeGreaterThan(constraintsIndex);
        risksIndex.Should().BeGreaterThan(assumptionsIndex);
        questionsIndex.Should().BeGreaterThan(risksIndex);
    }

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

    private static TruthMapState CreateCompleteTruthMap()
    {
        var truthMap = TruthMapState.CreateNew(Guid.NewGuid());

        truthMap.CoreIdea = "FarmConnect - B2B marketplace for local farmers";

        truthMap.Personas.Add(new Persona
        {
            Id = "persona-001",
            Name = "Chef Maria",
            Description = "Restaurant owner seeking fresh produce"
        });

        truthMap.SuccessMetrics.Add("10,000 MAU");

        truthMap.Constraints = new Constraints
        {
            Budget = "$100,000",
            Timeline = "6 months"
        };

        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Restaurants want local produce",
            Status = AssumptionStatus.Unvalidated
        });

        truthMap.Risks.Add(new Risk
        {
            Id = "risk-001",
            Text = "Supply chain risk",
            Severity = Severity.Medium,
            Agent = "claude"
        });

        truthMap.OpenQuestions.Add(new OpenQuestion
        {
            Id = "q-001",
            Text = "Minimum order size?",
            Blocking = true,
            RaisedBy = "claude"
        });

        return truthMap;
    }
}
