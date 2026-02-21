using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.TruthMap;

public class PatchValidatorTests
{
    private static TruthMapState CreateMapWithClaim(string claimId = "c1", string agent = "product_strategist")
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Claims.Add(new Claim
        {
            Id = claimId,
            Agent = agent,
            Round = 1,
            Text = "Original claim text.",
            Confidence = 0.8f
        });
        return map;
    }

    private static TruthMapPatch CreatePatch(string agent, int round, params PatchOperation[] ops)
    {
        return new TruthMapPatch
        {
            Ops = ops.ToList(),
            Meta = new PatchMeta
            {
                Agent = agent,
                Round = round,
                Reason = "Test patch",
                SessionId = Guid.NewGuid()
            }
        };
    }

    // --- Rule 1: Reject references to non-existent entity IDs (unless op is add) ---

    [Fact]
    public void Validate_RejectsReplace_OnNonExistentEntity()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        var patch = CreatePatch("contrarian", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Replace,
                Path = "/claims/c_nonexistent/status",
                Value = "contested"
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("c_nonexistent"));
    }

    [Fact]
    public void Validate_RejectsRemove_OnNonExistentEntity()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        var patch = CreatePatch("contrarian", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Remove,
                Path = "/claims/c_nonexistent",
                Value = null
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_AllowsAdd_ForNewEntity()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        var patch = CreatePatch("product_strategist", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Add,
                Path = "/claims/-",
                Value = new Claim
                {
                    Id = "c_new",
                    Agent = "product_strategist",
                    Round = 1,
                    Text = "New claim.",
                    Confidence = 0.7f
                }
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeTrue();
    }

    // --- Rule 2: Reject replace/remove on mismatched entity id ---

    [Fact]
    public void Validate_RejectsReplace_WhenPathEntityIdDoesNotMatchValueId()
    {
        var map = CreateMapWithClaim("c1");
        var patch = CreatePatch("product_strategist", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Replace,
                Path = "/claims/c1",
                Value = new Claim
                {
                    Id = "c_wrong",
                    Agent = "product_strategist",
                    Round = 1,
                    Text = "Updated text.",
                    Confidence = 0.9f
                }
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("mismatch"));
    }

    // --- Rule 3: Prevent cross-agent text modification ---

    [Fact]
    public void Validate_RejectsTextModification_ByDifferentAgent()
    {
        var map = CreateMapWithClaim("c1", "product_strategist");
        var patch = CreatePatch("contrarian", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Replace,
                Path = "/claims/c1/text",
                Value = "Contrarian overwrites Product Strategist's claim text."
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("Cross-agent"));
    }

    [Fact]
    public void Validate_AllowsTextModification_BySameAgent()
    {
        var map = CreateMapWithClaim("c1", "product_strategist");
        var patch = CreatePatch("product_strategist", 2,
            new PatchOperation
            {
                Op = PatchOperationType.Replace,
                Path = "/claims/c1/text",
                Value = "Product Strategist updates own claim text."
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AllowsChallengedByAddition_ByDifferentAgent()
    {
        var map = CreateMapWithClaim("c1", "product_strategist");
        var patch = CreatePatch("contrarian", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Add,
                Path = "/claims/c1/challenged_by/-",
                Value = "r1"
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AllowsStatusChange_ByDifferentAgent()
    {
        var map = CreateMapWithClaim("c1", "product_strategist");
        var patch = CreatePatch("contrarian", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Replace,
                Path = "/claims/c1/status",
                Value = "contested"
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeTrue();
    }

    // --- Rule 4: Require rationale on decisions ---

    [Fact]
    public void Validate_RejectsDecision_WithoutRationale()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        var decision = new Decision
        {
            Id = "d1",
            Text = "Use Postgres.",
            Rationale = "",
            Owner = "technical_architect"
        };
        var patch = CreatePatch("technical_architect", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Add,
                Path = "/decisions/-",
                Value = decision
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("rationale"));
    }

    [Fact]
    public void Validate_AcceptsDecision_WithRationale()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        var decision = new Decision
        {
            Id = "d1",
            Text = "Use Postgres.",
            Rationale = "Supports JSONB for Truth Map storage.",
            Owner = "technical_architect"
        };
        var patch = CreatePatch("technical_architect", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Add,
                Path = "/decisions/-",
                Value = decision
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeTrue();
    }

    // --- Rule 5: Require validation_step on assumptions after Round 2 ---

    [Fact]
    public void Validate_RejectsAssumption_WithoutValidationStep_AfterRound2()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Round = 3;
        var assumption = new Assumption
        {
            Id = "a1",
            Text = "Users want this.",
            ValidationStep = null
        };
        var patch = CreatePatch("product_strategist", 3,
            new PatchOperation
            {
                Op = PatchOperationType.Add,
                Path = "/assumptions/-",
                Value = assumption
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("validation_step"));
    }

    [Fact]
    public void Validate_AcceptsAssumption_WithoutValidationStep_BeforeRound3()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Round = 1;
        var assumption = new Assumption
        {
            Id = "a1",
            Text = "Users want this.",
            ValidationStep = null
        };
        var patch = CreatePatch("product_strategist", 1,
            new PatchOperation
            {
                Op = PatchOperationType.Add,
                Path = "/assumptions/-",
                Value = assumption
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsAssumption_WithValidationStep_AfterRound2()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Round = 3;
        var assumption = new Assumption
        {
            Id = "a1",
            Text = "Users want this.",
            ValidationStep = "Run a survey."
        };
        var patch = CreatePatch("product_strategist", 3,
            new PatchOperation
            {
                Op = PatchOperationType.Add,
                Path = "/assumptions/-",
                Value = assumption
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeTrue();
    }

    // --- Empty patch ---

    [Fact]
    public void Validate_AcceptsEmptyPatch()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        var patch = CreatePatch("product_strategist", 1);

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // --- Multiple errors ---

    [Fact]
    public void Validate_ReturnsMultipleErrors_WhenMultipleRulesViolated()
    {
        var map = TruthMapState.CreateNew(Guid.NewGuid());
        map.Round = 3;
        var patch = CreatePatch("contrarian", 3,
            new PatchOperation
            {
                Op = PatchOperationType.Remove,
                Path = "/claims/c_nonexistent",
                Value = null
            },
            new PatchOperation
            {
                Op = PatchOperationType.Add,
                Path = "/decisions/-",
                Value = new Decision
                {
                    Id = "d1",
                    Text = "Bad decision.",
                    Rationale = "",
                    Owner = "contrarian"
                }
            });

        var result = PatchValidator.Validate(patch, map);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    // --- ValidationResult ---

    [Fact]
    public void ValidationResult_Success_IsValid()
    {
        var result = ValidationResult.Success();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidationResult_Failure_ContainsErrors()
    {
        var result = ValidationResult.Failure(new List<string> { "Error 1", "Error 2" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
    }
}
