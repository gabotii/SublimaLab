using Microsoft.EntityFrameworkCore;
using SublimaLab.Domain.Carts;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Customers;
using SublimaLab.Domain.Customization;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Discounts;
using SublimaLab.Domain.Ordering;
using SublimaLab.Domain.Shipping;
using SublimaLab.Domain.Uploads;

namespace SublimaLab.Application.Abstractions;

/// <summary>
/// The application's unit of work. Services query EF Core directly through this interface
/// instead of going through per-entity repositories: DbSet already is a repository, and
/// LINQ projections keep queries efficient without an extra abstraction layer.
/// </summary>
public interface IAppDbContext
{
    DbSet<ProductCategory> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<CustomizationConfiguration> CustomizationConfigurations { get; }
    DbSet<Design> Designs { get; }
    DbSet<ArtworkUpload> Uploads { get; }
    DbSet<Cart> Carts { get; }
    DbSet<Order> Orders { get; }
    DbSet<ShippingMethod> ShippingMethods { get; }
    DbSet<DiscountCode> DiscountCodes { get; }
    DbSet<CustomerAddress> CustomerAddresses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
