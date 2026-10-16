using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SublimaLab.Infrastructure;
using SublimaLab.Infrastructure.Persistence;

namespace SublimaLab.Migrations.Sqlite;

/// <summary>Used only by <c>dotnet ef</c> to create migrations; never used at runtime.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite("Data Source=design-time.db", o => o.MigrationsAssembly(DependencyInjection.SqliteMigrationsAssembly))
            .Options);
}
