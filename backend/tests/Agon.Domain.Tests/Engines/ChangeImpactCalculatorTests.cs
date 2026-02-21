using Agon.Domain.Engines;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.Engines;

public class ChangeImpactCalculatorTests
{
    // --- Basic graph traversal ---

    [Fact]
    public void CalculateImpact_ReturnsDirectDependents()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim { Id = "c1", Agent = "a", Round = 1, Text = "Root claim.", Confidence = 0.8f });
        map.Claims.Add(new Claim { Id = "c2", Agent = "b", Round = 1, Text = "Depends on c1.", Confidence = 0.7f, DerivedFrom = new List<string> { "c1" } });

        var impact = ChangeImpactCalculator.CalculateImpact("c1", map);

        impact.Should().Contain("c2");
    }

    [Fact]
    public void CalculateImpact_ReturnsTransitiveDependents()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim { Id = "c1", Agent = "a", Round = 1, Text = "Root.", Confidence = 0.8f });
        map.Claims.Add(new Claim { Id = "c2", Agent = "b", Round = 1, Text = "Depends on c1.", Confidence = 0.7f, DerivedFrom = new List<string> { "c1" } });
        map.Risks.Add(new Risk { Id = "r1", Agent = "c", Text = "Depends on c2.", DerivedFrom = new List<string> { "c2" } });

        var impact = ChangeImpactCalculator.CalculateImpact("c1", map);

        impact.Should().Contain("c2");
        impact.Should().Contain("r1");
    }

    [Fact]
    public void CalculateImpact_DoesNotIncludeSourceEntity()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim { Id = "c1", Agent = "a", Round = 1, Text = "Root.", Confidence = 0.8f });
        map.Claims.Add(new Claim { Id = "c2", Agent = "b", Round = 1, Text = "Depends on c1.", Confidence = 0.7f, DerivedFrom = new List<string> { "c1" } });

        var impact = ChangeImpactCalculator.CalculateImpact("c1", map);

        impact.Should().NotContain("c1");
    }

    [Fact]
    public void CalculateImpact_ReturnsEmptySet_WhenNoDependents()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim { Id = "c1", Agent = "a", Round = 1, Text = "Isolated.", Confidence = 0.8f });

        var impact = ChangeImpactCalculator.CalculateImpact("c1", map);

        impact.Should().BeEmpty();
    }

    [Fact]
    public void CalculateImpact_ReturnsEmptySet_WhenEntityDoesNotExist()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());

        var impact = ChangeImpactCalculator.CalculateImpact("nonexistent", map);

        impact.Should().BeEmpty();
    }

    // --- Cross-entity-type traversal ---

    [Fact]
    public void CalculateImpact_TraversesAcrossEntityTypes()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim { Id = "c1", Agent = "a", Round = 1, Text = "Root claim.", Confidence = 0.8f });
        map.Assumptions.Add(new Assumption { Id = "a1", Text = "Depends on c1.", DerivedFrom = new List<string> { "c1" } });
        map.Decisions.Add(new Decision { Id = "d1", Text = "Depends on a1.", Rationale = "Because.", Owner = "x", DerivedFrom = new List<string> { "a1" } });
        map.Risks.Add(new Risk { Id = "r1", Agent = "y", Text = "Depends on d1.", DerivedFrom = new List<string> { "d1" } });

        var impact = ChangeImpactCalculator.CalculateImpact("c1", map);

        impact.Should().Contain("a1");
        impact.Should().Contain("d1");
        impact.Should().Contain("r1");
        impact.Should().HaveCount(3);
    }

    // --- Diamond dependency (no duplicates) ---

    [Fact]
    public void CalculateImpact_DoesNotDuplicate_WhenDiamondDependency()
    {
        // c1 → c2, c1 → c3, c2 → c4, c3 → c4
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim { Id = "c1", Agent = "a", Round = 1, Text = "Root.", Confidence = 0.8f });
        map.Claims.Add(new Claim { Id = "c2", Agent = "b", Round = 1, Text = "A.", Confidence = 0.7f, DerivedFrom = new List<string> { "c1" } });
        map.Claims.Add(new Claim { Id = "c3", Agent = "c", Round = 1, Text = "B.", Confidence = 0.7f, DerivedFrom = new List<string> { "c1" } });
        map.Claims.Add(new Claim { Id = "c4", Agent = "d", Round = 1, Text = "C.", Confidence = 0.6f, DerivedFrom = new List<string> { "c2", "c3" } });

        var impact = ChangeImpactCalculator.CalculateImpact("c1", map);

        impact.Should().HaveCount(3);
        impact.Should().Contain("c2");
        impact.Should().Contain("c3");
        impact.Should().Contain("c4");
    }

    // --- Circular dependency handling ---

    [Fact]
    public void CalculateImpact_HandlesCircularDependency_WithoutInfiniteLoop()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim { Id = "c1", Agent = "a", Round = 1, Text = "A.", Confidence = 0.8f, DerivedFrom = new List<string> { "c2" } });
        map.Claims.Add(new Claim { Id = "c2", Agent = "b", Round = 1, Text = "B.", Confidence = 0.7f, DerivedFrom = new List<string> { "c1" } });

        var impact = ChangeImpactCalculator.CalculateImpact("c1", map);

        impact.Should().Contain("c2");
        impact.Should().NotContain("c1");
    }

    // --- Evidence supports/contradicts links ---

    [Fact]
    public void CalculateImpact_IncludesEvidenceThatSupportsAffectedClaim()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim { Id = "c1", Agent = "a", Round = 1, Text = "Root.", Confidence = 0.8f });
        map.Evidence.Add(new Evidence { Id = "e1", Title = "Study", Source = "url", Supports = new List<string> { "c1" } });

        // Evidence that supports c1 is impacted when c1 changes
        var impact = ChangeImpactCalculator.CalculateImpact("c1", map);

        impact.Should().Contain("e1");
    }
}
