using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SublimaLab.IntegrationTests.Support;

namespace SublimaLab.IntegrationTests;

public sealed partial class StorefrontRenderingTests(SublimaLabFactory factory) : IClassFixture<SublimaLabFactory>
{
    [Fact]
    public async Task Public_pages_are_prerendered_as_complete_html()
    {
        var response = await factory.CreateClient().GetAsync("/products/classic-white-t-shirt");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1", html, StringComparison.Ordinal);
        Assert.Contains("Classic White T-Shirt", html, StringComparison.Ordinal);
        Assert.Contains("<title>Classic White T-Shirt — SublimaLab</title>", html, StringComparison.Ordinal);
        Assert.Contains("application/ld+json", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"app-shell\"", html, StringComparison.Ordinal);

        // The API responses used for the render are handed to the WebAssembly app.
        Assert.Contains("api/products/classic-white-t-shirt", PersistedState(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prerendering_sees_the_visitor_as_signed_in()
    {
        var (customer, email, _) = await Scenario.SignedInCustomerAsync(factory);

        var html = await customer.Http.GetStringAsync("/");

        // The server's internal API calls carried the visitor's auth cookie.
        Assert.Contains(email, PersistedState(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Private_pages_render_only_in_the_browser()
    {
        var html = await factory.CreateClient().GetStringAsync("/account");

        Assert.Contains("id=\"app-shell\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("My account", html, StringComparison.Ordinal);
        Assert.Contains("_framework/blazor.web.js", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_pages_return_404_with_the_not_found_page()
    {
        var response = await factory.CreateClient().GetAsync("/this-page-does-not-exist");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("We couldn't find that page", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    /// <summary>Decodes the component state Blazor embeds in prerendered pages.</summary>
    private static string PersistedState(string html)
    {
        var match = StatePattern().Match(html);
        Assert.True(match.Success, "The page has no persisted component state.");

        // A base64 JSON object whose values are themselves base64-encoded JSON.
        using var state = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(match.Groups[1].Value));
        var responses = state.RootElement.GetProperty("sl-api-responses").GetString()!;
        return Encoding.UTF8.GetString(Convert.FromBase64String(responses));
    }

    [GeneratedRegex("<!--Blazor-WebAssembly-Component-State:([A-Za-z0-9+/=]+)-->")]
    private static partial Regex StatePattern();
}
