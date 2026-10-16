using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace SublimaLab.Api.Hosting;

/// <summary>
/// Builds the forwarded-headers policy from the <c>ReverseProxy</c> section. X-Forwarded-For and
/// X-Forwarded-Proto are honoured only from the configured proxy addresses; anyone else could
/// otherwise spoof the client IP (defeating rate limiting) or the scheme.
/// </summary>
internal static class ReverseProxySetup
{
    public const string SectionName = "ReverseProxy";

    /// <returns>The options to use, or null when the app is not behind a proxy.</returns>
    /// <exception cref="InvalidOperationException">Proxy mode is on but no trusted proxy is configured.</exception>
    public static ForwardedHeadersOptions? CreateOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        if (!section.GetValue<bool>("Enabled"))
        {
            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = section.GetValue("ForwardLimit", 1),
        };

        // Defaults trust loopback only; replace them with exactly what is configured.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.TryParse(network, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"ReverseProxy:KnownNetworks contains an invalid CIDR range: '{network}'."));
        }

        foreach (var proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            options.KnownProxies.Add(IPAddress.TryParse(proxy, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"ReverseProxy:KnownProxies contains an invalid IP address: '{proxy}'."));
        }

        if (options.KnownIPNetworks.Count == 0 && options.KnownProxies.Count == 0)
        {
            if (!section.GetValue<bool>("TrustAllProxies"))
            {
                throw new InvalidOperationException(
                    "ReverseProxy:Enabled is true but no trusted proxy is configured. Set ReverseProxy:KnownNetworks (CIDR ranges) " +
                    "or ReverseProxy:KnownProxies (IP addresses) to your proxy or ingress. ReverseProxy:TrustAllProxies=true accepts " +
                    "any sender, and is only safe when the app port cannot be reached except through the proxy.");
            }

            // Explicit opt-in: with both lists empty the middleware accepts forwarded headers from any address.
        }

        return options;
    }
}
