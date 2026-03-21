using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using Agon.Infrastructure.Persistence.PostgreSQL;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Infrastructure.Tests.Persistence;

/// <summary>
/// Extended unit tests for TruthMapRepository covering SaveAsync,
/// GetImpactSetAsync, GetPatchHistoryAsync, and error-handling paths.
/// </summary>
public class TruthMapRepositoryExtendedTests : IDisposable
{
    private readonly AgonDbContext _dbContext;
    private readonly ITruthMapRepository _repository;

    public TruthMapRepositoryExtendedTests()
    {
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

    // ── SaveAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_WithNewSessionId_CreatesNewEntity()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = new TruthMapModel
        {
            SessionId = sessionId,
            Version = 1,
            CoreIdea = "Test idea for SaveAsync"
        };

        // Act
        await _repository.SaveAsync(truthMap, CancellationToken.None);

        // Assert
        var entity = await _dbContext.TruthMaps.FirstOrDefaultAsync(tm => tm.SessionId == sessionId);
        entity.Should().NotBeNull();
        entity!.Version.Should().Be(1);
    }

    [Fact]
    public async Task SaveAsync_WithExistingSessionId_UpdatesEntity()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = new TruthMapModel
        {
            SessionId = sessionId,
            Version = 1,
            CoreIdea = "Initial idea"
        };

        // Create initial entity
        await _repository.SaveAsync(truthMap, CancellationToken.None);

        // Update it
        var updatedTruthMap = new TruthMapModel
        {
            SessionId = sessionId,
            Version = 2,
            CoreIdea = "Updated idea"
        };

        // Act
        await _repository.SaveAsync(updatedTruthMap, CancellationToken.None);

        // Assert
        var entities = await _dbContext.TruthMaps.Where(tm => tm.SessionId == sessionId).ToListAsync();
        entities.Should().HaveCount(1); // No duplicate created
        entities[0].Version.Should().Be(2);
    }

    [Fact]
    public async Task SaveAsync_PreservesVersionFromTruthMap()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = new TruthMapModel
        {
            SessionId = sessionId,
            Version = 5, // Specific version set by caller
            CoreIdea = "With explicit version"
        };

        // Act
        await _repository.SaveAsync(truthMap, CancellationToken.None);

        // Assert
        var entity = await _dbContext.TruthMaps.FirstOrDefaultAsync(tm => tm.SessionId == sessionId);
        entity!.Version.Should().Be(5);
    }

    // ── GetImpactSetAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetImpactSetAsync_WhenSessionNotFound_ReturnsEmptySet()
    {
        // Arrange - no data in DB
        var sessionId = Guid.NewGuid();

        // Act
        var result = await _repository.GetImpactSetAsync(sessionId, "nonexistent-entity", CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetImpactSetAsync_WithSimpleTruthMap_ReturnsEmptySetForUnreferencedEntity()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/0", BuildClaimValue("claim-1", "Independent claim"))],
            new PatchMeta("gpt_agent", 1, "Initial", sessionId)
        );
        await _repository.ApplyPatchAsync(sessionId, patch, CancellationToken.None);

        // Act - look for impact of an entity that nothing derives from
        var result = await _repository.GetImpactSetAsync(sessionId, "claim-1", CancellationToken.None);

        // Assert
        result.Should().BeEmpty(); // Nothing derives from claim-1
    }

    [Fact]
    public async Task GetImpactSetAsync_WhenRiskDerivesFromClaim_ReturnsRiskInImpactSet()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Add claim first
        var claimPatch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/0", BuildClaimValue("claim-1", "Market size claim"))],
            new PatchMeta("gpt_agent", 1, "Add claim", sessionId)
        );
        await _repository.ApplyPatchAsync(sessionId, claimPatch, CancellationToken.None);

        // Add risk that derives from the claim
        var riskPatch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/risks/0", BuildRiskValue("risk-1", "Market risk", ["claim-1"]))],
            new PatchMeta("gemini_agent", 1, "Add risk", sessionId)
        );
        await _repository.ApplyPatchAsync(sessionId, riskPatch, CancellationToken.None);

        // Act
        var result = await _repository.GetImpactSetAsync(sessionId, "claim-1", CancellationToken.None);

        // Assert
        result.Should().Contain("risk-1");
    }

    // ── GetPatchHistoryAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetPatchHistoryAsync_WhenNoPatchesExist_ReturnsEmptyList()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        var history = await _repository.GetPatchHistoryAsync(sessionId, CancellationToken.None);

        // Assert
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPatchHistoryAsync_WhenPatchesExist_ReturnsInChronologicalOrder()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        var patch1 = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/0", BuildClaimValue("c-1", "Claim 1"))],
            new PatchMeta("gpt_agent", 1, "Round 1 patch", sessionId)
        );
        var patch2 = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/risks/0", BuildRiskValue("r-1", "Risk 1", []))],
            new PatchMeta("gemini_agent", 2, "Round 2 patch", sessionId)
        );

        await _repository.ApplyPatchAsync(sessionId, patch1, CancellationToken.None);
        await _repository.ApplyPatchAsync(sessionId, patch2, CancellationToken.None);

        // Act
        var history = await _repository.GetPatchHistoryAsync(sessionId, CancellationToken.None);

        // Assert
        history.Should().HaveCount(2);
        history[0].Meta.Agent.Should().Be("gpt_agent");
        history[1].Meta.Agent.Should().Be("gemini_agent");
    }

    [Fact]
    public async Task GetPatchHistoryAsync_CorrectlyDeserializesPatches()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        var patch = new TruthMapPatch(
            [
                new PatchOperation(PatchOp.Add, "/claims/0", BuildClaimValue("c-1", "Claim 1")),
                new PatchOperation(PatchOp.Add, "/claims/1", BuildClaimValue("c-2", "Claim 2"))
            ],
            new PatchMeta("gpt_agent", 1, "Multi-op patch", sessionId)
        );

        await _repository.ApplyPatchAsync(sessionId, patch, CancellationToken.None);

        // Act
        var history = await _repository.GetPatchHistoryAsync(sessionId, CancellationToken.None);

        // Assert
        history.Should().HaveCount(1);
        history[0].Ops.Should().HaveCount(2);
        history[0].Meta.Reason.Should().Be("Multi-op patch");
    }

    // ── GetAsync with existing data ────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_WhenDataExists_ReturnsDeserializedTruthMap()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/0", BuildClaimValue("c-1", "Claim text here"))],
            new PatchMeta("gpt_agent", 1, "Add claim", sessionId)
        );

        await _repository.ApplyPatchAsync(sessionId, patch, CancellationToken.None);

        // Act
        var result = await _repository.GetAsync(sessionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(sessionId);
        result.Claims.Should().HaveCount(1);
        result.Claims[0].Id.Should().Be("c-1");
    }

    [Fact]
    public async Task GetAsync_MultiplePatchRounds_ReturnsMergedState()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Round 1: add claim
        await _repository.ApplyPatchAsync(sessionId, new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/0", BuildClaimValue("c-1", "First claim"))],
            new PatchMeta("gpt_agent", 1, "Round 1", sessionId)
        ), CancellationToken.None);

        // Round 2: add risk
        await _repository.ApplyPatchAsync(sessionId, new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/risks/0", BuildRiskValue("r-1", "First risk", ["c-1"]))],
            new PatchMeta("gemini_agent", 2, "Round 2", sessionId)
        ), CancellationToken.None);

        // Act
        var result = await _repository.GetAsync(sessionId, CancellationToken.None);

        // Assert
        result!.Claims.Should().HaveCount(1);
        result.Risks.Should().HaveCount(1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static object BuildClaimValue(string id, string text) => new
    {
        id,
        agent = "gpt_agent",
        round = 1,
        text,
        confidence = 0.8,
        status = "Active",
        derived_from = Array.Empty<string>(),
        challenged_by = Array.Empty<string>()
    };

    private static object BuildRiskValue(string id, string text, string[] derivedFrom) => new
    {
        id,
        text,
        category = "Technical",
        severity = "High",
        likelihood = "Medium",
        mitigation = "Monitor closely",
        derived_from = derivedFrom,
        agent = "gemini_agent"
    };
}
