using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SublimaLab.Application.Accounts;
using SublimaLab.Application.Carts;
using SublimaLab.Application.Catalog;
using SublimaLab.Application.Customization;
using SublimaLab.Application.Orders;
using SublimaLab.Application.Payments;
using SublimaLab.Application.Shipping;
using SublimaLab.Application.Uploads;

namespace SublimaLab.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers application services as concrete classes: they have a single implementation
    /// and are exercised in tests with a real (SQLite) database, so interfaces would add
    /// indirection without value. Ports to the outside world (storage, payments, email) are interfaces.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<CatalogService>();
        services.AddScoped<Seo.SeoService>();
        services.AddScoped<DesignValidationService>();
        services.AddScoped<DesignService>();
        services.AddScoped<UploadService>();
        services.AddScoped<CartPricer>();
        services.AddScoped<CartService>();
        services.AddScoped<ShippingService>();
        services.AddScoped<OrderService>();
        services.AddScoped<AdminOrderService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<AccountService>();
        services.AddScoped<GuestSessionMerger>();
        services.AddScoped<Admin.AdminDashboardService>();
        services.AddScoped<Admin.AdminCatalogService>();
        services.AddScoped<Admin.AdminCustomerService>();
        services.AddScoped<Admin.ProductionArtworkService>();
        services.AddScoped<RetentionService>();

        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
