using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Common;
using SublimaLab.Infrastructure.Identity;

namespace SublimaLab.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool SeedCatalog { get; set; } = true;

    /// <summary>Optional bootstrap administrator. Provide via user-secrets or environment variables, never source control.</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}

/// <summary>Applies migrations and idempotently seeds roles, catalog and the bootstrap admin.</summary>
public sealed class DatabaseInitializer(
    AppDbContext db,
    RoleManager<IdentityRole<Guid>> roles,
    UserManager<ApplicationUser> users,
    IOptions<SeedOptions> options,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    public static async Task InitializeAsync(IServiceProvider services, bool applyMigrations, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.RunAsync(applyMigrations, cancellationToken);
    }

    public async Task RunAsync(bool applyMigrations, CancellationToken cancellationToken)
    {
        if (applyMigrations)
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(cancellationToken);
        }

        await EnsureRolesAsync();
        await EnsureAdminAsync();

        if (options.Value.SeedCatalog)
        {
            await SeedCatalogAsync(cancellationToken);
        }
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in new[] { Roles.Admin, Roles.Customer })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() });
            }
        }
    }

    private async Task EnsureAdminAsync()
    {
        var (email, password) = (options.Value.AdminEmail, options.Value.AdminPassword);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = "SublimaLab",
                LastName = "Admin",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            };

            var result = await users.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Could not create the bootstrap admin: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            logger.LogInformation("Created bootstrap administrator {Email}", email);
        }

        if (!await users.IsInRoleAsync(admin, Roles.Admin))
        {
            await users.AddToRoleAsync(admin, Roles.Admin);
        }
    }

    private async Task SeedCatalogAsync(CancellationToken cancellationToken)
    {
        if (!await db.Categories.AnyAsync(cancellationToken))
        {
            db.Categories.AddRange(CatalogSeed.Build());
            logger.LogInformation("Seeding sample catalog");
        }

        if (!await db.ShippingMethods.AnyAsync(cancellationToken))
        {
            db.ShippingMethods.AddRange(CatalogSeed.ShippingMethods());
        }

        if (!await db.DiscountCodes.AnyAsync(cancellationToken))
        {
            db.DiscountCodes.AddRange(CatalogSeed.DiscountCodes());
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
