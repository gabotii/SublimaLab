using System.Net;
using System.Text.Json;
using SublimaLab.IntegrationTests.Support;

namespace SublimaLab.IntegrationTests;

public sealed class CartAndCheckoutTests(SublimaLabFactory factory) : IClassFixture<SublimaLabFactory>
{
    [Fact]
    public async Task Guest_cart_and_artwork_are_kept_when_the_guest_signs_in()
    {
        var client = await factory.CreateApiClientAsync();
        var email = Scenario.NewEmail();
        await client.PostAsync<JsonElement>("/api/auth/register", new { email, password = Scenario.Password, firstName = "Grace", lastName = "Hopper" });
        await Scenario.ConfirmEmailAsync(factory, client, email);

        // As a guest: upload artwork and add a customized tee
        var tee = await Scenario.ProductAsync(client, "classic-white-t-shirt");
        var uploadId = await Scenario.UploadArtworkAsync(client);
        await client.PostAsync<JsonElement>("/api/cart/items",
            new { productId = tee.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(tee, "L"), quantity = 2, design = Scenario.TeeDesign(uploadId) });

        await client.LoginAsync(email, Scenario.Password);

        var cart = await client.GetAsync<JsonElement>("/api/cart");
        var line = Assert.Single(cart.GetProperty("items").EnumerateArray());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.True(line.GetProperty("isAvailable").GetBoolean());

        // The upload now belongs to the account, so checkout validation accepts it
        var order = await client.PostAsync<JsonElement>("/api/orders", Scenario.CheckoutRequest(), HttpStatusCode.Created);
        Assert.Equal(51.98m, order.GetProperty("subtotal").GetDecimal()); // 2 x (19.99 + 6.00)
        Assert.Equal(0m, order.GetProperty("shipping").GetDecimal());     // free over $50
    }

    [Fact]
    public async Task Checkout_requires_an_account()
    {
        var guest = await factory.CreateApiClientAsync();
        var mug = await Scenario.ProductAsync(guest, "white-ceramic-mug");
        await guest.PostAsync<JsonElement>("/api/cart/items", new { productId = mug.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(mug), quantity = 1 });

        var response = await guest.SendAsync(HttpMethod.Post, "/api/orders", Scenario.CheckoutRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Placing_the_same_checkout_twice_returns_the_same_order()
    {
        var (client, _, _) = await Scenario.SignedInCustomerAsync(factory);
        await AddMugAsync(client);
        var key = Guid.NewGuid();

        var first = await client.PostAsync<JsonElement>("/api/orders", Scenario.CheckoutRequest(key), HttpStatusCode.Created);
        var second = await client.PostAsync<JsonElement>("/api/orders", Scenario.CheckoutRequest(key), HttpStatusCode.Created);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        var history = await client.GetAsync<JsonElement>("/api/orders");
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Declined_payments_leave_the_order_payable_and_can_be_retried()
    {
        var (client, _, _) = await Scenario.SignedInCustomerAsync(factory);
        await AddMugAsync(client);
        var order = await client.PostAsync<JsonElement>("/api/orders", Scenario.CheckoutRequest(), HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();

        var declined = await client.PostAsync<JsonElement>("/api/payments", new { orderId }, HttpStatusCode.Created);
        var failure = await client.PostAsync<JsonElement>($"/api/payments/{declined.GetProperty("id").GetGuid()}/confirm", new { paymentToken = "tok_test_declined" });
        Assert.Equal("PaymentPending", failure.GetProperty("orderStatus").GetString());
        Assert.Equal("Your card was declined.", failure.GetProperty("payment").GetProperty("failureReason").GetString());

        var retry = await client.PostAsync<JsonElement>("/api/payments", new { orderId }, HttpStatusCode.Created);
        var success = await client.PostAsync<JsonElement>($"/api/payments/{retry.GetProperty("id").GetGuid()}/confirm", new { paymentToken = "tok_test_success" });
        Assert.Equal("Paid", success.GetProperty("orderStatus").GetString());

        // A processed payment cannot be confirmed again
        var replay = await client.SendAsync(HttpMethod.Post, $"/api/payments/{retry.GetProperty("id").GetGuid()}/confirm", new { paymentToken = "tok_test_success" });
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
    }

    [Fact]
    public async Task Discount_codes_are_validated_and_applied_server_side()
    {
        var client = await factory.CreateApiClientAsync();
        await AddMugAsync(client, quantity: 2); // 25.98

        var invalid = await client.SendAsync(HttpMethod.Post, "/api/cart/discount", new { code = "FREESTUFF" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        var cart = await client.PostAsync<JsonElement>("/api/cart/discount", new { code = "welcome10" });
        var totals = cart.GetProperty("totals");
        Assert.Equal("WELCOME10", cart.GetProperty("discountCode").GetString());
        Assert.Equal(2.60m, totals.GetProperty("discount").GetDecimal());
        Assert.Equal(1.87m, totals.GetProperty("tax").GetDecimal()); // 8% of (25.98 - 2.60) = 1.8704
    }

    [Fact]
    public async Task Customers_can_edit_quantities_and_remove_items()
    {
        var client = await factory.CreateApiClientAsync();
        var cart = await AddMugAsync(client);
        var itemId = cart.GetProperty("items")[0].GetProperty("id").GetGuid();

        cart = await client.PutAsync<JsonElement>($"/api/cart/items/{itemId}", new { quantity = 4 });
        Assert.Equal(51.96m, cart.GetProperty("totals").GetProperty("subtotal").GetDecimal());

        var tooMany = await client.SendAsync(HttpMethod.Put, $"/api/cart/items/{itemId}", new { quantity = 500 });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        var removed = await ApiClient.ReadAsync<JsonElement>(await client.SendAsync(HttpMethod.Delete, $"/api/cart/items/{itemId}"), HttpStatusCode.OK);
        Assert.Empty(removed.GetProperty("items").EnumerateArray());
    }

    private static async Task<JsonElement> AddMugAsync(ApiClient client, int quantity = 1)
    {
        var mug = await Scenario.ProductAsync(client, "white-ceramic-mug");
        return await client.PostAsync<JsonElement>("/api/cart/items",
            new { productId = mug.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(mug, "11 oz"), quantity });
    }
}
