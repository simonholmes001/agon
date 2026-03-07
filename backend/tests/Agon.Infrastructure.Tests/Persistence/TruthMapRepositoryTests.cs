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
                new PatchOperation(PatchOp.Add, "/claims/0", new { text = "Test claim", confidence = 0.8 })
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
            [new PatchOperation(PatchOp.Add, "/claims/0", new { text = "Claim 1" })],
            new PatchMeta("gpt-agent", 1, "First patch", sessionId)
        );
        
        var patch2 = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/claims/1", new { text = "Claim 2" })],
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
                [new PatchOperation(PatchOp.Add, "/claims/0", new { text = "Claim 1" })],
                new PatchMeta("gpt-agent", 1, "First", sessionId)
            ),
            new TruthMapPatch(
                [new PatchOperation(PatchOp.Add, "/claims/1", new { text = "Claim 2" })],
                new PatchMeta("gemini-agent", 1, "Second", sessionId)
            ),
            new TruthMapPatch(
                [new PatchOperation(PatchOp.Add, "/risks/0", new { text = "Risk 1" })],
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
                [new PatchOperation(PatchOp.Add, "/claims/0", new { text = "Initial claim" })],
                new PatchMeta("gpt-agent", 1, "Add claim", sessionId)
            ),
            new TruthMapPatch(
                [new PatchOperation(PatchOp.Add, "/risks/0", new { text = "Initial risk" })],
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
    public async Task GetImpactSetAsync_ReturnsEmptySet()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var entityId = "claim-123";

        // Act
        var impactSet = await _repository.GetImpactSetAsync(sessionId, entityId);

        // Assert
        // Placeholder implementation returns empty set
        impactSet.Should().NotBeNull();
        impactSet.Should().BeEmpty();
    }
}
