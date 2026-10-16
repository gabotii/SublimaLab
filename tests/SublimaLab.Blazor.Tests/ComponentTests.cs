using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SublimaLab.Blazor.Components;
using SublimaLab.Blazor.Models;
using SublimaLab.Blazor.Pages;
using SublimaLab.Blazor.Pages.Account;
using SublimaLab.Blazor.Services;
using SublimaLab.Blazor.Tests.Support;

namespace SublimaLab.Blazor.Tests;

public sealed class ComponentTests : BunitContext
{
    private readonly StubApi _api = new();

    public ComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Register(Services);
    }

    [Fact]
    public void Quantity_stepper_raises_changes_and_respects_bounds()
    {
        var value = 1;
        var cut = Render<QuantityStepper>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.Max, 2)
            .Add(c => c.ValueChanged, (int v) => value = v));

        Assert.True(cut.Find("button[aria-label='Decrease quantity']").HasAttribute("disabled"));
        cut.Find("button[aria-label='Increase quantity']").Click();
        Assert.Equal(2, value);
    }

    [Fact]
    public void Product_card_shows_price_customize_link_and_color_count()
    {
        var cut = Render<ProductCard>(p => p.Add(c => c.Product, TestModels.Product(colors: 3)));

        Assert.Contains("Classic White T-Shirt", cut.Find("h3").TextContent, StringComparison.Ordinal);
        Assert.Contains("From", cut.Find(".product-card__price").TextContent, StringComparison.Ordinal);
        Assert.Contains("$19.99", cut.Find(".product-card__price").TextContent, StringComparison.Ordinal);
        Assert.Equal("customize/classic-white-t-shirt", cut.Find("a.btn--primary").GetAttribute("href"));
        Assert.Equal(3, cut.FindAll(".swatches li").Count);
    }

    [Fact]
    public void Non_customizable_products_have_no_customize_button()
    {
        var cut = Render<ProductCard>(p => p.Add(c => c.Product, TestModels.Product(customizable: false)));

        Assert.Empty(cut.FindAll("a.btn--primary"));
        Assert.DoesNotContain("Customizable", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Product_art_only_accepts_hex_colors_in_the_style_attribute()
    {
        var safe = Render<ProductArt>(p => p.Add(c => c.Color, "#A9D4F5"));
        var hostile = Render<ProductArt>(p => p.Add(c => c.Color, "red;background:url(https://evil)"));

        Assert.Contains("color:#A9D4F5", safe.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("evil", hostile.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Cart_page_lists_server_priced_lines_and_totals()
    {
        _api.Json("GET api/cart", TestModels.CartWith(TestModels.Line(quantity: 2, customization: 6m), TestModels.Line("White Ceramic Mug", 1, 12.99m)));

        var cut = Render<CartPage>();
        cut.WaitForElements(".cart-line");

        Assert.Equal(2, cut.FindAll(".cart-line").Count);
        Assert.Contains("Your design", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("$64.97", cut.Find(".summary__rows").TextContent, StringComparison.Ordinal); // 2 x (19.99 + 6.00) + 12.99
        Assert.Contains("$72.04", cut.Find(".summary__total").TextContent, StringComparison.Ordinal); // + 4.99 shipping + 2.08 tax
    }

    [Fact]
    public void Empty_cart_shows_an_empty_state_with_shop_links()
    {
        var cut = Render<CartPage>();
        cut.WaitForElement(".empty-state");

        Assert.Contains("Your cart is empty", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"shop\"", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("//evil.example/steal", "http://localhost/")]
    [InlineData("https://evil.example", "http://localhost/")]
    [InlineData("/\\evil.example", "http://localhost/")]
    [InlineData("/checkout", "http://localhost/checkout")]
    public void Login_only_redirects_to_local_paths(string returnUrl, string expected)
    {
        _api.Json("POST api/auth/login", new AuthResponse(
            new User(Guid.NewGuid(), "ada@example.com", "Ada", "Lovelace", null, true, ["Customer"]),
            new AntiforgeryToken("user-token", "X-XSRF-TOKEN")));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"account/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        var cut = Render<Login>();
        cut.Find("#login-email").Change("ada@example.com");
        cut.Find("#login-password").Change("Correct-Horse-1");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(expected, navigation.Uri));
    }

    [Fact]
    public void Login_errors_are_shown_with_a_resend_option_for_unconfirmed_accounts()
    {
        _api.Error("POST api/auth/login", System.Net.HttpStatusCode.Forbidden, "EMAIL_NOT_CONFIRMED", "Please confirm your email address before signing in.");

        var cut = Render<Login>();
        cut.Find("#login-email").Change("ada@example.com");
        cut.Find("#login-password").Change("whatever");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Please confirm your email", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Resend confirmation email", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_asks_for_an_authenticator_code_when_two_step_verification_is_on()
    {
        _api.Json("POST api/auth/login", new AuthResponse(null, new AntiforgeryToken("pending-token", "X-XSRF-TOKEN"), RequiresTwoFactor: true));
        _api.Json("POST api/auth/login/2fa", new ApiError("VALIDATION_ERROR", "One or more fields are invalid.",
            new Dictionary<string, string[]> { ["code"] = ["That code isn't valid."] }, "trace"), System.Net.HttpStatusCode.BadRequest);
        var navigation = Services.GetRequiredService<NavigationManager>();

        var cut = Render<Login>();
        cut.Find("#login-email").Change("admin@example.com");
        cut.Find("#login-password").Change("Correct-Horse-1");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Two-step verification", cut.Markup, StringComparison.Ordinal));
        var code = cut.Find("#login-code");
        Assert.Equal("one-time-code", code.GetAttribute("autocomplete"));

        // A wrong code is explained next to the field, without the generic summary.
        code.Input("000000");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("That code isn't valid.", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("One or more fields are invalid.", cut.Markup, StringComparison.Ordinal);

        _api.Json("POST api/auth/login/2fa", new AuthResponse(
            new User(Guid.NewGuid(), "admin@example.com", "Grace", "Hopper", null, true, ["Admin"], TwoFactorEnabled: true),
            new AntiforgeryToken("user-token", "X-XSRF-TOKEN")));
        cut.Find("#login-code").Input("123 456");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/", navigation.Uri));
        Assert.Contains(_api.Requests, r => r.StartsWith("POST api/auth/login/2fa", StringComparison.Ordinal));
    }

    [Fact]
    public void Recovery_codes_can_be_used_instead_of_the_app()
    {
        _api.Json("POST api/auth/login", new AuthResponse(null, new AntiforgeryToken("pending-token", "X-XSRF-TOKEN"), RequiresTwoFactor: true));

        var cut = Render<Login>();
        cut.Find("#login-email").Change("admin@example.com");
        cut.Find("#login-password").Change("Correct-Horse-1");
        cut.Find("form").Submit();
        cut.WaitForElement("#login-code");

        cut.FindAll("button").Single(b => b.TextContent.Contains("Use a recovery code", StringComparison.Ordinal)).Click();

        Assert.Contains("Recovery code", cut.Find("label[for=login-code]").TextContent, StringComparison.Ordinal);
        Assert.Equal("text", cut.Find("#login-code").GetAttribute("inputmode"));
    }

    [Fact]
    public void Admin_area_asks_administrators_to_set_up_two_step_verification_first()
    {
        Services.GetRequiredService<AuthService>().SetUser(
            new User(Guid.NewGuid(), "admin@example.com", "Grace", "Hopper", null, true, ["Admin"], MustEnableTwoFactor: true));

        var cut = Render<SublimaLab.Blazor.Layout.AdminLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "admin-secret"))));

        Assert.Contains("Turn on two-step verification", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("admin-secret", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Legal_pages_render_with_the_template_notice()
    {
        Assert.Contains("Terms of service", Render<SublimaLab.Blazor.Pages.Legal.Terms>().Markup, StringComparison.Ordinal);
        Assert.Contains("__Host-sl.auth", Render<SublimaLab.Blazor.Pages.Legal.Privacy>().Markup, StringComparison.Ordinal);

        var contact = Render<SublimaLab.Blazor.Pages.Legal.Contact>();
        Assert.Contains("mailto:" + StoreInfo.SupportEmail, contact.Markup, StringComparison.Ordinal);
        Assert.Contains("Template text", contact.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Product_structured_data_is_valid_json_and_cannot_break_out_of_its_script_tag()
    {
        var product = new ProductDetail(Guid.NewGuid(), "Mug </script><script>alert(1)</script>", "classic-mug", "Ceramic mug", "Long text", "mugs", "Mugs",
            12.99m, "USD", true, 1, [new ProductImage("/images/products/classic-mug.svg", "Mug")],
            [new Variant(Guid.NewGuid(), "MUG-W", "White", "#FFFFFF", null, 12.99m), new Variant(Guid.NewGuid(), "MUG-B", "Black", "#000000", null, 14.99m)],
            [], null);

        var cut = Render<ProductJsonLd>(p => p.Add(c => c.Product, product));
        var json = cut.Instance.BuildJson();

        Assert.DoesNotContain("</script>", json, StringComparison.OrdinalIgnoreCase);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var offers = doc.RootElement.GetProperty("offers");
        Assert.Equal("Product", doc.RootElement.GetProperty("@type").GetString());
        Assert.Equal(12.99m, offers.GetProperty("lowPrice").GetDecimal());
        Assert.Equal(14.99m, offers.GetProperty("highPrice").GetDecimal());
        Assert.Equal("http://localhost/images/products/classic-mug.svg", doc.RootElement.GetProperty("image")[0].GetString());
    }

    [Fact]
    public void Shipping_page_lists_the_live_shipping_methods()
    {
        var cut = Render<SublimaLab.Blazor.Pages.Legal.ShippingReturns>();

        cut.WaitForAssertion(() => Assert.Contains("Standard Shipping", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("3–6 business days", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Free over", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_client_retries_once_with_a_fresh_token_when_csrf_validation_fails()
    {
        var attempts = 0;
        var api = new StubApi();
        api.Json("GET api/antiforgery/token", new AntiforgeryToken("fresh", "X-XSRF-TOKEN"));
        api.Error("POST api/cart/discount", System.Net.HttpStatusCode.BadRequest, "CSRF_TOKEN_INVALID", "expired");
        var client = new ApiClient(new HttpClient(new CountingHandler(api, () => attempts++)) { BaseAddress = new Uri("http://localhost/") });

        var error = await Assert.ThrowsAsync<ApiException>(() => client.PostAsync<Cart>("api/cart/discount", new ApplyDiscountRequest("X")));

        Assert.Equal("CSRF_TOKEN_INVALID", error.Code);
        Assert.Equal(2, api.Requests.Count(r => r.StartsWith("POST api/cart/discount", StringComparison.Ordinal)));
        Assert.Equal(2, api.Requests.Count(r => r.StartsWith("GET api/antiforgery/token", StringComparison.Ordinal)));
        Assert.Equal(4, attempts);
    }

    private sealed class CountingHandler(HttpMessageHandler inner, Action onSend) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend();
            return base.SendAsync(request, cancellationToken);
        }
    }
}

public class FormattingTests
{
    [Theory]
    [InlineData(1234.5, "USD", "$1,234.50")]
    [InlineData(0, "USD", "$0.00")]
    [InlineData(9.99, "EUR", "€9.99")]
    public void Money_is_formatted_for_display(decimal amount, string currency, string expected) =>
        Assert.Equal(expected, Format.Money(amount, currency));

    [Theory]
    [InlineData("classic-white-t-shirt", ArtKind.TShirt)]
    [InlineData("magic-color-changing-mug", ArtKind.Mug)]
    [InlineData("round-keychain", ArtKind.KeychainRound)]
    [InlineData("square-keychain", ArtKind.KeychainSquare)]
    [InlineData("acrylic-keychain", ArtKind.KeychainRect)]
    public void Product_slugs_map_to_illustrations(string slug, string expected) =>
        Assert.Equal(expected, ArtKind.FromProductSlug(slug));

    [Theory]
    [InlineData("KEY-RK-WHI", ArtKind.KeychainRound)]
    [InlineData("MUG-WC-WHI-11OZ", ArtKind.Mug)]
    [InlineData("TSH-CC-SKY-L", ArtKind.TShirt)]
    public void Skus_map_to_illustrations(string sku, string expected) =>
        Assert.Equal(expected, ArtKind.FromSku(sku));

    [Theory]
    [InlineData(OrderStatus.Delivered, "success")]
    [InlineData(OrderStatus.Cancelled, "danger")]
    [InlineData(OrderStatus.PaymentPending, "warning")]
    [InlineData(OrderStatus.InProduction, "info")]
    public void Order_statuses_have_consistent_tones(OrderStatus status, string tone) =>
        Assert.Equal(tone, Format.StatusTone(status));
}
