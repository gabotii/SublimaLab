using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Accounts;
using SublimaLab.Application.Common;
using SublimaLab.Infrastructure.Email;
using SublimaLab.Infrastructure.Identity;
using SublimaLab.Infrastructure.Imaging;
using SublimaLab.Infrastructure.Payments;
using SublimaLab.Infrastructure.Persistence;
using SublimaLab.Infrastructure.Persistence.Seed;
using SublimaLab.Infrastructure.Storage;

namespace SublimaLab.Infrastructure;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>"Sqlite" (local development, tests) or "Postgres" (Docker, production).</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>Apply pending migrations and seed data at startup.</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>
    /// Seed roles, the bootstrap admin and (if enabled) the catalog at startup without migrating,
    /// for deployments that apply migrations as a separate step (the migration bundle).
    /// </summary>
    public bool SeedOnStartup { get; set; }
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool RequireConfirmedEmail { get; set; } = true;

    /// <summary>Administrators must sign in with an authenticator code to use admin features.</summary>
    public bool RequireAdminTwoFactor { get; set; } = true;

    /// <summary>Issuer shown in authenticator apps.</summary>
    public string AuthenticatorIssuer { get; set; } = "SublimaLab";
}

public static class DependencyInjection
{
    // Migrations live in one assembly per provider, keeping provider-specific generated code
    // out of the core infrastructure assembly.
    public const string SqliteMigrationsAssembly = "SublimaLab.Migrations.Sqlite";
    public const string PostgresMigrationsAssembly = "SublimaLab.Migrations.Postgres";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CommerceOptions>().Bind(configuration.GetSection(CommerceOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<UploadOptions>().Bind(configuration.GetSection(UploadOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AppUrlOptions>().Bind(configuration.GetSection(AppUrlOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.Configure<LocalStorageOptions>(configuration.GetSection(LocalStorageOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        AddPersistence(services, configuration);
        AddIdentity(services, configuration);

        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IImageInspector, ImageHeaderInspector>();
        services.AddSingleton<IImageSanitizer, SkiaImageSanitizer>();
        services.AddSingleton<IPaymentGateway, TestPaymentGateway>();

        var emailProvider = configuration.GetValue<string>($"{EmailOptions.SectionName}:{nameof(EmailOptions.Provider)}");
        if (string.Equals(emailProvider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, PickupDirectoryEmailSender>();
        }

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetValue<string>($"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Provider)}") ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is not configured.");

        services.AddSingleton<AuditingInterceptor>();

        if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<PostgresAppDbContext>((sp, options) => options
                .UseNpgsql(connectionString, npgsql => npgsql
                    .MigrationsAssembly(PostgresMigrationsAssembly)
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
            services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<PostgresAppDbContext>());
        }
        else if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = ResolveSqlitePath(connectionString, configuration[HostDefaults.ContentRootKey]);
            services.AddDbContext<SqliteAppDbContext>((sp, options) => options
                .UseSqlite(connectionString, sqlite => sqlite
                    .MigrationsAssembly(SqliteMigrationsAssembly)
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
            services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<SqliteAppDbContext>());
        }
        else
        {
            throw new InvalidOperationException($"Unsupported database provider '{provider}'. Use 'Sqlite' or 'Postgres'.");
        }

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DatabaseInitializer>();
    }

    /// <summary>Resolves a relative SQLite file against the content root and makes sure its folder exists.</summary>
    private static string ResolveSqlitePath(string connectionString, string? contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource is "" or ":memory:" || builder.Mode == SqliteOpenMode.Memory)
        {
            return connectionString;
        }

        if (!Path.IsPathRooted(builder.DataSource) && !string.IsNullOrEmpty(contentRoot))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRoot, builder.DataSource));
        }

        var directory = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return builder.ToString();
    }

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = auth.RequireConfirmedEmail;

                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24));

        // Cookie re-validation rebuilds the principal from the database; keep the proof that
        // this session was signed in with a second factor.
        services.Configure<SecurityStampValidatorOptions>(o => o.OnRefreshingPrincipal = context =>
        {
            if (context.CurrentPrincipal?.FindFirst(MultiFactor.ClaimType) is { } amr && context.NewPrincipal?.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim(amr.Type, amr.Value));
            }

            return Task.CompletedTask;
        });

        // Keys protect auth cookies, antiforgery and account tokens: keep them across restarts
        // (App_Data is a persistent volume in Docker). Use shared storage when scaling out.
        var keysPath = configuration["DataProtection:KeysPath"] ?? "App_Data/keys";
        var contentRoot = configuration[HostDefaults.ContentRootKey] ?? Directory.GetCurrentDirectory();
        services.AddDataProtection()
            .SetApplicationName("SublimaLab")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.IsPathRooted(keysPath) ? keysPath : Path.Combine(contentRoot, keysPath)));
        services.AddHttpContextAccessor();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
    }

    /// <summary>Resolves the configured database options (used by the host at startup).</summary>
    public static DatabaseOptions GetDatabaseOptions(this IServiceProvider services) =>
        services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
}
