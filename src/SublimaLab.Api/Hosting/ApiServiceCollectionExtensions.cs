using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using SublimaLab.Api.Controllers;
using SublimaLab.Api.Http;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Infrastructure.Persistence;

namespace SublimaLab.Api.Hosting;

internal static class ApiServiceCollectionExtensions
{
    public const string CorsPolicy = "Frontend";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<CookieClientSession>();
        services.AddScoped<IClientSession>(sp => sp.GetRequiredService<CookieClientSession>());

        services
            .AddControllers(options => options.Filters.Add<AntiforgeryApiFilter>())
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.AllowOutOfOrderMetadataProperties = true;
                options.JsonSerializerOptions.MaxDepth = 32;
                options.AllowInputFormatterExceptionMessages = false;
            })
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiErrors.FromModelState);

        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        AddAuthentication(services);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "__Host-sl.af";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.SuppressXFrameOptionsHeader = true; // Set globally by SecurityHeadersMiddleware.
        });

        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            policy.WithOrigins(origins)
                .AllowCredentials()
                .WithHeaders("Content-Type", "X-XSRF-TOKEN")
                .WithMethods("GET", "POST", "PUT", "DELETE");
        }));

        services.AddApiRateLimiting(configuration);
        services.AddStorefrontRendering();

        services.AddOutputCache(options => options.AddPolicy(CatalogController.CachePolicy, policy => policy
            .Expire(TimeSpan.FromSeconds(60))
            .SetVaryByQuery("search", "category", "minPrice", "maxPrice", "customizable", "featured", "sort", "page", "pageSize")
            .Tag("catalog")));

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);
        services.AddHostedService<RetentionWorker>();

        // Compress static storefront assets only. API JSON is excluded on purpose: compressing
        // responses that mix secrets (antiforgery tokens) with reflected input enables BREACH-style attacks.
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ["text/html", "text/css", "text/javascript", "application/javascript", "image/svg+xml", "model/gltf-binary"];
        });
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "SublimaLab API";
            document.Info.Version = "v1";
            document.Info.Description =
                "REST API for the SublimaLab storefront and customization studio. Authentication uses an HttpOnly cookie; " +
                "unsafe requests must include the X-XSRF-TOKEN header obtained from GET /api/antiforgery/token.";
            return Task.CompletedTask;
        }));

        return services;
    }

    private static void AddAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-sl.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            // APIs answer with status codes instead of redirecting to a login page.
            options.Events.OnRedirectToLogin = context =>
                ApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "You need to sign in to continue.");
            options.Events.OnRedirectToAccessDenied = context =>
                ApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "FORBIDDEN", "You do not have permission to perform this action.");
        });

        // The password step of a two-factor sign-in only gets this short-lived cookie.
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            options.Cookie.Name = "__Host-sl.2fa";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });

        services.AddSingleton<IAuthorizationHandler, MultiFactorRequirementHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, MultiFactorAwareResultHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Admin)
                .AddRequirements(new MultiFactorRequirement()));
    }
}
