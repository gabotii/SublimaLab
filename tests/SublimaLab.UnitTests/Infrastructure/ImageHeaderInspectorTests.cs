using System.Text;
using SublimaLab.Application.Abstractions;
using SublimaLab.Infrastructure.Imaging;
using SublimaLab.Tests.Support;

namespace SublimaLab.UnitTests.Infrastructure;

public class ImageHeaderInspectorTests
{
    private readonly ImageHeaderInspector _inspector = new();

    [Fact]
    public void Reads_png_dimensions() => AssertImage(TestImages.Png(640, 480), ImageFormat.Png, 640, 480);

    [Fact]
    public void Reads_jpeg_dimensions_after_skipping_app_segments() => AssertImage(TestImages.Jpeg(3000, 2000), ImageFormat.Jpeg, 3000, 2000);

    [Fact]
    public void Reads_lossy_webp_dimensions() => AssertImage(TestImages.WebPLossy(1024, 768), ImageFormat.WebP, 1024, 768);

    [Fact]
    public void Reads_lossless_webp_dimensions() => AssertImage(TestImages.WebPLossless(800, 600), ImageFormat.WebP, 800, 600);

    [Fact]
    public void Reads_extended_webp_dimensions() => AssertImage(TestImages.WebPExtended(4096, 2160), ImageFormat.WebP, 4096, 2160);

    [Fact]
    public void Rejects_animated_webp() => Assert.Null(Inspect(TestImages.WebPExtended(100, 100, animated: true)));

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("GIF89a............")]
    [InlineData("<?php echo 'hi'; ?>")]
    [InlineData("")]
    public void Rejects_non_image_content(string content) => Assert.Null(Inspect(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public void Rejects_truncated_files()
    {
        Assert.Null(Inspect(TestImages.Png(10, 10)[..16]));
        Assert.Null(Inspect(TestImages.Jpeg(10, 10)[..6]));
        Assert.Null(Inspect(TestImages.WebPExtended(10, 10)[..24]));
        Assert.Null(Inspect(TestImages.WebPLossless(10, 10)[..22]));
    }

    [Fact]
    public void Rejects_zero_dimensions() => Assert.Null(Inspect(TestImages.Png(0, 10)));

    [Fact]
    public void Leaves_the_stream_at_its_original_position()
    {
        using var stream = new MemoryStream(TestImages.Jpeg(100, 100));

        _inspector.Inspect(stream);

        Assert.Equal(0, stream.Position);
    }

    private void AssertImage(byte[] bytes, ImageFormat format, int width, int height)
    {
        var info = Inspect(bytes);

        Assert.NotNull(info);
        Assert.Equal(new ImageInfo(format, width, height), info);
    }

    private ImageInfo? Inspect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return _inspector.Inspect(stream);
    }
}
