using Agon.Domain.Engines;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.Engines;

public class ConfidenceDecayEngineTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    private static ConfidenceDecayEngine BuildEngine(
        float decayStep = 0.15f,
        float boostStep = 0.10f,
        float contestedThreshold = 0.30f) =>
        new(new ConfidenceDecayConfig
        {
            DecayStep = decayStep,
            BoostStep = boostStep,
            ContestedThreshold = contestedThreshold
        });

    private static Agon.Domain.TruthMap.TruthMap MapWithClaim(
        string claimId,
        string agentId,
        float confidence,
        ClaimStatus status = ClaimStatus.Active) =>
        Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Round = 2,
            Claims = new List<Claim>
            {
                new(claimId, agentId, 1, "A claim", confidence, status, [], [])
            }
        };

    private static Agon.Domain.TruthMap.TruthMap MapWithClaimAndEvidence(
        string claimId,
        float confidence,
        string evidenceId) =>
        Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Round = 2,
            Claims = new List<Claim>
            {
                new(claimId, "gpt_agent", 1, "A claim", confidence, ClaimStatus.Active, [], [])
            },
            Evidence = new List<Evidence>
            {
                new(evidenceId, "Study", "https://example.com", DateTimeOffset.UtcNow,
                    "Supports claim", Supports: new[] { claimId }, Contradicts: [])
            }
        };

    // ── No activity — no change ───────────────────────────────────────────────

    [Fact]
    public void Apply_NoChallengeNoEvidence_ConfidenceUnchanged()
    {
        var map = MapWithClaim("c1", "gpt_agent", 0.80f);
        var engine = BuildEngine();

        var (updated, transitions) = engine.Apply(map, RoundActivity.Empty());

        updated.Claims[0].Confidence.Should().BeApproximately(0.80f, 0.001f);
        transitions.Should().BeEmpty();
    }

    // ── Rule 1: Decay on undefended challenge ─────────────────────────────────

    [Fact]
    public void Apply_ChallengedWithNoDefense_ConfidenceDecays()
    {
        var map = MapWithClaim("c1", "gpt_agent", 0.80f);
        var activity = new RoundActivity(
            ChallengedClaimIds: new HashSet<string> { "c1" },
            DefendedClaimIds: new HashSet<string>(),
            NewEvidenceIds: new HashSet<string>());

        var (updated, transitions) = BuildEngine().Apply(map, activity);

        updated.Claims[0].Confidence.Should().BeApproximately(0.65f, 0.001f);
        transitions.Should().HaveCount(1);
        transitions[0].Reason.Should().Be(ConfidenceTransitionReason.ChallengedNoDefense);
        transitions[0].FromConfidence.Should().BeApproximately(0.80f, 0.001f);
        transitions[0].ToConfidence.Should().BeApproximately(0.65f, 0.001f);
    }

    [Fact]
    public void Apply_ChallengedButDefended_ConfidenceUnchanged()
    {
        var map = MapWithClaim("c1", "gpt_agent", 0.80f);
        var activity = new RoundActivity(
            ChallengedClaimIds: new HashSet<string> { "c1" },
            DefendedClaimIds: new HashSet<string> { "c1" },  // defended
            NewEvidenceIds: new HashSet<string>());

        var (updated, transitions) = BuildEngine().Apply(map, activity);

        updated.Claims[0].Confidence.Should().BeApproximately(0.80f, 0.001f);
        transitions.Should().BeEmpty();
    }

    // ── Rule 2: Boost on supporting evidence ─────────────────────────────────

    [Fact]
    public void Apply_NewSupportingEvidence_ConfidenceBoosts()
    {
        var map = MapWithClaimAndEvidence("c1", 0.70f, "e1");
        var activity = new RoundActivity(
            ChallengedClaimIds: new HashSet<string>(),
            DefendedClaimIds: new HashSet<string>(),
            NewEvidenceIds: new HashSet<string> { "e1" });

        var (updated, transitions) = BuildEngine().Apply(map, activity);

        updated.Claims[0].Confidence.Should().BeApproximately(0.80f, 0.001f);
        transitions[0].Reason.Should().Be(ConfidenceTransitionReason.EvidenceCorroboration);
    }

    // ── Evidence boost overrides unanswered challenge ─────────────────────────

    [Fact]
    public void Apply_ChallengedAndNewEvidence_EvidenceWins()
    {
        var map = MapWithClaimAndEvidence("c1", 0.70f, "e1");
        var activity = new RoundActivity(
            ChallengedClaimIds: new HashSet<string> { "c1" },
            DefendedClaimIds: new HashSet<string>(),
            NewEvidenceIds: new HashSet<string> { "e1" });

        var (updated, transitions) = BuildEngine().Apply(map, activity);

        // Decay then boost: 0.70 - 0.15 + 0.10 = 0.65 (evidence boost applied last)
        updated.Claims[0].Confidence.Should().BeApproximately(0.65f, 0.001f);
        transitions[0].Reason.Should().Be(ConfidenceTransitionReason.EvidenceCorroboration);
    }

    // ── Rule 3: Clamp at 0 and 1 ─────────────────────────────────────────────

    [Fact]
    public void Apply_Decay_ClampedAtZero()
    {
        var map = MapWithClaim("c1", "gpt_agent", 0.05f); // very low
        var activity = new RoundActivity(
            new HashSet<string> { "c1" }, new HashSet<string>(), new HashSet<string>());

        var (updated, _) = BuildEngine(decayStep: 0.15f).Apply(map, activity);

        updated.Claims[0].Confidence.Should().BeGreaterThanOrEqualTo(0f);
        updated.Claims[0].Confidence.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Apply_Boost_ClampedAtOne()
    {
        var map = MapWithClaimAndEvidence("c1", 0.98f, "e1"); // very high
        var activity = new RoundActivity(
            new HashSet<string>(), new HashSet<string>(), new HashSet<string> { "e1" });

        var (updated, _) = BuildEngine(boostStep: 0.10f).Apply(map, activity);

        updated.Claims[0].Confidence.Should().BeLessThanOrEqualTo(1f);
        updated.Claims[0].Confidence.Should().BeApproximately(1f, 0.001f);
    }

    // ── Rule 4: Contested threshold ───────────────────────────────────────────

    [Fact]
    public void Apply_ConfidenceDropsBelowThreshold_MarksContested()
    {
        var map = MapWithClaim("c1", "gpt_agent", 0.40f); // will drop to 0.25 after decay
        var activity = new RoundActivity(
            new HashSet<string> { "c1" }, new HashSet<string>(), new HashSet<string>());

        var (updated, _) = BuildEngine(decayStep: 0.15f, contestedThreshold: 0.30f)
            .Apply(map, activity);

        updated.Claims[0].Status.Should().Be(ClaimStatus.Contested);
    }

    [Fact]
    public void Apply_ContestedClaimBoosts_RecoveredToActive()
    {
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId) with
        {
            Round = 2,
            Claims = new List<Claim>
            {
                new("c1", "gpt_agent", 1, "A claim", 0.28f,
                    ClaimStatus.Contested, // already contested
                    [], [])
            },
            Evidence = new List<Evidence>
            {
                new("e1", "Study", "https://example.com", DateTimeOffset.UtcNow,
                    "Supports", new[] { "c1" }, [])
            }
        };

        var activity = new RoundActivity(
            new HashSet<string>(), new HashSet<string>(), new HashSet<string> { "e1" });

        var (updated, _) = BuildEngine(boostStep: 0.15f, contestedThreshold: 0.30f)
            .Apply(map, activity);

        // 0.28 + 0.15 = 0.43 — above threshold → recovered
        updated.Claims[0].Confidence.Should().BeApproximately(0.43f, 0.001f);
        updated.Claims[0].Status.Should().Be(ClaimStatus.Active);
    }

    // ── Transitions are appended to map history ───────────────────────────────

    [Fact]
    public void Apply_Transitions_AppendedToMap()
    {
        var map = MapWithClaim("c1", "gpt_agent", 0.80f);
        var activity = new RoundActivity(
            new HashSet<string> { "c1" }, new HashSet<string>(), new HashSet<string>());

        var (updated, transitions) = BuildEngine().Apply(map, activity);

        updated.ConfidenceTransitions.Should().HaveCount(1);
        updated.ConfidenceTransitions[0].ClaimId.Should().Be("c1");
        transitions.Should().HaveCount(1);
    }

    // ── Empty map ─────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyMap_NoTransitions()
    {
        var map = Agon.Domain.TruthMap.TruthMap.Empty(SessionId);
        var activity = new RoundActivity(
            new HashSet<string> { "ghost" }, new HashSet<string>(), new HashSet<string>());

        var (updated, transitions) = BuildEngine().Apply(map, activity);

        updated.Claims.Should().BeEmpty();
        transitions.Should().BeEmpty();
    }
}
