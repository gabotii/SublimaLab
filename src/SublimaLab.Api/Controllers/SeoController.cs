using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using SublimaLab.Application.Seo;

namespace SublimaLab.Api.Controllers;

/// <summary>Crawler files at the site root, generated from configuration and the live catalog.</summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class SeoController(SeoService seo) : ControllerBase
{
    [HttpGet("/robots.txt")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public ContentResult Robots() => Content(seo.GetRobotsTxt(), "text/plain; charset=utf-8");

    [HttpGet("/sitemap.xml")]
    [OutputCache(PolicyName = CatalogController.CachePolicy)]
    public async Task<ContentResult> Sitemap(CancellationToken cancellationToken) =>
        Content(await seo.GetSitemapXmlAsync(cancellationToken), "application/xml; charset=utf-8");
}
