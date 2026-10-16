using System.Net;
using Microsoft.Extensions.Configuration;
using SublimaLab.Api.Hosting;

namespace SublimaLab.IntegrationTests;

public class ReverseProxySetupTests
{
    [Fact]
    public void Disabled_by_default() => Assert.Null(ReverseProxySetup.CreateOptions(Config()));

    [Fact]
    public void Only_configured_proxies_are_trusted()
    {
        var options = ReverseProxySetup.CreateOptions(Config(
            ("Enabled", "true"), ("KnownNetworks:0", "172.28.0.0/24"), ("KnownProxies:0", "10.0.0.5")))!;

        var network = Assert.Single(options.KnownIPNetworks);
        Assert.True(network.Contains(IPAddress.Parse("172.28.0.9")));
        Assert.False(network.Contains(IPAddress.Parse("203.0.113.7")));
        Assert.Equal(IPAddress.Parse("10.0.0.5"), Assert.Single(options.KnownProxies));
        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void Enabling_without_a_trusted_proxy_fails_at_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ReverseProxySetup.CreateOptions(Config(("Enabled", "true"))));
        Assert.Contains("KnownNetworks", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Trusting_every_sender_needs_an_explicit_opt_in()
    {
        var options = ReverseProxySetup.CreateOptions(Config(("Enabled", "true"), ("TrustAllProxies", "true")))!;

        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
    }

    [Theory]
    [InlineData("KnownNetworks:0", "not-a-range")]
    [InlineData("KnownProxies:0", "999.1.1.1")]
    public void Invalid_addresses_are_rejected(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => ReverseProxySetup.CreateOptions(Config(("Enabled", "true"), (key, value))));

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>($"ReverseProxy:{v.Key}", v.Value)))
            .Build();
}
