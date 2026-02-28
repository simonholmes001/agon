using Agon.Domain.Engines;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.Engines;

public class ConfidenceDecayEngineTests
{
    private static readonly ConfidenceDecayConfig DefaultConfig = new();

    // --- Decay on undefended challenge ---

    [Fact]
    public void ApplyDecay_ReducesConfidenceByDecayStep()
    {
        var claim = CreateClaim(confidence: 0.8f);

        var result = ConfidenceDecayEngine.ApplyDecay(claim, DefaultConfig);

        result.To.Should().BeApproximately(0.65f, 0.001f); // 0.8 - 0.15
        result.Reason.Should().Be(ConfidenceTransitionReason.ChallengedNoDefense);
    }

    [Fact]
    public void ApplyDecay_ClampsToZero()
    {
        var claim = CreateClaim(confidence: 0.1f);

        var result = ConfidenceDecayEngine.ApplyDecay(claim, DefaultConfig);

        result.To.Should().Be(0.0f);
    }

    [Fact]
    public void ApplyDecay_FromZero_StaysAtZero()
    {
        var claim = CreateClaim(confidence: 0.0f);

        var result = ConfidenceDecayEngine.ApplyDecay(claim, DefaultConfig);

        result.To.Should().Be(0.0f);
    }

    // --- Boost on evidence ---

    [Fact]
    public void ApplyBoost_IncreasesConfidenceByBoostStep()
    {
        var claim = CreateClaim(confidence: 0.6f);

        var result = ConfidenceDecayEngine.ApplyBoost(claim, DefaultConfig);

        result.To.Should().BeApproximately(0.7f, 0.001f); // 0.6 + 0.10
        result.Reason.Should().Be(ConfidenceTransitionReason.EvidenceCorroboration);
    }

    [Fact]
    public void ApplyBoost_ClampsToOne()
    {
        var claim = CreateClaim(confidence: 0.95f);

        var result = ConfidenceDecayEngine.ApplyBoost(claim, DefaultConfig);

        result.To.Should().Be(1.0f);
    }

    [Fact]
    public void ApplyBoost_FromOne_StaysAtOne()
    {
        var claim = CreateClaim(confidence: 1.0f);

        var result = ConfidenceDecayEngine.ApplyBoost(claim, DefaultConfig);

        result.To.Should().Be(1.0f);
    }

    // --- Contested threshold flagging ---

    [Fact]
    public void IsContested_ReturnsTrueWhenBelowThreshold()
    {
        var result = ConfidenceDecayEngine.IsContested(0.25f, DefaultConfig);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsContested_ReturnsFalseWhenAboveThreshold()
    {
        var result = ConfidenceDecayEngine.IsContested(0.5f, DefaultConfig);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsContested_ReturnsTrueWhenExactlyAtThreshold()
    {
        // At threshold (0.30) should be contested — it hasn't cleared the bar
        var result = ConfidenceDecayEngine.IsContested(0.30f, DefaultConfig);

        result.Should().BeTrue();
    }

    // --- Transition records ---

    [Fact]
    public void ApplyDecay_ReturnsCorrectTransition()
    {
        var claim = CreateClaim(id: "c1", confidence: 0.8f, round: 2);

        var result = ConfidenceDecayEngine.ApplyDecay(claim, DefaultConfig);

        result.ClaimId.Should().Be("c1");
        result.From.Should().Be(0.8f);
        result.Round.Should().Be(2);
    }

    [Fact]
    public void ApplyBoost_ReturnsCorrectTransition()
    {
        var claim = CreateClaim(id: "c2", confidence: 0.5f, round: 1);

        var result = ConfidenceDecayEngine.ApplyBoost(claim, DefaultConfig);

        result.ClaimId.Should().Be("c2");
        result.From.Should().Be(0.5f);
        result.Round.Should().Be(1);
    }

    // --- Custom config ---

    [Fact]
    public void ApplyDecay_UsesCustomDecayStep()
    {
        var config = new ConfidenceDecayConfig { DecayStep = 0.25f };
        var claim = CreateClaim(confidence: 0.8f);

        var result = ConfidenceDecayEngine.ApplyDecay(claim, config);

        result.To.Should().BeApproximately(0.55f, 0.001f);
    }

    [Fact]
    public void ApplyBoost_UsesCustomBoostStep()
    {
        var config = new ConfidenceDecayConfig { BoostStep = 0.20f };
        var claim = CreateClaim(confidence: 0.6f);

        var result = ConfidenceDecayEngine.ApplyBoost(claim, config);

        result.To.Should().BeApproximately(0.8f, 0.001f);
    }

    [Fact]
    public void IsContested_UsesCustomThreshold()
    {
        var config = new ConfidenceDecayConfig { ContestedThreshold = 0.50f };

        ConfidenceDecayEngine.IsContested(0.45f, config).Should().BeTrue();
        ConfidenceDecayEngine.IsContested(0.55f, config).Should().BeFalse();
    }

    // --- Config defaults ---

    [Fact]
    public void DefaultConfig_HasCorrectValues()
    {
        var config = new ConfidenceDecayConfig();

        config.DecayStep.Should().Be(0.15f);
        config.BoostStep.Should().Be(0.10f);
        config.ContestedThreshold.Should().Be(0.30f);
    }

    private static Claim CreateClaim(float confidence, string id = "c1", int round = 1)
    {
        return new Claim
        {
            Id = id,
            Agent = "gpt_agent",
            Round = round,
            Text = "Test claim.",
            Confidence = confidence
        };
    }
}
