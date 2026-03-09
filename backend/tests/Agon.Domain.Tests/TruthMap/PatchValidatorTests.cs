using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;
using System.Text.Json;

namespace Agon.Domain.Tests.TruthMap;

public class PatchValidatorTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Agon.Domain.TruthMap.TruthMap EmptyMap() =>
        Agon.Domain.TruthMap.TruthMap.Empty(SessionId);

    private static Agon.Domain.TruthMap.TruthMap MapWithClaim(
        string claimId = "c1",
        string agentId = "gpt_agent",
        float confidence = 0.8f) =>
        EmptyMap() with
        {
            Claims = new List<Claim>
            {
                new(claimId, agentId, 1, "A sample claim", confidence,
                    ClaimStatus.Active, [], [])
            }
        };

    private static PatchMeta Meta(string agent = "gpt_agent", int round = 1) =>
        new(agent, round, "test reason", SessionId);

    // ── Rule 1: Non-add ops must reference existing entity IDs ────────────────

    [Fact]
    public void Rule1_Replace_OnNonExistentEntity_IsRejected()
    {
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Replace, "/claims/nonexistent-id/status", "contested")],
            Meta());

        var result = PatchValidator.Validate(patch, EmptyMap());

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("nonexistent-id");
    }

    [Fact]
    public void Rule1_Remove_OnNonExistentEntity_IsRejected()
    {
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Remove, "/claims/ghost-id", null)],
            Meta());

        var result = PatchValidator.Validate(patch, EmptyMap());

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("ghost-id");
    }

    [Fact]
    public void Rule1_Add_WithAnyPath_IsAlwaysAllowedRegardingRule1()
    {
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/-", new
            {
                Id = "c-new",
                ProposedBy = "gpt_agent",
                Round = 1,
                Text = "New claim",
                Confidence = 0.7f,
                Status = "active",
                DerivedFrom = Array.Empty<string>(),
                ChallengedBy = Array.Empty<string>()
            })],
            Meta());

        var result = PatchValidator.Validate(patch, EmptyMap());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rule1_Replace_OnExistingEntity_Passes()
    {
        var map = MapWithClaim("c1");
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Replace, "/claims/c1/status", "contested")],
            Meta());

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeTrue();
    }

    // ── Rule 2: Replace/remove on path with mismatched ID ────────────────────

    [Fact]
    public void Rule2_Replace_OnExistingId_IsValid()
    {
        var map = MapWithClaim("c1");
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Replace, "/claims/c1/confidence", 0.5f)],
            Meta());

        PatchValidator.Validate(patch, map).IsValid.Should().BeTrue();
    }

    // ── Rule 3: Cross-agent text modification prevention ─────────────────────

    [Fact]
    public void Rule3_DifferentAgent_CannotModifyClaimText()
    {
        var map = MapWithClaim("c1", agentId: "gpt_agent");
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Replace, "/claims/c1/text", "Overwritten text")],
            Meta(agent: "claude_agent")); // different agent

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("claude_agent");
        result.Reason.Should().Contain("gpt_agent");
    }

    [Fact]
    public void Rule3_SameAgent_CanModifyOwnClaimText()
    {
        var map = MapWithClaim("c1", agentId: "gpt_agent");
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Replace, "/claims/c1/text", "Updated text")],
            Meta(agent: "gpt_agent")); // same agent

        PatchValidator.Validate(patch, map).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rule3_DifferentAgent_CanAdd_ChallengedBy_Reference()
    {
        // Adding challenged_by is allowed — it's a different field, not /text.
        var map = MapWithClaim("c1", agentId: "gpt_agent");
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/c1/challenged_by/-", "r-risk-1")],
            Meta(agent: "claude_agent"));

        PatchValidator.Validate(patch, map).IsValid.Should().BeTrue();
    }

    // ── Rule 4: Decisions require rationale ──────────────────────────────────

    [Fact]
    public void Rule4_AddDecision_WithoutRationale_IsRejected()
    {
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/decisions/-", new
            {
                Id = "d1",
                Text = "Use microservices",
                Rationale = "",  // empty
                Owner = "gpt_agent",
                DerivedFrom = Array.Empty<string>(),
                Binding = true
            })],
            Meta());

        var result = PatchValidator.Validate(patch, EmptyMap());

        result.IsValid.Should().BeFalse();
        result.Reason.Should().ContainAny("rationale", "Rationale");
    }

    [Fact]
    public void Rule4_AddDecision_WithRationale_IsValid()
    {
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/decisions/-", new
            {
                Id = "d1",
                Text = "Use microservices",
                Rationale = "Enables independent scaling of services.",
                Owner = "gpt_agent",
                DerivedFrom = Array.Empty<string>(),
                Binding = true
            })],
            Meta());

        PatchValidator.Validate(patch, EmptyMap()).IsValid.Should().BeTrue();
    }

    // ── Rule 5: Assumptions require validation_step after Round 2 ────────────

    [Fact]
    public void Rule5_AddAssumption_WithoutValidationStep_AfterRound2_IsRejected()
    {
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/assumptions/-", new
            {
                Id = "a1",
                Text = "Users will pay $10/month",
                ValidationStep = "",  // missing
                DerivedFrom = Array.Empty<string>(),
                Status = "unvalidated"
            })],
            Meta(round: 3)); // Round 3 — validation_step is required

        var result = PatchValidator.Validate(patch, EmptyMap());

        result.IsValid.Should().BeFalse();
        result.Reason.Should().ContainAny("validation_step", "ValidationStep");
    }

    [Fact]
    public void Rule5_AddAssumption_WithoutValidationStep_InRound1_IsValid()
    {
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/assumptions/-", new
            {
                Id = "a1",
                Text = "Users will pay $10/month",
                ValidationStep = "",  // missing — but it's round 1, so OK
                DerivedFrom = Array.Empty<string>(),
                Status = "unvalidated"
            })],
            Meta(round: 1));

        PatchValidator.Validate(patch, EmptyMap()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rule5_AddAssumption_WithValidationStep_AfterRound2_IsValid()
    {
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/assumptions/-", new
            {
                Id = "a1",
                Text = "Users will pay $10/month",
                ValidationStep = "Run 20 user interviews; ask willingness-to-pay question.",
                DerivedFrom = Array.Empty<string>(),
                Status = "unvalidated"
            })],
            Meta(round: 3));

        PatchValidator.Validate(patch, EmptyMap()).IsValid.Should().BeTrue();
    }

    // ── Multiple ops — first invalid op stops validation ─────────────────────

    [Fact]
    public void MultipleOps_FirstInvalidOp_ShortCircuits()
    {
        var patch = new TruthMapPatch(
            [
                new PatchOperation(PatchOp.Replace, "/claims/ghost/status", "contested"), // invalid
                new PatchOperation(PatchOp.Add, "/claims/-", new { Id = "c2" })           // valid
            ],
            Meta());

        var result = PatchValidator.Validate(patch, EmptyMap());

        result.IsValid.Should().BeFalse();
    }

    // ── Empty patch is always valid ───────────────────────────────────────────

    [Fact]
    public void EmptyPatch_IsValid()
    {
        var patch = new TruthMapPatch([], Meta());
        PatchValidator.Validate(patch, EmptyMap()).IsValid.Should().BeTrue();
    }

    // ── JsonElement value (real LLM deserialization path) ────────────────────

    [Fact]
    public void Rule4_AddDecision_WithRationale_AsJsonElement_IsValid()
    {
        // This simulates what System.Text.Json produces when Value is typed as object?
        var json = """{"ops":[{"op":"add","path":"/decisions/-","value":{"id":"d1","text":"Use iOS-first","rationale":"Largest user base for the target demographic."}}],"meta":{"agent":"gpt_agent","round":1,"reason":"analysis","sessionId":"00000000-0000-0000-0000-000000000001"}}""";
        var patch = JsonSerializer.Deserialize<TruthMapPatch>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var result = PatchValidator.Validate(patch, EmptyMap());

        result.IsValid.Should().BeTrue("JsonElement with rationale should pass Rule 4");
    }

    [Fact]
    public void Rule4_AddDecision_WithoutRationale_AsJsonElement_IsRejected()
    {
        var json = """{"ops":[{"op":"add","path":"/decisions/-","value":{"id":"d1","text":"Use iOS-first"}}],"meta":{"agent":"gpt_agent","round":1,"reason":"analysis","sessionId":"00000000-0000-0000-0000-000000000001"}}""";
        var patch = JsonSerializer.Deserialize<TruthMapPatch>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var result = PatchValidator.Validate(patch, EmptyMap());

        result.IsValid.Should().BeFalse("JsonElement without rationale should fail Rule 4");
        result.Reason.Should().ContainAny("rationale", "Rationale");
    }
}
