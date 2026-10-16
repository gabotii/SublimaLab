using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SublimaLab.Domain.Discounts;
using SublimaLab.Domain.Ordering;
using SublimaLab.Domain.Shipping;
using SublimaLab.Infrastructure.Identity;

namespace SublimaLab.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", t => t.HasCheckConstraint("CK_Orders_Total", "\"Total\" >= 0"));
        builder.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.ShippingMethodCode).HasMaxLength(40).IsRequired();
        builder.Property(o => o.ShippingMethodName).HasMaxLength(80).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(o => o.DiscountCode).HasMaxLength(40);
        builder.Property(o => o.Version).IsConcurrencyToken();
        builder.Ignore(o => o.IsPaid);

        builder.ComplexProperty(o => o.Contact, contact =>
        {
            contact.Property(c => c.FullName).HasColumnName("ContactName").HasMaxLength(120);
            contact.Property(c => c.Email).HasColumnName("ContactEmail").HasMaxLength(256);
            contact.Property(c => c.Phone).HasColumnName("ContactPhone").HasMaxLength(30);
        });

        builder.ComplexProperty(o => o.ShippingAddress, address =>
        {
            address.Property(a => a.Line1).HasColumnName("ShipLine1").HasMaxLength(200);
            address.Property(a => a.Line2).HasColumnName("ShipLine2").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("ShipCity").HasMaxLength(100);
            address.Property(a => a.Region).HasColumnName("ShipRegion").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("ShipPostalCode").HasMaxLength(20);
            address.Property(a => a.Country).HasColumnName("ShipCountry").HasMaxLength(2).IsFixedLength();
        });

        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => new { o.UserId, o.IdempotencyKey }).IsUnique();
        builder.HasIndex(o => new { o.UserId, o.PlacedAt });
        builder.HasIndex(o => new { o.Status, o.PlacedAt });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.Payments).WithOne().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.StatusHistory).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(o => o.Shipment).WithOne().HasForeignKey<Shipment>(s => s.OrderId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        // Product/variant ids are kept for reporting but intentionally not foreign keys:
        // the item is a snapshot that must outlive catalog changes.
        builder.ToTable("OrderItems", t => t.HasCheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" > 0"));
        builder.Property(i => i.ProductName).HasMaxLength(120).IsRequired();
        builder.Property(i => i.Sku).HasMaxLength(64).IsRequired();
        builder.Property(i => i.VariantLabel).HasMaxLength(100).IsRequired();
        builder.Property(i => i.ColorHex).HasMaxLength(7).IsRequired();
        builder.Property(i => i.ImageUrl).HasMaxLength(500);
        builder.Property(i => i.Design).HasJsonConversion();
        builder.Property(i => i.ProductionSpec).HasJsonConversion();
        builder.HasIndex(i => i.ProductId);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.Property(p => p.Provider).HasMaxLength(40).IsRequired();
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.HasIndex(p => new { p.Provider, p.ProviderPaymentId }).IsUnique();
    }
}

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments");
        builder.Property(s => s.Carrier).HasMaxLength(60).IsRequired();
        builder.Property(s => s.TrackingNumber).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(s => s.OrderId).IsUnique();
    }
}

internal sealed class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> builder)
    {
        builder.ToTable("OrderStatusHistory");
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.Note).HasMaxLength(500);
    }
}

internal sealed class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.ToTable("ShippingMethods");
        builder.Property(m => m.Code).HasMaxLength(40).IsRequired();
        builder.Property(m => m.Name).HasMaxLength(80).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(300);
        builder.HasIndex(m => m.Code).IsUnique();
    }
}

internal sealed class DiscountCodeConfiguration : IEntityTypeConfiguration<DiscountCode>
{
    public void Configure(EntityTypeBuilder<DiscountCode> builder)
    {
        builder.ToTable("DiscountCodes");
        builder.Property(d => d.Code).HasMaxLength(40).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(200);
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(d => d.Code).IsUnique();
    }
}
