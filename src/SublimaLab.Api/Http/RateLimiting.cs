using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SublimaLab.Api.Hosting;
using SublimaLab.Application.Abstractions;

namespace SublimaLab.Api.Http;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per minute per user/IP across the whole API.</summary>
    public int GlobalPermitLimit { get; set; } = 300;

    /// <summary>Sign-in, registration and recovery attempts per minute per IP.</summary>
    public int AuthPermitLimit { get; set; } = 10;

    /// <summary>Uploads per minute per user/session/IP.</summary>
    public int UploadPermitLimit { get; set; } = 30;
}

internal static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Uploads = "uploads";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await ApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "RATE_LIMITED", "Too many requests. Please wait a moment and try again.");
            };

            // Prerender calls the app makes to itself are already covered by the visitor's page request.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.RequestServices.GetRequiredService<InternalRequestKey>().IsInternal(context)
                    ? RateLimitPartition.GetNoLimiter("internal")
                    : RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => Window(options.GlobalPermitLimit)));

            limiter.AddPolicy(Auth, context =>
                RateLimitPartition.GetFixedWindowLimiter(IpKey(context), _ => Window(options.AuthPermitLimit)));

            limiter.AddPolicy(Uploads, context =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => Window(options.UploadPermitLimit)));
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    };

    private static string ClientKey(HttpContext context)
    {
        var userId = context.RequestServices.GetRequiredService<ICurrentUser>().UserId;
        return userId is { } id ? $"user:{id}" : IpKey(context);
    }

    private static string IpKey(HttpContext context) => $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
