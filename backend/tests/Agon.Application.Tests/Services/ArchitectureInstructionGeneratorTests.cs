using Agon.Application.Interfaces;
using Agon.Application.Services;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Application.Tests.Services;

public class ArchitectureInstructionGeneratorTests
{
    private readonly ArchitectureInstructionGenerator _generator = new();

    // ─── Interface Implementation Tests ────────────────────────────────────────

    [Fact]
    public void Generator_ShouldImplementIArtifactGenerator()
    {
        // Assert
        _generator.Should().BeAssignableTo<IArtifactGenerator>();
    }

    [Fact]
    public void Type_ShouldReturnArchitecture()
    {
        // Assert
        _generator.Type.Should().Be(ArtifactType.Architecture);
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

    // ─── Header and Title Tests ────────────────────────────────────────────────

    [Fact]
    public void Generate_ShouldIncludeArchitectureHeader()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.CoreIdea = "A marketplace for local farmers";

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("# Architecture");
    }

    [Fact]
    public void Generate_WithCoreIdea_ShouldIncludeProjectNameInHeader()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.CoreIdea = "FarmConnect - A marketplace for local farmers";

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("# FarmConnect Architecture");
    }

    // ─── Tech Stack Section Tests ──────────────────────────────────────────────

    [Fact]
    public void Generate_WithTechStack_ShouldIncludeHighLevelTopologySection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Constraints.TechStack = ["Next.js", ".NET", "PostgreSQL", "Redis"];

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## High-Level Topology");
        result.Should().Contain("| Layer | Technology |");
    }

    [Fact]
    public void Generate_WithTechStack_ShouldCategoriseTechnologies()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Constraints.TechStack = ["Next.js", "React", ".NET", "PostgreSQL", "Redis", "SignalR"];

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("Frontend");
        result.Should().Contain("Next.js");
        result.Should().Contain("Backend");
        result.Should().Contain(".NET");
        result.Should().Contain("Persistence");
        result.Should().Contain("PostgreSQL");
    }

    [Fact]
    public void Generate_WithEmptyTechStack_ShouldOmitTopologySection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## High-Level Topology");
    }

    // ─── Architecture Decisions Section Tests ──────────────────────────────────

    [Fact]
    public void Generate_WithArchitectureDecisions_ShouldIncludeDecisionsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Decisions.Add(new Decision
        {
            Id = "dec-001",
            Text = "Use event-driven architecture",
            Rationale = "Enables loose coupling and independent scaling of components",
            Owner = "architect",
            Binding = true
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Key Architecture Decisions");
        result.Should().Contain("### Use event-driven architecture");
        result.Should().Contain("Enables loose coupling and independent scaling of components");
    }

    [Fact]
    public void Generate_WithBindingDecision_ShouldMarkAsBinding()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Decisions.Add(new Decision
        {
            Id = "dec-001",
            Text = "Microservices architecture",
            Rationale = "Team autonomy",
            Owner = "architect",
            Binding = true
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("**Status:** 🔒 Binding");
    }

    [Fact]
    public void Generate_WithAdvisoryDecision_ShouldMarkAsAdvisory()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Decisions.Add(new Decision
        {
            Id = "dec-001",
            Text = "Use GraphQL for API",
            Rationale = "Flexible queries",
            Owner = "architect",
            Binding = false
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("**Status:** Advisory");
    }

    // ─── Technical Risks Section Tests ─────────────────────────────────────────

    [Fact]
    public void Generate_WithTechnicalRisks_ShouldIncludeTechnicalRisksSection()
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
            Mitigation = "Implement read replicas and caching layer",
            Agent = "claude"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Technical Risks");
        result.Should().Contain("### Database scaling bottleneck");
        result.Should().Contain("**Mitigation:** Implement read replicas and caching layer");
    }

    [Fact]
    public void Generate_ShouldOnlyIncludeTechnicalRisks()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-tech",
            Text = "API rate limiting",
            Category = RiskCategory.Technical,
            Agent = "claude"
        });
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-market",
            Text = "Market competition",
            Category = RiskCategory.Market,
            Agent = "gpt"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("API rate limiting");
        result.Should().NotContain("Market competition");
    }

    [Fact]
    public void Generate_WithNoTechnicalRisks_ShouldOmitRisksSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Risks.Add(new Risk
        {
            Id = "risk-market",
            Text = "Market risk only",
            Category = RiskCategory.Market,
            Agent = "gpt"
        });

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## Technical Risks");
    }

    // ─── Constraints Section Tests ─────────────────────────────────────────────

    [Fact]
    public void Generate_WithNonNegotiables_ShouldIncludeConstraintsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();
        truthMap.Constraints.NonNegotiables = ["GDPR compliance", "99.9% uptime SLA", "Mobile-first"];

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().Contain("## Architectural Constraints");
        result.Should().Contain("- GDPR compliance");
        result.Should().Contain("- 99.9% uptime SLA");
        result.Should().Contain("- Mobile-first");
    }

    [Fact]
    public void Generate_WithNoNonNegotiables_ShouldOmitConstraintsSection()
    {
        // Arrange
        var truthMap = CreateMinimalTruthMap();

        // Act
        var result = _generator.Generate(truthMap);

        // Assert
        result.Should().NotContain("## Architectural Constraints");
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
        var headerIndex = result.IndexOf("# ");
        var topologyIndex = result.IndexOf("## High-Level Topology");
        var constraintsIndex = result.IndexOf("## Architectural Constraints");
        var decisionsIndex = result.IndexOf("## Key Architecture Decisions");
        var risksIndex = result.IndexOf("## Technical Risks");

        headerIndex.Should().BeGreaterThan(-1);
        topologyIndex.Should().BeGreaterThan(headerIndex);
        constraintsIndex.Should().BeGreaterThan(topologyIndex);
        decisionsIndex.Should().BeGreaterThan(constraintsIndex);
        risksIndex.Should().BeGreaterThan(decisionsIndex);
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

        truthMap.CoreIdea = "FarmConnect - A marketplace for local farmers";

        truthMap.Constraints = new Constraints
        {
            TechStack = ["Next.js", ".NET", "PostgreSQL"],
            NonNegotiables = ["GDPR compliance", "Mobile-first"]
        };

        truthMap.Decisions.Add(new Decision
        {
            Id = "dec-001",
            Text = "Event-driven architecture",
            Rationale = "Loose coupling",
            Owner = "architect",
            Binding = true
        });

        truthMap.Risks.Add(new Risk
        {
            Id = "risk-001",
            Text = "Database bottleneck",
            Category = RiskCategory.Technical,
            Severity = Severity.High,
            Agent = "claude"
        });

        return truthMap;
    }
}
