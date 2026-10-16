using System.Net;
using System.Xml.Linq;
using SublimaLab.IntegrationTests.Support;

namespace SublimaLab.IntegrationTests;

public sealed class SeoTests(SublimaLabFactory factory) : IClassFixture<SublimaLabFactory>
{
    [Fact]
    public async Task Robots_txt_hides_private_areas_and_points_to_the_sitemap()
    {
        var response = await factory.CreateClient().GetAsync("/robots.txt");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Disallow: /admin", text, StringComparison.Ordinal);
        Assert.Contains("Disallow: /checkout", text, StringComparison.Ordinal);
        Assert.Contains("Sitemap: https://localhost/sitemap.xml", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sitemap_lists_public_pages_categories_and_active_products()
    {
        var response = await factory.CreateClient().GetAsync("/sitemap.xml");
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = xml.Descendants(ns + "loc").Select(l => l.Value).ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("https://localhost/", locations);
        Assert.Contains("https://localhost/privacy", locations);
        Assert.Contains("https://localhost/shop/mugs", locations);
        Assert.Contains(locations, l => l.StartsWith("https://localhost/products/", StringComparison.Ordinal));
        Assert.DoesNotContain(locations, l => l.Contains("/account", StringComparison.Ordinal) || l.Contains("/admin", StringComparison.Ordinal));
        Assert.All(xml.Descendants(ns + "url").Where(u => u.Element(ns + "loc")!.Value.Contains("/products/", StringComparison.Ordinal)),
            url => Assert.NotNull(url.Element(ns + "lastmod")));
    }
}
