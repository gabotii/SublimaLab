using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SublimaLab.IntegrationTests.Support;
using SublimaLab.Tests.Support;

namespace SublimaLab.IntegrationTests;

public sealed class AdminTests(SublimaLabFactory factory) : IClassFixture<SublimaLabFactory>
{
    [Theory]
    [InlineData("GET", "/api/admin/dashboard")]
    [InlineData("GET", "/api/admin/products")]
    [InlineData("GET", "/api/admin/customers")]
    [InlineData("GET", "/api/admin/categories")]
    [InlineData("POST", "/api/admin/products")]
    public async Task Admin_endpoints_are_forbidden_to_customers(string method, string url)
    {
        var (customer, _, _) = await Scenario.SignedInCustomerAsync(factory);

        var response = await customer.SendAsync(new HttpMethod(method), url, method == "POST" ? new { } : null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrators_can_create_configure_and_publish_a_product()
    {
        var admin = await Scenario.SignedInAdminAsync(factory);
        var shopper = await factory.CreateApiClientAsync();
        var categories = await admin.GetAsync<JsonElement>("/api/admin/categories");
        var mugs = categories.EnumerateArray().First(c => c.GetProperty("slug").GetString() == "mugs").GetProperty("id").GetGuid();

        // Warm the storefront cache so we can prove the admin change evicts it.
        await shopper.GetAsync<JsonElement>("/api/products?category=mugs");

        var created = await admin.PostAsync<JsonElement>("/api/admin/products", new
        {
            categoryId = mugs,
            name = "Travel Tumbler",
            slug = "travel-tumbler",
            shortDescription = "Insulated steel tumbler",
            description = "Keeps drinks hot for hours.",
            basePrice = 24.5m,
            isActive = false,
            isFeatured = false,
            productionTimeDays = 3,
            specifications = new[] { new { name = "Capacity", value = "20 oz" } },
        }, HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        var activateTooEarly = await admin.SendAsync(HttpMethod.Put, $"/api/admin/products/{id}/status", new { isActive = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, activateTooEarly.StatusCode);

        var withVariants = await admin.PutAsync<JsonElement>($"/api/admin/products/{id}/variants", new
        {
            variants = new[]
            {
                new { id = (Guid?)null, sku = "TUM-SLV-20", colorName = "Silver", colorHex = "#C0C0C0", size = "20 oz", priceAdjustment = 0m, isActive = true },
                new { id = (Guid?)null, sku = "TUM-BLK-20", colorName = "Black", colorHex = "#111111", size = "20 oz", priceAdjustment = 2m, isActive = true },
            },
        });
        Assert.Equal(2, withVariants.GetProperty("variants").GetArrayLength());

        var customized = await admin.PutAsync<JsonElement>($"/api/admin/products/{id}/customization", new
        {
            isEnabled = true,
            kind = "Drinkware",
            modelUrl = "/models/mug.glb",
            baseFee = 4m,
            additionalPrintAreaFee = 0m,
            maxElementsPerArea = 6,
            minRecommendedDpi = 200,
            printAreas = new[] { new { key = "wrap", name = "Wrap-around", widthMm = 220m, heightMm = 90m, shape = "Rectangle" } },
        });
        Assert.Equal("wrap", customized.GetProperty("customization").GetProperty("printAreas")[0].GetProperty("key").GetString());

        await admin.PutAsync<JsonElement>($"/api/admin/products/{id}/status", new { isActive = true });

        // Visible to shoppers right away (catalog cache evicted), with server-computed prices.
        var catalog = await shopper.GetAsync<JsonElement>("/api/products?category=mugs");
        Assert.Contains(catalog.GetProperty("items").EnumerateArray(), p => p.GetProperty("slug").GetString() == "travel-tumbler");
        var detail = await shopper.GetAsync<JsonElement>("/api/products/travel-tumbler");
        Assert.Equal(26.5m, detail.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("sku").GetString() == "TUM-BLK-20").GetProperty("price").GetDecimal());
        Assert.Equal(220m, detail.GetProperty("customization").GetProperty("printAreas")[0].GetProperty("widthMm").GetDecimal());

        // Removing a variant deactivates it instead of deleting it.
        var silver = withVariants.GetProperty("variants").EnumerateArray().First(v => v.GetProperty("sku").GetString() == "TUM-SLV-20");
        var updated = await admin.PutAsync<JsonElement>($"/api/admin/products/{id}/variants", new
        {
            variants = new[] { new { id = (Guid?)silver.GetProperty("id").GetGuid(), sku = "TUM-SLV-20", colorName = "Silver", colorHex = "#C0C0C0", size = "20 oz", priceAdjustment = 1m, isActive = true } },
        });
        Assert.False(updated.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("sku").GetString() == "TUM-BLK-20").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Product_validation_rejects_duplicate_slugs_and_skus()
    {
        var admin = await Scenario.SignedInAdminAsync(factory);
        var categories = await admin.GetAsync<JsonElement>("/api/admin/categories");
        var categoryId = categories[0].GetProperty("id").GetGuid();

        var duplicateSlug = await admin.SendAsync(HttpMethod.Post, "/api/admin/products", new
        {
            categoryId,
            name = "Copy",
            slug = "classic-white-t-shirt",
            basePrice = 10m,
            isActive = false,
            isFeatured = false,
            productionTimeDays = 1,
        });
        var error = await ApiClient.ReadAsync<JsonElement>(duplicateSlug, HttpStatusCode.BadRequest);
        Assert.True(error.GetProperty("errors").TryGetProperty("slug", out _));

        var product = await admin.PostAsync<JsonElement>("/api/admin/products", new
        {
            categoryId,
            name = "Sku Clash",
            slug = "sku-clash",
            basePrice = 10m,
            isActive = false,
            isFeatured = false,
            productionTimeDays = 1,
        }, HttpStatusCode.Created);
        var clash = await admin.SendAsync(HttpMethod.Put, $"/api/admin/products/{product.GetProperty("id").GetGuid()}/variants", new
        {
            variants = new[] { new { id = (Guid?)null, sku = "TSH-CW-WHI-M", colorName = "White", colorHex = "#FFFFFF", size = "M", priceAdjustment = 0m, isActive = true } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
    }

    [Fact]
    public async Task Product_photos_are_validated_stored_and_served_publicly()
    {
        var admin = await Scenario.SignedInAdminAsync(factory);
        var products = await admin.GetAsync<JsonElement>("/api/admin/products?search=square");
        var id = products.GetProperty("items")[0].GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.Png(600, 600));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "square.png");
        var product = await ApiClient.ReadAsync<JsonElement>(await admin.SendAsync(HttpMethod.Post, $"/api/admin/products/{id}/images", form), HttpStatusCode.OK);
        var uploaded = product.GetProperty("images").EnumerateArray().Single(i => i.GetProperty("isUploaded").GetBoolean());

        var anonymous = await factory.CreateApiClientAsync();
        var image = await anonymous.Http.GetAsync(uploaded.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);

        using var fake = new MultipartFormDataContent();
        var html = new ByteArrayContent("<html></html>"u8.ToArray());
        html.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        fake.Add(html, "file", "fake.png");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, $"/api/admin/products/{id}/images", fake)).StatusCode);

        await admin.SendAsync(HttpMethod.Delete, $"/api/admin/products/{id}/images/{uploaded.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.Http.GetAsync(uploaded.GetProperty("url").GetString())).StatusCode);
    }

    [Fact]
    public async Task Production_package_contains_design_spec_and_original_artwork()
    {
        var (customer, _, _) = await Scenario.SignedInCustomerAsync(factory);
        var tee = await Scenario.ProductAsync(customer, "classic-white-t-shirt");
        var uploadId = await Scenario.UploadArtworkAsync(customer);
        await customer.PostAsync<JsonElement>("/api/cart/items", new { productId = tee.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(tee, "M"), quantity = 1, design = Scenario.TeeDesign(uploadId) });
        var order = await customer.PostAsync<JsonElement>("/api/orders", Scenario.CheckoutRequest(), HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();

        var admin = await Scenario.SignedInAdminAsync(factory);
        var detail = await admin.GetAsync<JsonElement>($"/api/admin/orders/{orderId}");
        Assert.True(detail.GetProperty("hasCustomization").GetBoolean());
        Assert.Contains("Cancelled", detail.GetProperty("nextStatuses").EnumerateArray().Select(s => s.GetString()));

        var response = await admin.Http.GetAsync($"/api/admin/orders/{orderId}/artwork");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync());
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("README.txt", names);
        Assert.Contains("order.json", names);
        Assert.Contains(names, n => n.EndsWith("/design.json", StringComparison.Ordinal));
        Assert.Contains(names, n => n.EndsWith("/production-spec.json", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains($"/artwork/{uploadId}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dashboard_reports_sales_and_pipeline_counts()
    {
        var (customer, _, _) = await Scenario.SignedInCustomerAsync(factory);
        var mug = await Scenario.ProductAsync(customer, "white-ceramic-mug");
        await customer.PostAsync<JsonElement>("/api/cart/items", new { productId = mug.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(mug, "11 oz"), quantity = 1 });
        var order = await customer.PostAsync<JsonElement>("/api/orders", Scenario.CheckoutRequest(), HttpStatusCode.Created);
        var payment = await customer.PostAsync<JsonElement>("/api/payments", new { orderId = order.GetProperty("id").GetGuid() }, HttpStatusCode.Created);
        await customer.PostAsync<JsonElement>($"/api/payments/{payment.GetProperty("id").GetGuid()}/confirm", new { paymentToken = "tok_test_success" });

        var admin = await Scenario.SignedInAdminAsync(factory);
        var dashboard = await admin.GetAsync<JsonElement>("/api/admin/dashboard");

        Assert.True(dashboard.GetProperty("revenueLast30Days").GetDecimal() >= order.GetProperty("total").GetDecimal());
        Assert.True(dashboard.GetProperty("awaitingProduction").GetInt32() >= 1);
        Assert.Equal(30, dashboard.GetProperty("sales").GetArrayLength());
        Assert.True(dashboard.GetProperty("customers").GetInt32() >= 1);
        Assert.Contains(dashboard.GetProperty("topProducts").EnumerateArray(), p => p.GetProperty("productName").GetString() == "White Ceramic Mug");
    }

    [Fact]
    public async Task Locked_customers_cannot_sign_in_and_admins_cannot_lock_themselves()
    {
        var (_, email, userId) = await Scenario.SignedInCustomerAsync(factory);
        var admin = await Scenario.SignedInAdminAsync(factory);

        var locked = await admin.PutAsync<JsonElement>($"/api/admin/customers/{userId}/lock", new { locked = true });
        Assert.True(locked.GetProperty("isLockedOut").GetBoolean());

        var client = await factory.CreateApiClientAsync();
        var login = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email, password = Scenario.Password });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        await admin.PutAsync<JsonElement>($"/api/admin/customers/{userId}/lock", new { locked = false });
        await client.LoginAsync(email, Scenario.Password);

        var me = await admin.GetAsync<JsonElement>("/api/auth/me");
        var selfLock = await admin.SendAsync(HttpMethod.Put, $"/api/admin/customers/{me.GetProperty("id").GetGuid()}/lock", new { locked = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, selfLock.StatusCode);

        var list = await admin.GetAsync<JsonElement>($"/api/admin/customers?search={Uri.EscapeDataString(email)}");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }
}
