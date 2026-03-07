using Agon.Domain.Engines;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.Engines;

public class ChangeImpactCalculatorTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Claim MakeClaim(string id, params string[] derivedFrom) =>
        new(id, "gpt_agent", 1, "Claim", 0.8f, ClaimStatus.Active,
            derivedFrom, []);

    private static Assumption MakeAssumption(string id, params string[] derivedFrom) =>
        new(id, "Assumption text", "Validation step", derivedFrom, AssumptionStatus.Unvalidated);

    private static Risk MakeRisk(string id, params string[] derivedFrom) =>
        new(id, "Risk text", RiskCategory.Technical, RiskSeverity.High, RiskLikelihood.Medium,
            "Mitigation", derivedFrom, "gpt_agent");

    private static Decision MakeDecision(string id, params string[] derivedFrom) =>
        new(id, "Decision text", "Rationale", "gpt_agent", derivedFrom, true);

    // ── Direct dependency ─────────────────────────────────────────────────────

    [Fact]
    public void GetImpactSet_ReturnsDirectDependents()
    {
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Claims = new[] { MakeClaim("c1", "base") },
        };

        var impact = ChangeImpactCalculator.GetImpactSet("base", map);

        impact.Should().Contain("c1");
        impact.Should().NotContain("base"); // source not included
    }

    [Fact]
    public void GetImpactSet_NoMatch_ReturnsEmpty()
    {
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Claims = new[] { MakeClaim("c1", "unrelated") }
        };

        var impact = ChangeImpactCalculator.GetImpactSet("base", map);

        impact.Should().BeEmpty();
    }

    // ── Transitive (multi-hop) traversal ─────────────────────────────────────

    [Fact]
    public void GetImpactSet_Transitive_ThreeHops()
    {
        // base → c1 → c2 → c3
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Claims = new[]
            {
                MakeClaim("c1", "base"),
                MakeClaim("c2", "c1"),
                MakeClaim("c3", "c2")
            }
        };

        var impact = ChangeImpactCalculator.GetImpactSet("base", map);

        impact.Should().Contain("c1").And.Contain("c2").And.Contain("c3");
        impact.Should().HaveCount(3);
    }

    // ── Cross-entity type traversal ───────────────────────────────────────────

    [Fact]
    public void GetImpactSet_CrossEntityTypes_ClaimRiskDecision()
    {
        // claim "c1" derived from "base"
        // risk "r1" derived from "c1"
        // decision "d1" derived from "r1"
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Claims = new[] { MakeClaim("c1", "base") },
            Risks = new[] { MakeRisk("r1", "c1") },
            Decisions = new[] { MakeDecision("d1", "r1") }
        };

        var impact = ChangeImpactCalculator.GetImpactSet("base", map);

        impact.Should().Contain("c1")
            .And.Contain("r1")
            .And.Contain("d1");
    }

    // ── Diamond dependency (shared ancestry) ─────────────────────────────────

    [Fact]
    public void GetImpactSet_Diamond_NoDuplicates()
    {
        // base → c1, base → c2, c3 derived from both c1 and c2
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Claims = new[]
            {
                MakeClaim("c1", "base"),
                MakeClaim("c2", "base"),
                MakeClaim("c3", "c1", "c2")  // depends on both
            }
        };

        var impact = ChangeImpactCalculator.GetImpactSet("base", map);

        impact.Should().Contain("c1").And.Contain("c2").And.Contain("c3");
        impact.Should().HaveCount(3); // no duplicate "c3"
    }

    // ── Cycles (defensive) ───────────────────────────────────────────────────

    [Fact]
    public void GetImpactSet_Cycle_DoesNotLoopInfinitely()
    {
        // c1 ↔ c2 (circular reference)
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Claims = new[]
            {
                MakeClaim("c1", "base", "c2"),
                MakeClaim("c2", "c1")
            }
        };

        var act = () => ChangeImpactCalculator.GetImpactSet("base", map);
        act.Should().NotThrow();

        var impact = act();
        impact.Should().Contain("c1").And.Contain("c2");
    }

    // ── Empty map ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetImpactSet_EmptyMap_ReturnsEmpty()
    {
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId);
        ChangeImpactCalculator.GetImpactSet("any-id", map).Should().BeEmpty();
    }

    // ── Assumption dependencies ───────────────────────────────────────────────

    [Fact]
    public void GetImpactSet_AssumptionDependsOnClaim()
    {
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Claims = new[] { MakeClaim("c1", "base") },
            Assumptions = new[] { MakeAssumption("a1", "c1") }
        };

        var impact = ChangeImpactCalculator.GetImpactSet("base", map);

        impact.Should().Contain("c1").And.Contain("a1");
    }

    // ── Guard clauses ─────────────────────────────────────────────────────────

    [Fact]
    public void GetImpactSet_NullOrEmptyId_Throws()
    {
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId);

        var act1 = () => ChangeImpactCalculator.GetImpactSet(null!, map);
        var act2 = () => ChangeImpactCalculator.GetImpactSet("", map);

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }
}
