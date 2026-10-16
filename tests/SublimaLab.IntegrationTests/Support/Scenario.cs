using System.Net;
using System.Text.Json;
using SublimaLab.Tests.Support;

namespace SublimaLab.IntegrationTests.Support;

/// <summary>Reusable steps for API flows.</summary>
public static class Scenario
{
    public const string Password = "Sublima-Test-42";

    public static string NewEmail() => $"customer-{Guid.NewGuid():N}@example.com";

    /// <summary>Registers a customer, confirms the email through the captured link and signs in.</summary>
    public static async Task<(ApiClient Client, string Email, Guid UserId)> SignedInCustomerAsync(SublimaLabFactory factory, ApiClient? client = null)
    {
        client ??= await factory.CreateApiClientAsync();
        var email = NewEmail();

        await client.PostAsync<JsonElement>("/api/auth/register", new { email, password = Password, firstName = "Ada", lastName = "Lovelace" });
        await ConfirmEmailAsync(factory, client, email);
        var user = await client.LoginAsync(email, Password);

        return (client, email, user.GetProperty("id").GetGuid());
    }

    public static async Task ConfirmEmailAsync(SublimaLabFactory factory, ApiClient client, string email)
    {
        var link = CapturingEmailSender.LinkParameters(factory.Emails.Last(email));
        var response = await client.SendAsync(HttpMethod.Post, "/api/auth/confirm-email", new { userId = link["userId"], token = link["token"] });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static async Task<ApiClient> SignedInAdminAsync(SublimaLabFactory factory)
    {
        var client = await factory.CreateApiClientAsync();
        await client.LoginAsync(SublimaLabFactory.AdminEmail, SublimaLabFactory.AdminPassword);
        return client;
    }

    public static async Task<JsonElement> ProductAsync(ApiClient client, string slug) =>
        await client.GetAsync<JsonElement>($"/api/products/{slug}");

    public static Guid VariantId(JsonElement product, string? size = null) =>
        product.GetProperty("variants").EnumerateArray()
            .First(v => size is null || v.GetProperty("size").GetString() == size)
            .GetProperty("id").GetGuid();

    public static async Task<Guid> UploadArtworkAsync(ApiClient client, int width = 2400, int height = 3000)
    {
        var upload = await client.UploadAsync(TestImages.Png(width, height), "artwork.png", "image/png");
        return upload.GetProperty("id").GetGuid();
    }

    public static object TeeDesign(Guid uploadId, decimal imageX = 150) => new
    {
        schemaVersion = 1,
        surfaces = new[]
        {
            new
            {
                printArea = "front",
                elements = new object[]
                {
                    new { type = "image", id = "artwork", uploadId, x = imageX, y = 200, width = 200, height = 250, rotation = 0 },
                    new { type = "text", id = "headline", text = "Made at SublimaLab", fontFamily = "Poppins", fontSize = 24, color = "#8B2CF5", bold = true, italic = false, align = "Center", letterSpacing = 0.05, x = 150, y = 40, width = 180, height = 20, rotation = 0 },
                },
            },
        },
    };

    public static object CheckoutRequest(Guid? idempotencyKey = null, string shippingMethodCode = "standard") => new
    {
        idempotencyKey = idempotencyKey ?? Guid.NewGuid(),
        contact = new { fullName = "Ada Lovelace", email = "ada@example.com", phone = "+1 555 0100" },
        shippingAddress = new { line1 = "12 Analytical Engine Way", line2 = (string?)null, city = "Springfield", region = "IL", postalCode = "62701", country = "US" },
        shippingMethodCode,
        saveAddress = true,
    };
}
