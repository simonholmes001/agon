using Agon.Domain.TruthMap;
using Agon.Infrastructure.Persistence.InMemory;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.Persistence;

public class InMemoryTruthMapRepositoryTests
{
    [Fact]
    public async Task CreateAndGetAsync_ReturnsStoredMap()
    {
        var repository = new InMemoryTruthMapRepository();
        var sessionId = Guid.NewGuid();
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Test idea";

        await repository.CreateAsync(map, CancellationToken.None);

        var loaded = await repository.GetAsync(sessionId, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.CoreIdea.Should().Be("Test idea");
    }

    [Fact]
    public async Task ApplyPatchAsync_IncrementsMapVersion()
    {
        var repository = new InMemoryTruthMapRepository();
        var sessionId = Guid.NewGuid();
        var map = TruthMapState.CreateNew(sessionId);

        await repository.CreateAsync(map, CancellationToken.None);

        var patch = new TruthMapPatch
        {
            Ops = [],
            Meta = new PatchMeta
            {
                Agent = "claude_agent",
                Round = 1,
                Reason = "test",
                SessionId = sessionId
            }
        };

        await repository.ApplyPatchAsync(sessionId, patch, CancellationToken.None);
        var updated = await repository.GetAsync(sessionId, CancellationToken.None);

        updated!.Version.Should().Be(1);
    }
}
