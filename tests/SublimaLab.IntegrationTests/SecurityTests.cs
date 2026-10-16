using System.Net;
using System.Text;
using System.Text.Json;
using SublimaLab.IntegrationTests.Support;
using SublimaLab.Tests.Support;

namespace SublimaLab.IntegrationTests;

public sealed class SecurityTests(SublimaLabFactory factory) : IClassFixture<SublimaLabFactory>
{
    [Fact]
    public async Task Unsafe_requests_without_the_antiforgery_token_are_rejected()
    {
        var client = await factory.CreateApiClientAsync();
        var mug = await Scenario.ProductAsync(client, "white-ceramic-mug");

        var response = await client.SendAsync(HttpMethod.Post, "/api/cart/items",
            new { productId = mug.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(mug), quantity = 1 }, withAntiforgery: false);
        var error = await ApiClient.ReadAsync<JsonElement>(response, HttpStatusCode.BadRequest);

        Assert.Equal("CSRF_TOKEN_INVALID", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Antiforgery_tokens_from_another_session_are_rejected()
    {
        var victim = await factory.CreateApiClientAsync();
        var attacker = await factory.CreateApiClientAsync();
        var token = (await attacker.GetAsync<JsonElement>("/api/antiforgery/token")).GetProperty("token").GetString()!;
        victim.UseAntiforgeryToken(token);

        var response = await victim.SendAsync(HttpMethod.Delete, "/api/cart/discount");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/orders")]
    [InlineData("/api/designs")]
    [InlineData("/api/account/profile")]
    [InlineData("/api/auth/me")]
    public async Task Protected_endpoints_require_authentication(string url)
    {
        var client = await factory.CreateApiClientAsync();

        var response = await client.Http.GetAsync(url);
        var error = await ApiClient.ReadAsync<JsonElement>(response, HttpStatusCode.Unauthorized);

        Assert.Equal("AUTHENTICATION_REQUIRED", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Session_endpoint_reports_the_signed_in_user_without_erroring_for_guests()
    {
        var guest = await factory.CreateApiClientAsync();
        var (customer, email, _) = await Scenario.SignedInCustomerAsync(factory);

        var guestSession = await guest.GetAsync<JsonElement>("/api/auth/session");
        var customerSession = await customer.GetAsync<JsonElement>("/api/auth/session");

        Assert.Equal(JsonValueKind.Null, guestSession.GetProperty("user").ValueKind);
        Assert.Equal(email, customerSession.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Storefront_routes_serve_the_app_while_unknown_api_routes_stay_json()
    {
        var client = await factory.CreateApiClientAsync();

        var page = await client.Http.GetAsync("/products/classic-white-t-shirt");
        var api = await client.Http.GetAsync("/api/nothing-here");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("'wasm-unsafe-eval'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Equal("application/json", api.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Customers_cannot_use_admin_endpoints()
    {
        var (customer, _, _) = await Scenario.SignedInCustomerAsync(factory);

        var response = await customer.Http.GetAsync("/api/admin/orders");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customers_cannot_see_or_pay_other_customers_orders()
    {
        var (owner, _, _) = await Scenario.SignedInCustomerAsync(factory);
        var mug = await Scenario.ProductAsync(owner, "white-ceramic-mug");
        await owner.PostAsync<JsonElement>("/api/cart/items", new { productId = mug.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(mug), quantity = 1 });
        var order = await owner.PostAsync<JsonElement>("/api/orders", Scenario.CheckoutRequest(), HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();

        var (other, _, _) = await Scenario.SignedInCustomerAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync($"/api/orders/{orderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(HttpMethod.Post, "/api/payments", new { orderId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/cancel")).StatusCode);
    }

    [Fact]
    public async Task Uploaded_artwork_is_private_to_its_owner()
    {
        var owner = await factory.CreateApiClientAsync();
        var uploadId = await Scenario.UploadArtworkAsync(owner, 300, 300);
        var stranger = await factory.CreateApiClientAsync();

        var ownResponse = await owner.Http.GetAsync($"/api/uploads/{uploadId}/content");
        var strangerResponse = await stranger.Http.GetAsync($"/api/uploads/{uploadId}/content");

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Contains("sandbox", ownResponse.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", ownResponse.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
    }

    [Fact]
    public async Task Files_disguised_as_images_are_rejected()
    {
        var client = await factory.CreateApiClientAsync();

        var html = await client.UploadAsync(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "cute-cat.png", "image/png", expected: HttpStatusCode.BadRequest);
        var svg = await client.UploadAsync(Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"), "logo.svg", "image/svg+xml", expected: HttpStatusCode.BadRequest);
        var mismatch = await client.UploadAsync(TestImages.Jpeg(400, 400), "photo.png", "image/png", expected: HttpStatusCode.BadRequest);

        Assert.All([html, svg, mismatch], e => Assert.Equal("VALIDATION_ERROR", e.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task Client_supplied_prices_are_ignored()
    {
        var client = await factory.CreateApiClientAsync();
        var mug = await Scenario.ProductAsync(client, "white-ceramic-mug");

        var cart = await client.PostAsync<JsonElement>("/api/cart/items", new
        {
            productId = mug.GetProperty("id").GetGuid(),
            variantId = Scenario.VariantId(mug, "11 oz"),
            quantity = 1,
            unitPrice = 0.01m,
            price = 0.01m,
            total = 0m,
            discount = 100m,
        });

        Assert.Equal(12.99m, cart.GetProperty("totals").GetProperty("subtotal").GetDecimal());
        Assert.Equal(0m, cart.GetProperty("totals").GetProperty("discount").GetDecimal());
    }

    [Fact]
    public async Task Variants_from_another_product_are_rejected()
    {
        var client = await factory.CreateApiClientAsync();
        var mug = await Scenario.ProductAsync(client, "white-ceramic-mug");
        var keychain = await Scenario.ProductAsync(client, "acrylic-keychain");

        var response = await client.SendAsync(HttpMethod.Post, "/api/cart/items",
            new { productId = keychain.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(mug), quantity = 1 });
        var error = await ApiClient.ReadAsync<JsonElement>(response, HttpStatusCode.BadRequest);

        Assert.True(error.GetProperty("errors").TryGetProperty("variantId", out _));
    }

    [Fact]
    public async Task Designs_outside_the_printable_area_are_rejected_with_the_element_path()
    {
        var client = await factory.CreateApiClientAsync();
        var tee = await Scenario.ProductAsync(client, "classic-white-t-shirt");
        var uploadId = await Scenario.UploadArtworkAsync(client, 3000, 3000);

        var response = await client.SendAsync(HttpMethod.Post, "/api/cart/items",
            new { productId = tee.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(tee), quantity = 1, design = Scenario.TeeDesign(uploadId, imageX: 280) });
        var error = await ApiClient.ReadAsync<JsonElement>(response, HttpStatusCode.BadRequest);

        Assert.True(error.GetProperty("errors").TryGetProperty("design.surfaces[0].elements[0]", out _));
    }

    [Fact]
    public async Task Designs_cannot_reference_another_visitors_upload()
    {
        var owner = await factory.CreateApiClientAsync();
        var uploadId = await Scenario.UploadArtworkAsync(owner);
        var thief = await factory.CreateApiClientAsync();
        var tee = await Scenario.ProductAsync(thief, "classic-white-t-shirt");

        var result = await thief.PostAsync<JsonElement>("/api/designs/validate", new { productId = tee.GetProperty("id").GetGuid(), design = Scenario.TeeDesign(uploadId) });

        Assert.False(result.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task Malformed_design_json_returns_a_validation_error_without_internal_details()
    {
        var client = await factory.CreateApiClientAsync();
        var content = new StringContent("""{"productId":"00000000-0000-0000-0000-000000000001","design":{"surfaces":[{"printArea":"front","elements":[{"type":"script","id":"x"}]}]}}""", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(HttpMethod.Post, "/api/designs/validate", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("SublimaLab.Domain", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_passwords_and_unconfirmed_accounts_cannot_sign_in()
    {
        var client = await factory.CreateApiClientAsync();
        var email = Scenario.NewEmail();
        await client.PostAsync<JsonElement>("/api/auth/register", new { email, password = Scenario.Password, firstName = "Alan", lastName = "Turing" });

        var unconfirmed = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email, password = Scenario.Password });
        var unknown = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email = "nobody@example.com", password = Scenario.Password });

        Assert.Equal("EMAIL_NOT_CONFIRMED", (await ApiClient.ReadAsync<JsonElement>(unconfirmed, HttpStatusCode.Forbidden)).GetProperty("code").GetString());
        Assert.Equal("INVALID_CREDENTIALS", (await ApiClient.ReadAsync<JsonElement>(unknown, HttpStatusCode.Unauthorized)).GetProperty("code").GetString());

        await Scenario.ConfirmEmailAsync(factory, client, email);
        var wrongPassword = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email, password = "Wrong-Password-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
    }

    [Fact]
    public async Task Registration_does_not_reveal_whether_an_account_exists()
    {
        var (_, email, _) = await Scenario.SignedInCustomerAsync(factory);
        var client = await factory.CreateApiClientAsync();

        var duplicate = await client.PostAsync<JsonElement>("/api/auth/register", new { email, password = Scenario.Password, firstName = "X", lastName = "Y" });
        var fresh = await client.PostAsync<JsonElement>("/api/auth/register", new { email = Scenario.NewEmail(), password = Scenario.Password, firstName = "X", lastName = "Y" });

        Assert.Equal(fresh.GetProperty("message").GetString(), duplicate.GetProperty("message").GetString());
        Assert.Contains("already have", factory.Emails.Last(email).Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Weak_passwords_are_rejected()
    {
        var client = await factory.CreateApiClientAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/auth/register", new { email = Scenario.NewEmail(), password = "short", firstName = "A", lastName = "B" });
        var error = await ApiClient.ReadAsync<JsonElement>(response, HttpStatusCode.BadRequest);

        Assert.True(error.GetProperty("errors").TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Password_reset_flow_issues_a_working_token_and_invalidates_the_old_password()
    {
        var (_, email, _) = await Scenario.SignedInCustomerAsync(factory);
        var client = await factory.CreateApiClientAsync();

        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(HttpMethod.Post, "/api/auth/forgot-password", new { email })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(HttpMethod.Post, "/api/auth/forgot-password", new { email = "ghost@example.com" })).StatusCode);

        var link = CapturingEmailSender.LinkParameters(factory.Emails.Last(email));
        var reset = await client.SendAsync(HttpMethod.Post, "/api/auth/reset-password", new { email, token = link["token"], newPassword = "Brand-New-Pass-7" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var oldPassword = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email, password = Scenario.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        await client.LoginAsync(email, "Brand-New-Pass-7");
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_unknown_routes_use_the_error_format()
    {
        var client = await factory.CreateApiClientAsync();

        var response = await client.Http.GetAsync("/api/definitely-not-here");
        var error = await ApiClient.ReadAsync<JsonElement>(response, HttpStatusCode.NotFound);

        Assert.Equal("NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Authentication_and_session_cookies_are_hardened()
    {
        var client = await factory.CreateApiClientAsync();
        var email = Scenario.NewEmail();
        await client.PostAsync<JsonElement>("/api/auth/register", new { email, password = Scenario.Password, firstName = "A", lastName = "B" });
        await Scenario.ConfirmEmailAsync(factory, client, email);

        var login = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email, password = Scenario.Password });
        var authCookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-sl.auth=", StringComparison.Ordinal));

        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);
    }
}
