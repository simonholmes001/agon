using Agon.Application.Interfaces;
using Agon.Domain.Snapshots;
using Agon.Infrastructure.Persistence.Redis;
using FluentAssertions;
using NSubstitute;
using StackExchange.Redis;
using System.Text.Json;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Infrastructure.Tests.Persistence;

/// <summary>
/// Unit tests for Redis Snapshot Store using mocked IDatabase.
/// </summary>
public class RedisSnapshotStoreTests
{
    private readonly IDatabase _mockDatabase;
    private readonly ISnapshotStore _store;

    public RedisSnapshotStoreTests()
    {
        _mockDatabase = Substitute.For<IDatabase>();
        _store = new RedisSnapshotStore(_mockDatabase);
    }

    [Fact]
    public async Task SaveAsync_StoresSnapshotInRedis()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);
        var snapshot = SessionSnapshot.Create(truthMap, round: 1);

        // Mock the StringSetAsync overload with Expiration
        _mockDatabase.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>())
            .Returns(true);

        _mockDatabase.SetAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<CommandFlags>())
            .Returns(true);

        _mockDatabase.KeyExpireAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CommandFlags>())
            .Returns(true);

        // Act
        var result = await _store.SaveAsync(snapshot);

        // Assert
        result.Should().NotBeNull();
        result.SnapshotId.Should().Be(snapshot.SnapshotId);
        
        // Verify StringSetAsync was called once
        await _mockDatabase.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>());
    }

    [Fact]
    public async Task GetAsync_WhenSnapshotExists_ReturnsSnapshot()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);
        var snapshot = SessionSnapshot.Create(truthMap, round: 1);
        
        var serialized = JsonSerializer.Serialize(snapshot);
        
        _mockDatabase.StringGetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<CommandFlags>())
            .Returns(serialized);

        // Act
        var result = await _store.GetAsync(snapshot.SnapshotId);

        // Assert
        result.Should().NotBeNull();
        result!.SnapshotId.Should().Be(snapshot.SnapshotId);
        result.SessionId.Should().Be(sessionId);
        result.Round.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_WhenSnapshotDoesNotExist_ReturnsNull()
    {
        // Arrange
        var nonExistentSnapshotId = Guid.NewGuid();
        
        _mockDatabase.StringGetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        // Act
        var result = await _store.GetAsync(nonExistentSnapshotId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListBySessionAsync_ReturnsSnapshotsOrderedByRound()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);
        
        var snapshot1 = SessionSnapshot.Create(truthMap, round: 1);
        var snapshot2 = SessionSnapshot.Create(truthMap, round: 2);
        var snapshot3 = SessionSnapshot.Create(truthMap, round: 3);
        
        var snapshotIds = new[]
        {
            $"snapshot:{snapshot1.SnapshotId}",
            $"snapshot:{snapshot2.SnapshotId}",
            $"snapshot:{snapshot3.SnapshotId}"
        };

        // Mock the set members call
        _mockDatabase.SetMembersAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains($"session:{sessionId}:snapshots")),
            Arg.Any<CommandFlags>())
            .Returns(snapshotIds.Select(id => (RedisValue)id).ToArray());

        // Mock individual snapshot retrievals
        _mockDatabase.StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains(snapshot1.SnapshotId.ToString())),
            Arg.Any<CommandFlags>())
            .Returns(JsonSerializer.Serialize(snapshot1));
        
        _mockDatabase.StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains(snapshot2.SnapshotId.ToString())),
            Arg.Any<CommandFlags>())
            .Returns(JsonSerializer.Serialize(snapshot2));
        
        _mockDatabase.StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains(snapshot3.SnapshotId.ToString())),
            Arg.Any<CommandFlags>())
            .Returns(JsonSerializer.Serialize(snapshot3));

        // Act
        var results = await _store.ListBySessionAsync(sessionId);

        // Assert
        results.Should().HaveCount(3);
        results[0].Round.Should().Be(1);
        results[1].Round.Should().Be(2);
        results[2].Round.Should().Be(3);
    }

    [Fact]
    public async Task ListBySessionAsync_WhenNoSnapshots_ReturnsEmptyList()
    {
        // Arrange
        var sessionIdWithNoSnapshots = Guid.NewGuid();
        
        _mockDatabase.SetMembersAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        // Act
        var results = await _store.ListBySessionAsync(sessionIdWithNoSnapshots);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_AddsSnapshotIdToSessionSet()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);
        var snapshot = SessionSnapshot.Create(truthMap, round: 1);

        _mockDatabase.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>())
            .Returns(true);

        _mockDatabase.SetAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<CommandFlags>())
            .Returns(true);

        // Act
        await _store.SaveAsync(snapshot);

        // Assert - Verify the snapshot ID was added to the session's snapshot set
        await _mockDatabase.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains($"session:{sessionId}:snapshots")),
            Arg.Is<RedisValue>(v => v.ToString().Contains(snapshot.SnapshotId.ToString())),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SaveAsync_VerifiesSnapshotIntegrity()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);
        var snapshot = SessionSnapshot.Create(truthMap, round: 1);

        _mockDatabase.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>())
            .Returns(true);

        // Act
        var result = await _store.SaveAsync(snapshot);

        // Assert - Verify the snapshot's hash is still valid after serialization
        result.IsIntact().Should().BeTrue();
    }
}
