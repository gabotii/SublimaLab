using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;

namespace SublimaLab.Api.Http;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Principal?.Identity?.IsAuthenticated == true && Guid.TryParse(Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public bool IsAuthenticated => UserId is not null;

    public bool IsAdmin => IsAuthenticated && Principal!.IsInRole(Roles.Admin);
}

/// <summary>
/// Anonymous session id stored in an HttpOnly, Secure, SameSite=Lax cookie. The value is 256 bits
/// of randomness, so it cannot be guessed; malformed values are ignored. The cookie is only
/// issued when a guest actually needs state (cart, upload).
/// </summary>
public sealed partial class CookieClientSession(IHttpContextAccessor accessor) : IClientSession
{
    public const string CookieName = "__Host-sl.sid";
    private const string ItemKey = "sl.session";

    public string? SessionId
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null)
            {
                return null;
            }

            if (context.Items.TryGetValue(ItemKey, out var cached))
            {
                return cached as string;
            }

            var value = context.Request.Cookies[CookieName];
            return value is not null && SessionIdPattern().IsMatch(value) ? value : null;
        }
    }

    public string GetOrCreateSessionId()
    {
        if (SessionId is { } existing)
        {
            return existing;
        }

        var context = accessor.HttpContext ?? throw new InvalidOperationException("No active HTTP request.");
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append(CookieName, id, CookieOptions(context));
        context.Items[ItemKey] = id;
        return id;
    }

    /// <summary>Drops the guest session (after its data was merged into an account).</summary>
    public void Clear()
    {
        var context = accessor.HttpContext;
        if (context is null)
        {
            return;
        }

        context.Response.Cookies.Delete(CookieName, CookieOptions(context));
        context.Items[ItemKey] = null;
    }

    private static CookieOptions CookieOptions(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
        MaxAge = TimeSpan.FromDays(30),
    };

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex SessionIdPattern();
}
