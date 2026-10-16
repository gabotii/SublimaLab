using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SublimaLab.Infrastructure;
using SublimaLab.Infrastructure.Persistence;

namespace SublimaLab.Migrations.Postgres;

/// <summary>Used only by <c>dotnet ef</c> to create migrations; no database connection is opened.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql("Host=localhost;Database=sublimalab_design", o => o.MigrationsAssembly(DependencyInjection.PostgresMigrationsAssembly))
            .Options);
}
