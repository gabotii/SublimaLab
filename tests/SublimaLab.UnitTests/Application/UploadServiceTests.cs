using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Common;
using SublimaLab.Application.Uploads;
using SublimaLab.Domain.Uploads;
using SublimaLab.Infrastructure.Imaging;
using SublimaLab.Tests.Support;

namespace SublimaLab.UnitTests.Application;

public sealed class UploadServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly InMemoryFileStorage _storage = new();
    private readonly FakeCurrentUser _user = new();
    private readonly FakeClientSession _session = new();
    private readonly SkiaImageSanitizer _sanitizer = new(NullLogger<SkiaImageSanitizer>.Instance);
    private readonly UploadOptions _options = new() { MaxArtworkBytes = 1024 * 1024, MinDimensionPx = 64, MaxDimensionPx = 5000, MaxMegapixels = 20, LowResolutionWarningPx = 1500 };

    [Fact]
    public async Task Valid_png_is_stored_with_server_detected_metadata()
    {
        var upload = await UploadAsync(TestImages.Png(1600, 1200), "My Design.png", "image/png");

        Assert.Equal(1600, upload.Width);
        Assert.Equal(1200, upload.Height);
        Assert.Equal("image/png", upload.ContentType);
        Assert.Equal($"/api/uploads/{upload.Id}/content", upload.Url);
        Assert.Null(upload.QualityWarning);
        Assert.Single(_storage.Files);
    }

    [Fact]
    public async Task Guests_get_an_anonymous_session_as_owner()
    {
        var upload = await UploadAsync(TestImages.Png(200, 200), "a.png", "image/png");

        var stored = _database.Context.Uploads.Single(u => u.Id == upload.Id);
        Assert.Null(stored.OwnerUserId);
        Assert.Equal(_session.SessionId, stored.SessionId);
    }

    [Fact]
    public async Task Small_artwork_gets_a_print_quality_warning()
    {
        var upload = await UploadAsync(TestImages.Png(800, 600), "small.png", "image/png");

        Assert.Equal(SublimaLab.Application.Customization.DesignRules.LowResolutionMessage, upload.QualityWarning);
    }

    [Theory]
    [InlineData("design.svg", "image/png")]
    [InlineData("design.png.exe", "image/png")]
    [InlineData("design.png", "image/svg+xml")]
    [InlineData("design.png", null)]
    public async Task Disallowed_names_or_content_types_are_rejected(string fileName, string? contentType)
    {
        var error = await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync(TestImages.Png(200, 200), fileName, contentType));
        Assert.Contains("file", error.Errors.Keys);
    }

    [Fact]
    public async Task Content_that_is_not_the_declared_image_type_is_rejected()
    {
        var html = Encoding.UTF8.GetBytes("<html><script>alert('xss')</script></html>");

        await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync(html, "evil.png", "image/png"));
        await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync(TestImages.Jpeg(200, 200), "photo.png", "image/png"));
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task Too_small_too_large_and_oversized_files_are_rejected()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync(TestImages.Png(32, 32), "tiny.png", "image/png"));
        await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync(TestImages.Jpeg(6000, 100), "wide.jpg", "image/jpeg"));
        await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync(TestImages.Jpeg(4900, 4900), "huge.jpg", "image/jpeg")); // > 20 MP
        await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync(new byte[_options.MaxArtworkBytes + 1], "big.png", "image/png"));
        await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync([], "empty.png", "image/png"));
    }

    [Fact]
    public async Task Declared_length_cannot_be_used_to_smuggle_a_larger_body()
    {
        var bytes = new byte[_options.MaxArtworkBytes + 10];
        TestImages.Png(100, 100).CopyTo(bytes, 0);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateService().UploadAsync(new MemoryStream(bytes), "a.png", "image/png", length: 100, UploadKind.Artwork, CancellationToken.None));
    }

    [Fact]
    public async Task Other_sessions_cannot_read_or_delete_an_upload()
    {
        var upload = await UploadAsync(TestImages.Png(200, 200), "mine.png", "image/png");
        var intruder = new UploadService(_database.Context, _storage, new ImageHeaderInspector(), _sanitizer, new FakeCurrentUser(), new FakeClientSession("someone-else"),
            Options.Create(_options), TimeProvider.System, NullLogger<UploadService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() => intruder.OpenContentAsync(upload.Id, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => intruder.DeleteAsync(upload.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Owners_can_delete_unused_uploads()
    {
        var upload = await UploadAsync(TestImages.Png(200, 200), "mine.png", "image/png");

        await CreateService().DeleteAsync(upload.Id, CancellationToken.None);

        Assert.Empty(_storage.Files);
        Assert.Empty(_database.Context.Uploads);
    }

    [Theory]
    [InlineData("../../etc/passwd.png", "passwd.png")]
    [InlineData("C:\\Users\\x\\photo.png", "photo.png")]
    [InlineData("<script>.png", "script.png")]
    [InlineData("   ", "image")]
    public void Display_file_names_are_sanitized(string input, string expected) =>
        Assert.Equal(expected, UploadService.SafeFileName(input));

    [Fact]
    public async Task Stored_file_is_re_encoded_without_appended_payloads()
    {
        var payload = Encoding.ASCII.GetBytes("<?php system($_GET['c']); ?>");
        var upload = await UploadAsync([.. TestImages.Png(200, 200), .. payload], "polyglot.png", "image/png");

        var stored = Assert.Single(_storage.Files).Value;
        Assert.Equal(-1, stored.AsSpan().IndexOf(payload));
        Assert.Equal(stored.LongLength, upload.SizeBytes);
    }

    [Fact]
    public async Task Uploads_are_counted_by_outcome()
    {
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == StoreMetrics.MeterName && instrument.Name == "sublimalab.uploads")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                {
                    outcomes.Add((string)tag.Value!);
                }
            }
        });
        listener.Start();

        await UploadAsync(TestImages.Png(200, 200), "ok.png", "image/png");
        await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync(Encoding.UTF8.GetBytes("<html>"), "bad.png", "image/png"));

        Assert.Contains("accepted", outcomes);
        Assert.Contains("rejected", outcomes);
    }

    [Fact]
    public async Task Undecodable_images_are_rejected()
    {
        var png = TestImages.Png(200, 200);
        png[^20] ^= 0xFF; // corrupt the compressed pixel data

        await Assert.ThrowsAsync<RequestValidationException>(() => UploadAsync(png, "broken.png", "image/png"));
        Assert.Empty(_storage.Files);
    }

    public void Dispose()
    {
        _sanitizer.Dispose();
        _database.Dispose();
    }

    private Task<UploadDto> UploadAsync(byte[] bytes, string fileName, string? contentType) =>
        CreateService().UploadAsync(new MemoryStream(bytes), fileName, contentType, bytes.Length, UploadKind.Artwork, CancellationToken.None);

    private UploadService CreateService() =>
        new(_database.Context, _storage, new ImageHeaderInspector(), _sanitizer, _user, _session, Options.Create(_options), TimeProvider.System, NullLogger<UploadService>.Instance);
}
