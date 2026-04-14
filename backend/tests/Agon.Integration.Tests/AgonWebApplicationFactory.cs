using System.Collections.Generic;
using Agon.Application.Interfaces;
using Agon.Domain.Snapshots;
using Agon.Infrastructure.Persistence.Repositories;
using Agon.Infrastructure.Persistence.PostgreSQL;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agon.Integration.Tests;

/// <summary>
/// Custom WebApplicationFactory that replaces external dependencies (Redis, PostgreSQL)
/// with in-memory implementations for integration testing.
/// 
/// This allows us to test the full HTTP pipeline, DI configuration, and routing
/// without requiring actual database servers to be running.
/// </summary>
public class AgonWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"AgonIntegrationTests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set environment to Testing to load appsettings.Testing.json if exists
        builder.UseEnvironment("Testing");

        // Override configuration to prevent PostgreSQL and Redis registration
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Add in-memory configuration that clears the connection strings
            // This must happen AFTER appsettings.json is loaded, so we add it last
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Set connection strings to null/empty so PostgreSQL and Redis won't be registered
                ["ConnectionStrings:PostgreSQL"] = null,
                ["ConnectionStrings:Redis"] = "localhost:6379", // Keep Redis connection string but we'll mock it
                ["ConnectionStrings:BlobStorage"] = string.Empty,
                ["ApiRateLimiting:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            // CRITICAL FIX: Don't use AddDbContext at all - it re-registers provider services
            // Instead, directly replace the DbContextOptions that controls which provider is used
            
            // Remove the existing DbContextOptions registration completely
            var dbContextOptionsDescriptor = services
                .FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<AgonDbContext>));
            if (dbContextOptionsDescriptor != null)
            {
                services.Remove(dbContextOptionsDescriptor);
            }

            // Register NEW DbContextOptions with InMemory provider
            // This uses AddSingleton to register the options directly, bypassing AddDbContext
            services.AddSingleton<DbContextOptions<AgonDbContext>>(sp =>
            {
                var builder = new DbContextOptionsBuilder<AgonDbContext>();
                builder.UseInMemoryDatabase(_databaseName);
                return builder.Options;
            });

            // Ensure AgonDbContext itself is registered as scoped
            services.RemoveAll(typeof(AgonDbContext));
            services.AddScoped<AgonDbContext>();

            // Add repositories (they might not be registered if PostgreSQL connection string was null)
            // Remove first to avoid duplicates, then add
            services.RemoveAll<ISessionRepository>();
            services.RemoveAll<ITruthMapRepository>();
            services.RemoveAll<IAgentMessageRepository>();
            services.RemoveAll<IAttachmentRepository>();
            services.AddScoped<ISessionRepository, SessionRepository>();
            services.AddScoped<ITruthMapRepository, TruthMapRepository>();
            services.AddScoped<IAgentMessageRepository, AgentMessageRepository>();
            services.AddScoped<IAttachmentRepository, AttachmentRepository>();

            // Remove Redis dependencies if they were registered
            services.RemoveAll<StackExchange.Redis.IConnectionMultiplexer>();
            services.RemoveAll<StackExchange.Redis.IDatabase>();
            services.RemoveAll<ISnapshotStore>();

            // Add a fake snapshot store
            services.AddScoped<ISnapshotStore, FakeSnapshotStore>();

            // NO need to call EnsureCreated - InMemory database is created automatically on first use
        });
    }
}

/// <summary>
/// Fake snapshot store for testing.
/// Stores snapshots in memory instead of Redis.
/// </summary>
public class FakeSnapshotStore : ISnapshotStore
{
    private readonly Dictionary<Guid, List<Domain.Snapshots.SessionSnapshot>> _snapshots = new();

    public Task<Domain.Snapshots.SessionSnapshot> SaveAsync(
        Domain.Snapshots.SessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!_snapshots.ContainsKey(snapshot.SessionId))
        {
            _snapshots[snapshot.SessionId] = new List<Domain.Snapshots.SessionSnapshot>();
        }

        _snapshots[snapshot.SessionId].Add(snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<IReadOnlyList<Domain.Snapshots.SessionSnapshot>> ListBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_snapshots.TryGetValue(sessionId, out var sessionSnapshots))
        {
            return Task.FromResult<IReadOnlyList<Domain.Snapshots.SessionSnapshot>>(
                sessionSnapshots.AsReadOnly());
        }

        return Task.FromResult<IReadOnlyList<Domain.Snapshots.SessionSnapshot>>(
            Array.Empty<Domain.Snapshots.SessionSnapshot>());
    }

    public Task<Domain.Snapshots.SessionSnapshot?> GetAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        foreach (var sessionSnapshots in _snapshots.Values)
        {
            var snapshot = sessionSnapshots.FirstOrDefault(s => s.SnapshotId == snapshotId);
            if (snapshot != null)
            {
                return Task.FromResult<Domain.Snapshots.SessionSnapshot?>(snapshot);
            }
        }

        return Task.FromResult<Domain.Snapshots.SessionSnapshot?>(null);
    }
}
