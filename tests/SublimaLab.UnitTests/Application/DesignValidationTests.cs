using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SublimaLab.Application.Customization;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Uploads;
using SublimaLab.Tests.Support;

namespace SublimaLab.UnitTests.Application;

public sealed class DesignValidationTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());
    private readonly FakeClientSession _session = new("guest-session");

    [Fact]
    public async Task A_well_placed_design_is_valid()
    {
        var result = await ValidateAsync(TestData.Design(("front", [TestData.Text()])));

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(10, 100)]   // left edge outside
    [InlineData(295, 100)]  // right edge outside
    [InlineData(150, 395)]  // bottom edge outside
    public async Task Elements_outside_the_print_area_are_rejected(decimal x, decimal y)
    {
        var result = await ValidateAsync(TestData.Design(("front", [TestData.Text(x: x, y: y)])));

        Assert.False(result.IsValid);
        Assert.Contains("design.surfaces[0].elements[0]", result.Errors.Keys);
    }

    [Fact]
    public async Task Rotation_is_taken_into_account_for_bounds()
    {
        // 280mm wide text fits unrotated in a 300mm area, but not when rotated 45 degrees near the top.
        var unrotated = await ValidateAsync(TestData.Design(("front", [TestData.Text(width: 280, height: 20, y: 30)])));
        var rotated = await ValidateAsync(TestData.Design(("front", [TestData.Text(width: 280, height: 20, y: 30, rotation: 45)])));

        Assert.True(unrotated.IsValid);
        Assert.False(rotated.IsValid);
    }

    [Fact]
    public async Task Unknown_print_areas_are_rejected()
    {
        var result = await ValidateAsync(TestData.Design(("sleeve", [TestData.Text()])));

        Assert.Contains("design.surfaces[0].printArea", result.Errors.Keys);
    }

    [Fact]
    public async Task Too_many_elements_per_area_are_rejected()
    {
        var elements = Enumerable.Range(0, 4).Select(i => (DesignElement)TestData.Text($"t{i}", y: 50 + (i * 40))).ToArray();

        var result = await ValidateAsync(TestData.Design(("front", elements)));

        Assert.Contains("design.surfaces[0].elements", result.Errors.Keys);
    }

    [Fact]
    public async Task Structural_rules_reject_unsupported_fonts_colors_and_duplicate_ids()
    {
        var badText = TestData.Text() with { FontFamily = "Comic Sans", Color = "red" };
        var result = await ValidateAsync(TestData.Design(("front", [badText, TestData.Text()])));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors.Keys, k => k.EndsWith(".fontFamily", StringComparison.Ordinal));
        Assert.Contains(result.Errors.Keys, k => k.EndsWith(".color", StringComparison.Ordinal));
        Assert.Contains("design.surfaces", result.Errors.Keys); // duplicate element ids
    }

    [Fact]
    public async Task Images_must_reference_an_accessible_artwork_upload()
    {
        var foreignUpload = await AddUploadAsync(ownerUserId: Guid.NewGuid(), widthPx: 3000, heightPx: 3000);

        var missing = await ValidateAsync(TestData.Design(("front", [TestData.Image(Guid.NewGuid())])));
        var foreign = await ValidateAsync(TestData.Design(("front", [TestData.Image(foreignUpload)])));

        Assert.Contains("design.surfaces[0].elements[0].uploadId", missing.Errors.Keys);
        Assert.Contains("design.surfaces[0].elements[0].uploadId", foreign.Errors.Keys);
    }

    [Fact]
    public async Task Low_resolution_artwork_produces_a_warning_not_an_error()
    {
        // 600px over 200mm = ~76 DPI, below the 150 DPI recommendation.
        var uploadId = await AddUploadAsync(ownerUserId: _user.UserId, widthPx: 600, heightPx: 750);

        var result = await ValidateAsync(TestData.Design(("front", [TestData.Image(uploadId)])));

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("LOW_RESOLUTION", warning.Code);
        Assert.Equal(76, warning.EffectiveDpi);
        Assert.Equal(DesignRules.LowResolutionMessage, warning.Message);
    }

    [Fact]
    public async Task Guests_can_use_uploads_from_their_own_session()
    {
        _user.UserId = null;
        var uploadId = await AddUploadAsync(ownerUserId: null, sessionId: "guest-session", widthPx: 3000, heightPx: 3750);

        var result = await ValidateAsync(TestData.Design(("front", [TestData.Image(uploadId)])));

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Products_without_customization_are_rejected()
    {
        var product = TestData.CustomizableTee();
        product.Customization!.IsEnabled = false;

        var result = await CreateService().ValidateAsync(product, TestData.Design(("front", [TestData.Text()])), "design", CancellationToken.None);

        Assert.Contains("design", result.Errors.Keys);
    }

    [Fact]
    public void Design_documents_round_trip_through_json_with_type_discriminators()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
        var design = TestData.Design(("front", [TestData.Image(Guid.NewGuid()), TestData.Text()]));

        var json = JsonSerializer.Serialize(design, options);
        var back = JsonSerializer.Deserialize<DesignDocument>(json, options)!;

        Assert.Contains("\"type\":\"image\"", json, StringComparison.Ordinal);
        Assert.IsType<ImageElement>(back.Surfaces[0].Elements[0]);
        Assert.IsType<TextElement>(back.Surfaces[0].Elements[1]);
        Assert.Equal(JsonSerializer.Serialize(back, options), json);
    }

    [Fact]
    public void Unknown_element_types_cannot_be_deserialized()
    {
        const string json = """{"schemaVersion":1,"surfaces":[{"printArea":"front","elements":[{"type":"script","id":"x"}]}]}""";

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<DesignDocument>(json, JsonSerializerOptions.Web));
    }

    public void Dispose() => _database.Dispose();

    private Task<DesignValidationResult> ValidateAsync(DesignDocument design) =>
        CreateService().ValidateAsync(TestData.CustomizableTee(), design, "design", CancellationToken.None);

    private DesignValidationService CreateService() =>
        new(_database.Context, _user, _session, new DesignDocumentValidator());

    private async Task<Guid> AddUploadAsync(Guid? ownerUserId, int widthPx, int heightPx, string? sessionId = null)
    {
        var upload = new ArtworkUpload(ownerUserId, ownerUserId is null ? sessionId : null, UploadKind.Artwork,
            $"2026/10/{Guid.NewGuid():N}.png", "art.png", "image/png", 1234, widthPx, heightPx, new string('a', 64), DateTime.UtcNow);

        // Bypass the Identity FK for the test by inserting with foreign keys disabled.
        await _database.Context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        _database.Context.Uploads.Add(upload);
        await _database.Context.SaveChangesAsync();
        return upload.Id;
    }
}
