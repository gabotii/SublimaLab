using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SublimaLab.Infrastructure.Persistence;

/// <summary>
/// SQLite context for zero-setup local development and tests. SQLite has no native decimal
/// or UTC datetime type, so decimals are stored as REAL (enabling ORDER BY / comparisons in SQL;
/// values are always rounded to cents or 4 decimals) and DateTimes are read back as UTC.
/// </summary>
public sealed class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : AppDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<decimal>().HaveConversion<double>();
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}

/// <summary>PostgreSQL context for Docker and production.</summary>
public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : AppDbContext(options);
