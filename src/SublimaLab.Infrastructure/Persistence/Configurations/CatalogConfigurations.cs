using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Customization;

namespace SublimaLab.Infrastructure.Persistence.Configurations;

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategories");
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(80).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.ImageUrl).HasMaxLength(500);
        builder.HasIndex(c => c.Slug).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", t => t.HasCheckConstraint("CK_Products_BasePrice", "\"BasePrice\" >= 0"));
        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(120).IsRequired();
        builder.Property(p => p.ShortDescription).HasMaxLength(300);
        builder.Property(p => p.Description).HasMaxLength(4000);
        builder.Property(p => p.Specifications).HasJsonConversion();
        builder.Ignore(p => p.IsCustomizable);

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => new { p.CategoryId, p.IsActive });
        builder.HasIndex(p => p.BasePrice);

        builder.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(p => p.Variants).WithOne(v => v.Product).HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Customization).WithOne().HasForeignKey<CustomizationConfiguration>(c => c.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariants");
        builder.Property(v => v.Sku).HasMaxLength(64).IsRequired();
        builder.Property(v => v.ColorName).HasMaxLength(60).IsRequired();
        builder.Property(v => v.ColorHex).HasMaxLength(7).IsRequired();
        builder.Property(v => v.Size).HasMaxLength(30);
        builder.Ignore(v => v.Label);
        builder.HasIndex(v => v.Sku).IsUnique();
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages");
        builder.Property(i => i.Url).HasMaxLength(500).IsRequired();
        builder.Property(i => i.AltText).HasMaxLength(200).IsRequired();
        builder.Property(i => i.StorageKey).HasMaxLength(200);
        builder.Property(i => i.ContentType).HasMaxLength(50);
        builder.HasIndex(i => new { i.ProductId, i.SortOrder });
    }
}

internal sealed class CustomizationConfigurationConfiguration : IEntityTypeConfiguration<CustomizationConfiguration>
{
    public void Configure(EntityTypeBuilder<CustomizationConfiguration> builder)
    {
        builder.ToTable("CustomizationConfigurations");
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ModelUrl).HasMaxLength(500);
        builder.HasIndex(c => c.ProductId).IsUnique();
        builder.HasMany(c => c.PrintAreas).WithOne().HasForeignKey(a => a.CustomizationConfigurationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PrintAreaConfiguration : IEntityTypeConfiguration<PrintArea>
{
    public void Configure(EntityTypeBuilder<PrintArea> builder)
    {
        builder.ToTable("PrintAreas");
        builder.Property(a => a.Key).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Shape).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.WidthMm).HasPrecision(10, 2);
        builder.Property(a => a.HeightMm).HasPrecision(10, 2);
        builder.Property(a => a.UvX).HasPrecision(8, 5);
        builder.Property(a => a.UvY).HasPrecision(8, 5);
        builder.Property(a => a.UvWidth).HasPrecision(8, 5);
        builder.Property(a => a.UvHeight).HasPrecision(8, 5);
        builder.HasIndex(a => new { a.CustomizationConfigurationId, a.Key }).IsUnique();
    }
}
