using Agon.Application.Interfaces;
using Agon.Application.Services;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Application.Tests.Services;

public class CopilotInstructionGeneratorTests
{
    private readonly CopilotInstructionGenerator _generator = new();

    // ─── Interface Implementation Tests ────────────────────────────────────────

    [Fact]
    public void Generator_ShouldImplementIArtifactGenerator()
    {
        // Assert
        _generator.Should().BeAssignableTo<IArtifactGenerator>();
    }

    [Fact]
    public void Type_ShouldReturnCopilot()
    {
        // Assert
        _generator.Type.Should().Be(ArtifactType.Copilot);
    }

    [Fact]
    public void Generate_WithSingleParameter_ShouldUseDefaultOptions()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        IArtifactGenerator generator = _generator;

        // Act
        var result = generator.Generate(truthMap);

        // Assert
        result.Should().StartWith("---\napplyTo: '**'\n---\n");
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

    [Fact]
    public void Generate_WithCustomApplyToPattern_ShouldUseSpecifiedPattern()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        var options = new CopilotInstructionOptions { ApplyTo = "**/*.cs" };

        // Act
        var result = _generator.Generate(truthMap, options);

        // Assert
        result.Should().StartWith("---\napplyTo: '**/*.cs'\n---\n");
    }

    // ─── Core Idea Section Tests ───────────────────────────────────────────────

    [Fact]
    public void Generate_ShouldIncludeCoreIdeaSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.CoreIdea = "A marketplace connecting local farmers with restaurants";

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("# Project Overview");
        result.Should().Contain("A marketplace connecting local farmers with restaurants");
    }

    [Fact]
    public void Generate_WithEmptyCoreIdea_ShouldOmitProjectOverviewSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.CoreIdea = "";

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("# Project Overview");
    }

    // ─── Constraints Section Tests ─────────────────────────────────────────────

    [Fact]
    public void Generate_WithConstraints_ShouldIncludeConstraintsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Constraints = new Constraints
        {
            Budget = "$50,000",
            Timeline = "6 months",
            TechStack = ["Next.js", ".NET", "PostgreSQL"],
            NonNegotiables = ["Mobile-first", "GDPR compliance"]
        };

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Constraints");
        result.Should().Contain("**Budget:** $50,000");
        result.Should().Contain("**Timeline:** 6 months");
        result.Should().Contain("### Tech Stack");
        result.Should().Contain("- Next.js");
        result.Should().Contain("- .NET");
        result.Should().Contain("- PostgreSQL");
        result.Should().Contain("### Non-Negotiables");
        result.Should().Contain("- Mobile-first");
        result.Should().Contain("- GDPR compliance");
    }

    [Fact]
    public void Generate_WithEmptyConstraints_ShouldOmitConstraintsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Constraints = new Constraints();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## Constraints");
    }

    // ─── Decisions Section Tests ───────────────────────────────────────────────

    [Fact]
    public void Generate_WithDecisions_ShouldIncludeDecisionsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Decisions.Add(new Decision
        {
            Id = "dec-001",
            Text = "Use event-driven architecture",
            Rationale = "Enables loose coupling and scalability",
            Owner = "architect",
            Binding = true
        });
        truthMap.Decisions.Add(new Decision
        {
            Id = "dec-002",
            Text = "PostgreSQL for primary data store",
            Rationale = "Strong JSON support and reliability",
            Owner = "architect",
            Binding = false
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Key Decisions");
        result.Should().Contain("### Use event-driven architecture");
        result.Should().Contain("**Rationale:** Enables loose coupling and scalability");
        result.Should().Contain("**Status:** Binding");
        result.Should().Contain("### PostgreSQL for primary data store");
        result.Should().Contain("**Status:** Advisory");
    }

    [Fact]
    public void Generate_WithNoDecisions_ShouldOmitDecisionsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## Key Decisions");
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
            Text = "Third-party API rate limits may throttle operations",
            Category = RiskCategory.Technical,
            Severity = Severity.High,
            Likelihood = Likelihood.Medium,
            Mitigation = "Implement caching and request queuing",
            Agent = "claude"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Risks & Mitigations");
        result.Should().Contain("### Third-party API rate limits may throttle operations");
        result.Should().Contain("**Category:** Technical");
        result.Should().Contain("**Severity:** High");
        result.Should().Contain("**Likelihood:** Medium");
        result.Should().Contain("**Mitigation:** Implement caching and request queuing");
    }

    [Fact]
    public void Generate_WithRisks_ShouldSortBySeverityDescending()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-low",
            Text = "Minor UX inconsistencies",
            Severity = Severity.Low,
            Agent = "gpt"
        });
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-critical",
            Text = "Data breach vulnerability",
            Severity = Severity.Critical,
            Agent = "claude"
        });
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-medium",
            Text = "Performance degradation",
            Severity = Severity.Medium,
            Agent = "gemini"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        var criticalIndex = result.IndexOf("Data breach vulnerability");
        var mediumIndex = result.IndexOf("Performance degradation");
        var lowIndex = result.IndexOf("Minor UX inconsistencies");

        criticalIndex.Should().BeLessThan(mediumIndex);
        mediumIndex.Should().BeLessThan(lowIndex);
    }

    [Fact]
    public void Generate_WithNoRisks_ShouldOmitRisksSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## Risks & Mitigations");
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
            Text = "Users have reliable internet connectivity",
            ValidationStep = "Survey target user base connectivity patterns",
            Status = AssumptionStatus.Unvalidated
        });
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-002",
            Text = "Restaurant owners are willing to change suppliers",
            ValidationStep = "Conduct 10 customer discovery interviews",
            Status = AssumptionStatus.Validated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Assumptions");
        result.Should().Contain("### Users have reliable internet connectivity");
        result.Should().Contain("**Validation:** Survey target user base connectivity patterns");
        result.Should().Contain("**Status:** ⚠️ Unvalidated");
        result.Should().Contain("### Restaurant owners are willing to change suppliers");
        result.Should().Contain("**Status:** ✅ Validated");
    }

    [Fact]
    public void Generate_WithInvalidatedAssumption_ShouldShowWarningStatus()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Users prefer mobile apps over web",
            Status = AssumptionStatus.Invalidated
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("**Status:** ❌ Invalidated");
    }

    [Fact]
    public void Generate_WithNoAssumptions_ShouldOmitAssumptionsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## Assumptions");
    }

    // ─── Success Metrics Section Tests ─────────────────────────────────────────

    [Fact]
    public void Generate_WithSuccessMetrics_ShouldIncludeMetricsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.SuccessMetrics.AddRange([
            "10,000 MAU within 6 months",
            "95% uptime SLA",
            "< 2 second page load time"
        ]);

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Success Metrics");
        result.Should().Contain("- 10,000 MAU within 6 months");
        result.Should().Contain("- 95% uptime SLA");
        result.Should().Contain("- < 2 second page load time");
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

    // ─── Personas Section Tests ────────────────────────────────────────────────

    [Fact]
    public void Generate_WithPersonas_ShouldIncludePersonasSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Personas.Add(new Persona
        {
            Id = "persona-001",
            Name = "Chef Maria",
            Description = "Head chef at a mid-size restaurant looking for fresh, local ingredients"
        });
        truthMap.Personas.Add(new Persona
        {
            Id = "persona-002",
            Name = "Farmer Joe",
            Description = "Small-scale organic farmer seeking direct sales channels"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Target Users");
        result.Should().Contain("### Chef Maria");
        result.Should().Contain("Head chef at a mid-size restaurant looking for fresh, local ingredients");
        result.Should().Contain("### Farmer Joe");
        result.Should().Contain("Small-scale organic farmer seeking direct sales channels");
    }

    [Fact]
    public void Generate_WithNoPersonas_ShouldOmitPersonasSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## Target Users");
    }

    // ─── Full Document Structure Tests ─────────────────────────────────────────

    [Fact]
    public void Generate_WithCompleteTruthMap_ShouldProduceWellStructuredDocument()
    {
        // Arrange
        var truthMap = CreateCompleteTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        // Verify section order
        var overviewIndex = result.IndexOf("# Project Overview");
        var constraintsIndex = result.IndexOf("## Constraints");
        var metricsIndex = result.IndexOf("## Success Metrics");
        var usersIndex = result.IndexOf("## Target Users");
        var decisionsIndex = result.IndexOf("## Key Decisions");
        var risksIndex = result.IndexOf("## Risks & Mitigations");
        var assumptionsIndex = result.IndexOf("## Assumptions");

        overviewIndex.Should().BeGreaterThan(-1);
        constraintsIndex.Should().BeGreaterThan(overviewIndex);
        metricsIndex.Should().BeGreaterThan(constraintsIndex);
        usersIndex.Should().BeGreaterThan(metricsIndex);
        decisionsIndex.Should().BeGreaterThan(usersIndex);
        risksIndex.Should().BeGreaterThan(decisionsIndex);
        assumptionsIndex.Should().BeGreaterThan(risksIndex);
    }

    [Fact]
    public void Generate_WithEmptyTruthMap_ShouldReturnMinimalDocument()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().StartWith("---\napplyTo: '**'\n---\n");
        result.Should().Contain("# Development Instructions");
        // Should only have the header and frontmatter
        result.Split('\n').Where(line => line.StartsWith("##")).Should().BeEmpty();
    }

    // ─── Edge Cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_WithSpecialMarkdownCharacters_ShouldNotBreakFormatting()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.CoreIdea = "Build a `code formatter` with *special* **chars** and [links](url)";

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("Build a `code formatter` with *special* **chars** and [links](url)");
    }

    [Fact]
    public void Generate_WithNullTruthMap_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => _generator.Generate(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Generate_WithMultilineDecisionRationale_ShouldPreserveNewlines()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Decisions.Add(new Decision
        {
            Id = "dec-001",
            Text = "Use microservices architecture",
            Rationale = "Key reasons:\n1. Independent scaling\n2. Team autonomy\n3. Technology flexibility",
            Owner = "architect"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("Key reasons:");
        result.Should().Contain("1. Independent scaling");
    }

    // ─── Helper Methods ────────────────────────────────────────────────────────

    private static TruthMapState CreateMinimalTruthMap()
    {
        return TruthMapState.CreateNew(Guid.NewGuid());
    }

    private static TruthMapState CreateCompleteTruthMap()
    {
        var truthMap = TruthMapState.CreateNew(Guid.NewGuid());
        
        truthMap.CoreIdea = "A marketplace connecting local farmers with restaurants";
        
        truthMap.Constraints = new Constraints
        {
            Budget = "$50,000",
            Timeline = "6 months",
            TechStack = ["Next.js", ".NET"],
            NonNegotiables = ["Mobile-first"]
        };
        
        truthMap.SuccessMetrics.Add("10,000 MAU");
        
        truthMap.Personas.Add(new Persona
        {
            Id = "persona-001",
            Name = "Chef Maria",
            Description = "Restaurant owner"
        });
        
        truthMap.Decisions.Add(new Decision
        {
            Id = "dec-001",
            Text = "Event-driven architecture",
            Rationale = "Scalability",
            Owner = "architect"
        });
        
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-001",
            Text = "API rate limits",
            Severity = Severity.High,
            Agent = "claude"
        });
        
        truthMap.Assumptions.Add(new Assumption
        {
            Id = "asm-001",
            Text = "Users have internet",
            Status = AssumptionStatus.Unvalidated
        });

        return truthMap;
    }
}
