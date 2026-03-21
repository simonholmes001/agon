using Agon.Domain.Snapshots;
using Agon.Domain.TruthMap;
using FluentAssertions;

namespace Agon.Domain.Tests;

/// <summary>
/// Unit tests for domain model entities that don't have dedicated test files:
/// ForkRequest, SessionSnapshot, and other minor domain types.
/// </summary>
public class DomainEntityTests
{
    // ── ForkRequest ────────────────────────────────────────────────────────────

    [Fact]
    public void ForkRequest_Validate_WhenAllFieldsValid_ReturnsNull()
    {
        // Arrange
        var request = new ForkRequest(
            ParentSessionId: Guid.NewGuid(),
            SnapshotId: Guid.NewGuid(),
            InitialPatches: [],
            Label: "What if budget is halved?");

        // Act
        var error = request.Validate();

        // Assert
        error.Should().BeNull();
    }

    [Fact]
    public void ForkRequest_Validate_WhenParentSessionIdIsEmpty_ReturnsError()
    {
        // Arrange
        var request = new ForkRequest(
            ParentSessionId: Guid.Empty,
            SnapshotId: Guid.NewGuid(),
            InitialPatches: [],
            Label: "Scenario label");

        // Act
        var error = request.Validate();

        // Assert
        error.Should().NotBeNull();
        error.Should().Contain("ParentSessionId");
    }

    [Fact]
    public void ForkRequest_Validate_WhenSnapshotIdIsEmpty_ReturnsError()
    {
        // Arrange
        var request = new ForkRequest(
            ParentSessionId: Guid.NewGuid(),
            SnapshotId: Guid.Empty,
            InitialPatches: [],
            Label: "Scenario label");

        // Act
        var error = request.Validate();

        // Assert
        error.Should().NotBeNull();
        error.Should().Contain("SnapshotId");
    }

    [Fact]
    public void ForkRequest_Validate_WhenLabelIsNull_ReturnsError()
    {
        // Arrange
        var request = new ForkRequest(
            ParentSessionId: Guid.NewGuid(),
            SnapshotId: Guid.NewGuid(),
            InitialPatches: [],
            Label: null!);

        // Act
        var error = request.Validate();

        // Assert
        error.Should().NotBeNull();
        error.Should().Contain("Label");
    }

    [Fact]
    public void ForkRequest_Validate_WhenLabelIsWhitespace_ReturnsError()
    {
        // Arrange
        var request = new ForkRequest(
            ParentSessionId: Guid.NewGuid(),
            SnapshotId: Guid.NewGuid(),
            InitialPatches: [],
            Label: "   ");

        // Act
        var error = request.Validate();

        // Assert
        error.Should().NotBeNull();
        error.Should().Contain("Label");
    }

    [Fact]
    public void ForkRequest_Validate_WithInitialPatches_IsValid()
    {
        // Arrange
        var patches = new[]
        {
            new TruthMapPatch(
                [new PatchOperation(PatchOp.Add, "/claims/0", null)],
                new PatchMeta("user", 1, "Override budget constraint", Guid.NewGuid()))
        };

        var request = new ForkRequest(
            ParentSessionId: Guid.NewGuid(),
            SnapshotId: Guid.NewGuid(),
            InitialPatches: patches,
            Label: "Budget halved scenario");

        // Act
        var error = request.Validate();

        // Assert
        error.Should().BeNull();
    }

    // ── SessionSnapshot ────────────────────────────────────────────────────────

    [Fact]
    public void SessionSnapshot_Create_ProducesValidSnapshot()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = Agon.Domain.TruthMap.TruthMap.Empty(sessionId);

        // Act
        var snapshot = SessionSnapshot.Create(truthMap, 2);

        // Assert
        snapshot.SnapshotId.Should().NotBeEmpty();
        snapshot.SessionId.Should().Be(sessionId);
        snapshot.Round.Should().Be(2);
        snapshot.TruthMapHash.Should().NotBeNullOrEmpty();
        snapshot.TruthMap.Should().Be(truthMap);
    }

    [Fact]
    public void SessionSnapshot_IsIntact_WhenUnmodified_ReturnsTrue()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = Agon.Domain.TruthMap.TruthMap.Empty(sessionId);
        var snapshot = SessionSnapshot.Create(truthMap, 1);

        // Act & Assert
        snapshot.IsIntact().Should().BeTrue();
    }

    // ── PatchMeta ────────────────────────────────────────────────────────────

    [Fact]
    public void PatchMeta_Properties_AreAccessible()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var meta = new PatchMeta("gpt_agent", 3, "Challenging assumption", sessionId);

        // Assert
        meta.Agent.Should().Be("gpt_agent");
        meta.Round.Should().Be(3);
        meta.Reason.Should().Be("Challenging assumption");
        meta.SessionId.Should().Be(sessionId);
    }

    [Fact]
    public void PatchOperation_Properties_AreAccessible()
    {
        // Arrange
        var op = new PatchOperation(PatchOp.Replace, "/claims/0/status", "contested");

        // Assert
        op.Op.Should().Be(PatchOp.Replace);
        op.Path.Should().Be("/claims/0/status");
        op.Value.Should().Be("contested");
    }

    [Fact]
    public void PatchOperation_Remove_HasNullValue()
    {
        // Arrange
        var op = new PatchOperation(PatchOp.Remove, "/claims/0", null);

        // Assert
        op.Op.Should().Be(PatchOp.Remove);
        op.Value.Should().BeNull();
    }

    // ── TruthMapPatch ────────────────────────────────────────────────────────

    [Fact]
    public void TruthMapPatch_WithEmptyOps_IsValid()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch(
            Ops: [],
            Meta: new PatchMeta("gpt_agent", 1, "No-op patch", sessionId));

        // Assert
        patch.Ops.Should().BeEmpty();
        patch.Meta.Agent.Should().Be("gpt_agent");
    }

    [Fact]
    public void TruthMapPatch_WithMultipleOps_AllAccessible()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch(
            [
                new PatchOperation(PatchOp.Add, "/claims/0", "claim data"),
                new PatchOperation(PatchOp.Add, "/risks/0", "risk data"),
                new PatchOperation(PatchOp.Replace, "/convergence/overall", 0.8)
            ],
            new PatchMeta("gpt_agent", 1, "Multi-op", sessionId));

        // Assert
        patch.Ops.Should().HaveCount(3);
    }

    // ── ConfidenceDecayConfig ─────────────────────────────────────────────────

    [Fact]
    public void ConfidenceDecayConfig_DefaultValues_AreExpected()
    {
        // Arrange & Act
        var config = new Agon.Domain.Engines.ConfidenceDecayConfig();

        // Assert
        config.DecayStep.Should().BeGreaterThan(0);
        config.BoostStep.Should().BeGreaterThan(0);
        config.ContestedThreshold.Should().BeGreaterThan(0);
        config.ContestedThreshold.Should().BeLessThan(1);
    }
}
