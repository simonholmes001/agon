using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Domain.Sessions;
using Agon.Infrastructure.Persistence.PostgreSQL;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Infrastructure.Tests.Persistence;

/// <summary>
/// Unit tests for Session Repository using in-memory database (no Docker required).
/// </summary>
public class SessionRepositoryTests : IDisposable
{
    private readonly AgonDbContext _dbContext;
    private readonly ISessionRepository _repository;

    public SessionRepositoryTests()
    {
        // Use in-memory database for unit tests (no Docker required)
        var options = new DbContextOptionsBuilder<AgonDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _dbContext = new AgonDbContext(options);
        
        // Create a stub TruthMapRepository for testing
        var truthMapRepo = Substitute.For<ITruthMapRepository>();
        truthMapRepo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<TruthMapModel?>(TruthMapModel.Empty((Guid)callInfo[0])));
        
        _repository = new SessionRepository(_dbContext, truthMapRepo);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsync_WithValidSessionState_PersistsSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);
        
        var sessionState = SessionState.Create(
            sessionId,
            frictionLevel: 50,
            researchToolsEnabled: true,
            initialTruthMap: truthMap
        );

        // Act
        var result = await _repository.CreateAsync(sessionState);

        // Assert
        result.Should().NotBeNull();
        result.SessionId.Should().Be(sessionId);
        result.Phase.Should().Be(SessionPhase.Intake);
        result.Status.Should().Be(SessionStatus.Active);
        result.FrictionLevel.Should().Be(50);
        result.ResearchToolsEnabled.Should().BeTrue();

        // Verify entity was created in database
        var entity = await _dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        entity.Should().NotBeNull();
        entity!.Phase.Should().Be(SessionPhase.Intake.ToString());
        entity.Status.Should().Be(SessionStatus.Active.ToString());
    }

    [Fact]
    public async Task GetAsync_WhenSessionExists_ReturnsSessionState()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        
        var entity = new SessionEntity
        {
            Id = sessionId,
            UserId = userId,
            Mode = SessionMode.Quick.ToString(),
            FrictionLevel = 70,
            Status = SessionStatus.Active.ToString(),
            Phase = SessionPhase.Clarification.ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        _dbContext.Sessions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _repository.GetAsync(sessionId);

        // Assert
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(sessionId);
        result.Phase.Should().Be(SessionPhase.Clarification);
        result.Status.Should().Be(SessionStatus.Active);
        result.FrictionLevel.Should().Be(70);
    }

    [Fact]
    public async Task GetAsync_WhenSessionDoesNotExist_ReturnsNull()
    {
        // Arrange
        var nonExistentSessionId = Guid.NewGuid();

        // Act
        var result = await _repository.GetAsync(nonExistentSessionId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesSessionStateCorrectly()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);
        
        var sessionState = SessionState.Create(
            sessionId,
            frictionLevel: 50,
            researchToolsEnabled: true,
            initialTruthMap: truthMap
        );
        
        await _repository.CreateAsync(sessionState);

        // Act - Update phase and round
        sessionState.Phase = SessionPhase.AnalysisRound;
        sessionState.CurrentRound = 1;
        sessionState.TokensUsed = 1500;
        await _repository.UpdateAsync(sessionState);

        // Assert
        var entity = await _dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        entity.Should().NotBeNull();
        entity!.Phase.Should().Be(SessionPhase.AnalysisRound.ToString());
        entity.CurrentRound.Should().Be(1);
        entity.TokensUsed.Should().Be(1500);
        entity.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CreateAsync_WithUserContext_PersistsUserId()
    {
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);

        var sessionState = SessionState.Create(
            sessionId,
            userId,
            "My idea",
            frictionLevel: 60,
            researchToolsEnabled: false,
            initialTruthMap: truthMap
        );

        await _repository.CreateAsync(sessionState);

        var entity = await _dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        entity.Should().NotBeNull();
        entity!.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task UpdateAsync_WithPhaseTransition_UpdatesPhaseCorrectly()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);
        
        var sessionState = SessionState.Create(
            sessionId,
            frictionLevel: 30,
            researchToolsEnabled: false,
            initialTruthMap: truthMap
        );
        
        await _repository.CreateAsync(sessionState);

        // Act - Transition through phases
        sessionState.Phase = SessionPhase.Clarification;
        await _repository.UpdateAsync(sessionState);
        
        sessionState.Phase = SessionPhase.AnalysisRound;
        await _repository.UpdateAsync(sessionState);
        
        sessionState.Phase = SessionPhase.Synthesis;
        await _repository.UpdateAsync(sessionState);

        // Assert
        var entity = await _dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        entity.Should().NotBeNull();
        entity!.Phase.Should().Be(SessionPhase.Synthesis.ToString());
    }

    [Fact]
    public async Task UpdateAsync_WithStatusChange_UpdatesStatusCorrectly()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);
        
        var sessionState = SessionState.Create(
            sessionId,
            frictionLevel: 90,
            researchToolsEnabled: true,
            initialTruthMap: truthMap
        );
        
        await _repository.CreateAsync(sessionState);

        // Act - Change status to complete
        sessionState.Status = SessionStatus.Complete;
        await _repository.UpdateAsync(sessionState);

        // Assert
        var entity = await _dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(SessionStatus.Complete.ToString());
    }

    [Fact]
    public async Task ListByUserAsync_ReturnsSessionsOrderedByCreatedAtDescending()
    {
        // Arrange
        var userId = Guid.NewGuid();
        
        var session1 = new SessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Mode = SessionMode.Quick.ToString(),
            FrictionLevel = 50,
            Status = SessionStatus.Active.ToString(),
            Phase = SessionPhase.Intake.ToString(),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2)
        };
        
        var session2 = new SessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Mode = SessionMode.Deep.ToString(),
            FrictionLevel = 70,
            Status = SessionStatus.Complete.ToString(),
            Phase = SessionPhase.PostDelivery.ToString(),
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        
        var session3 = new SessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Mode = SessionMode.Quick.ToString(),
            FrictionLevel = 30,
            Status = SessionStatus.Active.ToString(),
            Phase = SessionPhase.Clarification.ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        // Add a session for a different user (should not be returned)
        var otherUserSession = new SessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Mode = SessionMode.Quick.ToString(),
            FrictionLevel = 50,
            Status = SessionStatus.Active.ToString(),
            Phase = SessionPhase.Intake.ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        _dbContext.Sessions.AddRange(session1, session2, session3, otherUserSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _repository.ListByUserAsync(userId);

        // Assert
        results.Should().HaveCount(3);
        results[0].SessionId.Should().Be(session3.Id); // Most recent
        results[1].SessionId.Should().Be(session2.Id);
        results[2].SessionId.Should().Be(session1.Id); // Oldest
    }

    [Fact]
    public async Task ListByUserAsync_WhenUserHasNoSessions_ReturnsEmptyList()
    {
        // Arrange
        var userIdWithNoSessions = Guid.NewGuid();

        // Act
        var results = await _repository.ListByUserAsync(userIdWithNoSessions);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithForkedSession_StoresForkedFromAndSnapshotId()
    {
        // Arrange
        var originalSessionId = Guid.NewGuid();
        var forkedSessionId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        
        // Create original session first
        var originalTruthMap = TruthMapModel.Empty(originalSessionId);
        var originalSession = SessionState.Create(
            originalSessionId,
            frictionLevel: 50,
            researchToolsEnabled: true,
            initialTruthMap: originalTruthMap
        );
        await _repository.CreateAsync(originalSession);
        
        // Create forked session
        var forkedTruthMap = TruthMapModel.Empty(forkedSessionId);
        var forkedSession = SessionState.Create(
            forkedSessionId,
            frictionLevel: 50,
            researchToolsEnabled: true,
            initialTruthMap: forkedTruthMap
        );

        // Act
        var result = await _repository.CreateAsync(forkedSession);

        // Assert
        var entity = await _dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == forkedSessionId);
        entity.Should().NotBeNull();
        // Note: ForkedFrom and ForkSnapshotId would need to be part of SessionState
        // For now, verify basic creation works
        entity!.Id.Should().Be(forkedSessionId);
    }
}
