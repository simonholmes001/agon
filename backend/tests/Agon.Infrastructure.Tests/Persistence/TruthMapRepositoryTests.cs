using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.Persistence.PostgreSQL;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Infrastructure.Tests.Persistence;

/// <summary>
/// Unit tests for Truth Map Repository using in-memory database (no Docker required).
/// </summary>
    public class TruthMapRepositoryTests : IDisposable
{
    private readonly AgonDbContext _dbContext;
    private readonly ITruthMapRepository _repository;

    public TruthMapRepositoryTests()
    {
        // Use in-memory database for unit tests (no Docker required)
        var options = new DbContextOptionsBuilder<AgonDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _dbContext = new AgonDbContext(options);
        _repository = new TruthMapRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetAsync_WhenTruthMapDoesNotExist_ReturnsNewTruthMap()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        var result = await _repository.GetAsync(sessionId);

        // Assert
        result.Should().NotBeNull();
        result.SessionId.Should().Be(sessionId);
        result.Claims.Should().BeEmpty();
        result.Risks.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyPatchAsync_WithNewSession_CreatesTruthMapAndRecordsPatchEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch(
            [
                new PatchOperation(PatchOp.Add, "/claims/0", BuildClaim("claim-1", "Test claim", 1, "gpt-agent"))
            ],
            new PatchMeta("gpt-agent", 1, "Initial claim", sessionId)
        );

        // Act
        await _repository.ApplyPatchAsync(sessionId, patch);

        // Assert
        var entity = await _dbContext.TruthMaps.FirstOrDefaultAsync(tm => tm.SessionId == sessionId);
        entity.Should().NotBeNull();
        entity!.Version.Should().Be(1);

        var patchEvents = await _dbContext.TruthMapPatchEvents
            .Where(e => e.SessionId == sessionId)
            .ToListAsync();
        patchEvents.Should().HaveCount(1);
        patchEvents[0].Agent.Should().Be("gpt-agent");
        patchEvents[0].Round.Should().Be(1);
    }

    [Fact]
    public async Task ApplyPatchAsync_WithExistingTruthMap_UpdatesVersionAndRecordsEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        
        var patch1 = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/0", BuildClaim("claim-1", "Claim 1", 1, "gpt-agent"))],
            new PatchMeta("gpt-agent", 1, "First patch", sessionId)
        );
        
        var patch2 = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/1", BuildClaim("claim-2", "Claim 2", 2, "gemini-agent"))],
            new PatchMeta("gemini-agent", 2, "Second patch", sessionId)
        );

        // Act
        await _repository.ApplyPatchAsync(sessionId, patch1);
        await _repository.ApplyPatchAsync(sessionId, patch2);

        // Assert
        var entity = await _dbContext.TruthMaps.FirstOrDefaultAsync(tm => tm.SessionId == sessionId);
        entity.Should().NotBeNull();
        entity!.Version.Should().Be(2);

        var patchEvents = await _dbContext.TruthMapPatchEvents
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.AppliedAt)
            .ToListAsync();
        
        patchEvents.Should().HaveCount(2);
        patchEvents[0].Agent.Should().Be("gpt-agent");
        patchEvents[1].Agent.Should().Be("gemini-agent");
    }

    [Fact]
    public async Task GetPatchHistoryAsync_ReturnsEventsInChronologicalOrder()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        
        var patches = new[]
        {
            new TruthMapPatch(
                [new PatchOperation(PatchOp.Add, "/claims/0", BuildClaim("claim-1", "Claim 1", 1, "gpt-agent"))],
                new PatchMeta("gpt-agent", 1, "First", sessionId)
            ),
            new TruthMapPatch(
                [new PatchOperation(PatchOp.Add, "/claims/1", BuildClaim("claim-2", "Claim 2", 1, "gemini-agent"))],
                new PatchMeta("gemini-agent", 1, "Second", sessionId)
            ),
            new TruthMapPatch(
                [new PatchOperation(PatchOp.Add, "/risks/0", BuildRisk("risk-1", "Risk 1", "claude-agent", "claim-1"))],
                new PatchMeta("claude-agent", 2, "Third", sessionId)
            )
        };

        foreach (var patch in patches)
        {
            await _repository.ApplyPatchAsync(sessionId, patch);
            await Task.Delay(10); // Small delay to ensure distinct timestamps
        }

        // Act
        var history = await _repository.GetPatchHistoryAsync(sessionId);

        // Assert
        history.Should().HaveCount(3);
        history[0].Meta.Agent.Should().Be("gpt-agent");
        history[1].Meta.Agent.Should().Be("gemini-agent");
        history[2].Meta.Agent.Should().Be("claude-agent");
    }

    [Fact]
    public async Task GetAsync_AfterMultiplePatches_ReturnsCurrentState()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        
        var patches = new[]
        {
            new TruthMapPatch(
                [new PatchOperation(PatchOp.Add, "/claims/0", BuildClaim("claim-1", "Initial claim", 1, "gpt-agent"))],
                new PatchMeta("gpt-agent", 1, "Add claim", sessionId)
            ),
            new TruthMapPatch(
                [new PatchOperation(PatchOp.Add, "/risks/0", BuildRisk("risk-1", "Initial risk", "gemini-agent", "claim-1"))],
                new PatchMeta("gemini-agent", 1, "Add risk", sessionId)
            )
        };

        foreach (var patch in patches)
        {
            await _repository.ApplyPatchAsync(sessionId, patch);
        }

        // Act
        var truthMap = await _repository.GetAsync(sessionId);

        // Assert
        truthMap.Should().NotBeNull();
        truthMap.SessionId.Should().Be(sessionId);
        
        var entity = await _dbContext.TruthMaps.FirstOrDefaultAsync(tm => tm.SessionId == sessionId);
        entity.Should().NotBeNull();
        entity!.Version.Should().Be(2);
    }

    [Fact]
    public async Task SaveAsync_WithoutApplyingPatch_SavesTruthMapWithoutVersionIncrement()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);

        // Act
        await _repository.SaveAsync(truthMap);

        // Assert
        var entity = await _dbContext.TruthMaps.FirstOrDefaultAsync(tm => tm.SessionId == sessionId);
        entity.Should().NotBeNull();
        entity!.Version.Should().Be(0); // SaveAsync doesn't increment version
        entity!.CurrentState.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ApplyPatchAsync_WithOpenQuestionAsString_NormalizesAndPersists()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/open_questions/0", "What data should we collect first?")],
            new PatchMeta("moderator", 1, "Add open question as scalar", sessionId)
        );

        // Act
        var updated = await _repository.ApplyPatchAsync(sessionId, patch);

        // Assert
        updated.OpenQuestions.Should().HaveCount(1);
        updated.OpenQuestions[0].Text.Should().Be("What data should we collect first?");
        updated.OpenQuestions[0].RaisedBy.Should().Be("moderator");
        updated.OpenQuestions[0].Blocking.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyPatchAsync_WithOpenQuestionAlternativeFields_NormalizesAndPersists()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch(
            [
                new PatchOperation(PatchOp.Add, "/open_questions/0", new
                {
                    question = "Who is the first launch cohort?",
                    is_blocking = "true",
                    agent = "moderator"
                })
            ],
            new PatchMeta("moderator", 1, "Add open question with non-canonical fields", sessionId)
        );

        // Act
        var updated = await _repository.ApplyPatchAsync(sessionId, patch);

        // Assert
        updated.OpenQuestions.Should().HaveCount(1);
        updated.OpenQuestions[0].Text.Should().Be("Who is the first launch cohort?");
        updated.OpenQuestions[0].RaisedBy.Should().Be("moderator");
        updated.OpenQuestions[0].Blocking.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyPatchAsync_WithMalformedOpenQuestionObject_DropsInvalidEntry()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch(
            [
                new PatchOperation(PatchOp.Add, "/open_questions/0", new
                {
                    title = "Unsupported key should not crash deserialization",
                    priority = "high"
                })
            ],
            new PatchMeta("moderator", 1, "Add malformed open question object", sessionId)
        );

        // Act
        var updated = await _repository.ApplyPatchAsync(sessionId, patch);

        // Assert
        updated.OpenQuestions.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyPatchAsync_WithNonStringOpenQuestionScalar_DropsInvalidEntry()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/open_questions/0", 42)],
            new PatchMeta("moderator", 1, "Add malformed open question scalar", sessionId)
        );

        // Act
        var updated = await _repository.ApplyPatchAsync(sessionId, patch);

        // Assert
        updated.OpenQuestions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetImpactSetAsync_ReturnsTransitiveDependents()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var claimId = "claim-1";
        var assumptionId = "assumption-1";
        var decisionId = "decision-1";

        var claimPatch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/0", BuildClaim(claimId, "Claim", 1, "gpt-agent"))],
            new PatchMeta("gpt-agent", 1, "Seed claim", sessionId)
        );
        var assumptionPatch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/assumptions/0", BuildAssumption(assumptionId, "Assumption", claimId))],
            new PatchMeta("gemini-agent", 2, "Derived assumption", sessionId)
        );
        var decisionPatch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/decisions/0", BuildDecision(decisionId, "Decision", assumptionId))],
            new PatchMeta("claude-agent", 3, "Derived decision", sessionId)
        );

        await _repository.ApplyPatchAsync(sessionId, claimPatch);
        await _repository.ApplyPatchAsync(sessionId, assumptionPatch);
        await _repository.ApplyPatchAsync(sessionId, decisionPatch);

        // Act
        var impactSet = await _repository.GetImpactSetAsync(sessionId, claimId);

        // Assert
        impactSet.Should().NotBeNull();
        impactSet.Should().Contain(assumptionId);
        impactSet.Should().Contain(decisionId);
    }

    private static object BuildClaim(string id, string text, int round, string proposedBy) => new
    {
        id,
        proposed_by = proposedBy,
        round,
        text,
        confidence = 0.8f,
        status = "Active",
        derived_from = Array.Empty<string>(),
        challenged_by = Array.Empty<string>()
    };

    private static object BuildRisk(string id, string text, string raisedBy, string derivedFromId) => new
    {
        id,
        text,
        category = "Technical",
        severity = "Medium",
        likelihood = "Low",
        mitigation = "Monitor",
        derived_from = new[] { derivedFromId },
        raised_by = raisedBy
    };

    private static object BuildAssumption(string id, string text, string derivedFromId) => new
    {
        id,
        text,
        validation_step = "Validate via user testing",
        derived_from = new[] { derivedFromId },
        status = "Unvalidated"
    };

    private static object BuildDecision(string id, string text, string derivedFromId) => new
    {
        id,
        text,
        rationale = "Derived from assumption",
        owner = "moderator",
        derived_from = new[] { derivedFromId },
        binding = true
    };
}
