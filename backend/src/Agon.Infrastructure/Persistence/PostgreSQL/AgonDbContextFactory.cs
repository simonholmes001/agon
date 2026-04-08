using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Agon.Infrastructure.Persistence.PostgreSQL;

/// <summary>
/// Design-time factory used by EF tooling to scaffold migrations without booting the API host.
/// </summary>
public sealed class AgonDbContextFactory : IDesignTimeDbContextFactory<AgonDbContext>
{
    public AgonDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AgonDbContext>();
        var connectionString =
            Environment.GetEnvironmentVariable("AGON_EF_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=agon;Username=agon_user;Password=agon_dev_password";

        optionsBuilder.UseNpgsql(connectionString);
        return new AgonDbContext(optionsBuilder.Options);
    }
}
