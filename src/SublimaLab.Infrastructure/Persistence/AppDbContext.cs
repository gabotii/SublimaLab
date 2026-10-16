using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SublimaLab.Application.Abstractions;
using SublimaLab.Domain.Carts;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Common;
using SublimaLab.Domain.Customers;
using SublimaLab.Domain.Customization;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Discounts;
using SublimaLab.Domain.Ordering;
using SublimaLab.Domain.Shipping;
using SublimaLab.Domain.Uploads;
using SublimaLab.Infrastructure.Identity;

namespace SublimaLab.Infrastructure.Persistence;

/// <summary>
/// Shared EF Core model. Each database provider has its own derived context
/// (<see cref="SqliteAppDbContext"/>, <see cref="PostgresAppDbContext"/>) so provider-specific
/// conventions and migrations stay isolated while the model is defined once.
/// </summary>
public abstract class AppDbContext(DbContextOptions options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<ProductCategory> Categories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<CustomizationConfiguration> CustomizationConfigurations => Set<CustomizationConfiguration>();
    public DbSet<Design> Designs => Set<Design>();
    public DbSet<ArtworkUpload> Uploads => Set<ArtworkUpload>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Ids are generated in the domain (UUIDv7). Marking them as never generated lets EF
        // recognise new entities added to tracked aggregates (e.g. a new CartItem) as inserts.
        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.FirstName).HasMaxLength(60);
            user.Property(u => u.LastName).HasMaxLength(60);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }
}
