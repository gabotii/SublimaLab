using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using SublimaLab.Api.Http;
using SublimaLab.Application.Common;
using SublimaLab.Application.Uploads;
using SublimaLab.Domain.Uploads;

namespace SublimaLab.Api.Controllers;

public sealed class UploadForm
{
    public IFormFile? File { get; set; }

    public UploadKind Kind { get; set; } = UploadKind.Artwork;
}

[ApiController]
[Route("api/uploads")]
public sealed class UploadsController(UploadService uploads) : ControllerBase
{
    // Slightly above the largest allowed file to leave room for multipart framing;
    // the precise per-kind limit is enforced by UploadService.
    private const long MaxRequestBytes = 16 * 1024 * 1024;

    /// <summary>Uploads a PNG, JPG or WebP image (multipart field "file"). Guests may upload too.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    public async Task<ActionResult<UploadDto>> Upload([FromForm] UploadForm form, CancellationToken cancellationToken)
    {
        if (form.File is null)
        {
            throw RequestValidationException.For("file", "Choose an image to upload.");
        }

        if (!Enum.IsDefined(form.Kind))
        {
            throw RequestValidationException.For("kind", "Unknown upload kind.");
        }

        await using var stream = form.File.OpenReadStream();
        var upload = await uploads.UploadAsync(stream, form.File.FileName, form.File.ContentType, form.File.Length, form.Kind, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = upload.Id }, upload);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UploadDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await uploads.GetAsync(id, cancellationToken));

    /// <summary>Streams the image. Only its owner (user or guest session) and admins can read it.</summary>
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, CancellationToken cancellationToken)
    {
        var content = await uploads.OpenContentAsync(id, cancellationToken);

        // Content is immutable per id; "private" keeps it out of shared caches.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(content.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();

        return File(content.Content, content.ContentType);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await uploads.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
