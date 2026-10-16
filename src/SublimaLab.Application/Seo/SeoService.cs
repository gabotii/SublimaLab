using System.Globalization;
using System.Text;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;

namespace SublimaLab.Application.Seo;

/// <summary>Builds robots.txt and the XML sitemap from the public catalog.</summary>
public sealed class SeoService(IAppDbContext db, IOptions<AppUrlOptions> urls)
{
    /// <summary>Public pages that aren't part of the catalog.</summary>
    private static readonly string[] StaticPaths = ["/", "/shop", "/shipping-returns", "/contact", "/terms", "/privacy"];

    /// <summary>Private or per-visitor pages crawlers should skip.</summary>
    private static readonly string[] DisallowedPaths = ["/account", "/admin", "/cart", "/checkout", "/customize/", "/api/"];

    private string BaseUrl => urls.Value.PublicBaseUrl.TrimEnd('/');

    public string GetRobotsTxt()
    {
        var text = new StringBuilder("User-agent: *\n");
        foreach (var path in DisallowedPaths)
        {
            text.Append("Disallow: ").Append(path).Append('\n');
        }

        return text.Append("\nSitemap: ").Append(BaseUrl).Append("/sitemap.xml\n").ToString();
    }

    public async Task<string> GetSitemapXmlAsync(CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Slug, c.UpdatedAt })
            .ToListAsync(cancellationToken);

        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Category!.IsActive)
            .OrderBy(p => p.Slug)
            .Select(p => new { p.Slug, p.UpdatedAt })
            .ToListAsync(cancellationToken);

        var settings = new XmlWriterSettings { Indent = true, Async = true, Encoding = new UTF8Encoding(false) };
        using var output = new StringWriterWithEncoding(settings.Encoding);
        await using (var xml = XmlWriter.Create(output, settings))
        {
            await xml.WriteStartDocumentAsync();
            xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            foreach (var path in StaticPaths)
            {
                WriteUrl(xml, path, null);
            }

            foreach (var category in categories)
            {
                WriteUrl(xml, $"/shop/{Uri.EscapeDataString(category.Slug)}", category.UpdatedAt);
            }

            foreach (var product in products)
            {
                WriteUrl(xml, $"/products/{Uri.EscapeDataString(product.Slug)}", product.UpdatedAt);
            }

            await xml.WriteEndElementAsync();
            await xml.WriteEndDocumentAsync();
        }

        return output.ToString();
    }

    private void WriteUrl(XmlWriter xml, string path, DateTime? lastModified)
    {
        xml.WriteStartElement("url");
        xml.WriteElementString("loc", BaseUrl + path);
        if (lastModified is { } date && date > DateTime.MinValue)
        {
            xml.WriteElementString("lastmod", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        xml.WriteEndElement();
    }

    private sealed class StringWriterWithEncoding(Encoding encoding) : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => encoding;
    }
}
