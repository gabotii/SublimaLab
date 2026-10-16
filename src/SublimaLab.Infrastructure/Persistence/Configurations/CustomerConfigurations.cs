using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SublimaLab.Domain.Carts;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Customers;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Uploads;
using SublimaLab.Infrastructure.Identity;

namespace SublimaLab.Infrastructure.Persistence.Configurations;

internal sealed class DesignConfiguration : IEntityTypeConfiguration<Design>
{
    public void Configure(EntityTypeBuilder<Design> builder)
    {
        builder.ToTable("Designs");
        builder.Property(d => d.Name).HasMaxLength(80).IsRequired();
        builder.Property(d => d.Document).HasJsonConversion().IsRequired();
        builder.HasIndex(d => new { d.UserId, d.UpdatedAt });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ArtworkUploadConfiguration : IEntityTypeConfiguration<ArtworkUpload>
{
    public void Configure(EntityTypeBuilder<ArtworkUpload> builder)
    {
        builder.ToTable("Uploads");
        builder.Property(u => u.SessionId).HasMaxLength(64);
        builder.Property(u => u.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.StorageKey).HasMaxLength(200).IsRequired();
        builder.Property(u => u.OriginalFileName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(u => u.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasIndex(u => u.OwnerUserId);
        builder.HasIndex(u => u.SessionId);
        builder.HasIndex(u => u.StorageKey).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(u => u.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts", t => t.HasCheckConstraint("CK_Carts_Owner", "\"UserId\" IS NOT NULL OR \"SessionId\" IS NOT NULL"));
        builder.Property(c => c.SessionId).HasMaxLength(64);
        builder.Property(c => c.DiscountCode).HasMaxLength(40);

        // Unique indexes allow multiple NULLs in both SQLite and PostgreSQL.
        builder.HasIndex(c => c.UserId).IsUnique();
        builder.HasIndex(c => c.SessionId).IsUnique();

        builder.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems", t => t.HasCheckConstraint("CK_CartItems_Quantity", "\"Quantity\" BETWEEN 1 AND 100"));
        builder.Property(i => i.Design).HasJsonConversion();
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(i => i.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddresses");
        builder.Property(a => a.Label).HasMaxLength(40);
        builder.Property(a => a.RecipientName).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Phone).HasMaxLength(30).IsRequired();
        builder.Property(a => a.Line1).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Line2).HasMaxLength(200);
        builder.Property(a => a.City).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Region).HasMaxLength(100).IsRequired();
        builder.Property(a => a.PostalCode).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Country).HasMaxLength(2).IsFixedLength().IsRequired();
        builder.HasIndex(a => a.UserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
