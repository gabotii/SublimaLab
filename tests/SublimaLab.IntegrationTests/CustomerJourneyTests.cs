using System.Net;
using System.Text.Json;
using SublimaLab.IntegrationTests.Support;

namespace SublimaLab.IntegrationTests;

/// <summary>
/// The primary acceptance flow: register, browse, upload artwork, customize a T-shirt,
/// add to cart, check out, pay with the test gateway, then follow fulfilment as admin.
/// </summary>
public sealed class CustomerJourneyTests(SublimaLabFactory factory) : IClassFixture<SublimaLabFactory>
{
    [Fact]
    public async Task Customer_can_design_order_pay_and_track_a_custom_t_shirt()
    {
        var (customer, _, _) = await Scenario.SignedInCustomerAsync(factory);

        // Browse
        var tshirts = await customer.GetAsync<JsonElement>("/api/products?category=t-shirts");
        Assert.Equal(5, tshirts.GetProperty("totalCount").GetInt32());
        var tee = await Scenario.ProductAsync(customer, "classic-white-t-shirt");
        Assert.True(tee.GetProperty("isCustomizable").GetBoolean());
        var mug = await Scenario.ProductAsync(customer, "white-ceramic-mug");

        // Upload and validate the design
        var uploadId = await Scenario.UploadArtworkAsync(customer);
        var validation = await customer.PostAsync<JsonElement>("/api/designs/validate", new { productId = tee.GetProperty("id").GetGuid(), design = Scenario.TeeDesign(uploadId) });
        Assert.True(validation.GetProperty("isValid").GetBoolean());
        Assert.Empty(validation.GetProperty("warnings").EnumerateArray());

        // Save the design to the account
        var saved = await customer.PostAsync<JsonElement>("/api/designs",
            new { productId = tee.GetProperty("id").GetGuid(), name = "My first tee", design = Scenario.TeeDesign(uploadId) }, HttpStatusCode.Created);
        var designs = await customer.GetAsync<JsonElement>("/api/designs");
        Assert.Contains(designs.EnumerateArray(), d => d.GetProperty("id").GetGuid() == saved.GetProperty("id").GetGuid());

        // Cart: customized tee (size M) + plain mug
        await customer.PostAsync<JsonElement>("/api/cart/items",
            new { productId = tee.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(tee, "M"), quantity = 1, design = Scenario.TeeDesign(uploadId) });
        var cart = await customer.PostAsync<JsonElement>("/api/cart/items",
            new { productId = mug.GetProperty("id").GetGuid(), variantId = Scenario.VariantId(mug, "11 oz"), quantity = 1 });

        var totals = cart.GetProperty("totals");
        Assert.Equal(2, cart.GetProperty("itemCount").GetInt32());
        Assert.Equal(38.98m, totals.GetProperty("subtotal").GetDecimal());   // (19.99 + 6.00 customization) + 12.99
        Assert.Equal(4.99m, totals.GetProperty("shipping").GetDecimal());    // standard, below free threshold
        Assert.Equal(3.12m, totals.GetProperty("tax").GetDecimal());         // 8% of 38.98
        Assert.Equal(47.09m, totals.GetProperty("total").GetDecimal());
        var teeLine = cart.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("isCustomized").GetBoolean());
        Assert.Equal(6.00m, teeLine.GetProperty("customizationPrice").GetDecimal());

        // Checkout
        var order = await customer.PostAsync<JsonElement>("/api/orders", Scenario.CheckoutRequest(), HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();
        Assert.Equal("Pending", order.GetProperty("status").GetString());
        Assert.Equal(47.09m, order.GetProperty("total").GetDecimal());
        Assert.Matches("^SL-\\d{6}-[0-9A-Z]{6}$", order.GetProperty("orderNumber").GetString());

        var emptyCart = await customer.GetAsync<JsonElement>("/api/cart");
        Assert.Empty(emptyCart.GetProperty("items").EnumerateArray());

        // The exact design is preserved on the order
        var orderedTee = order.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("isCustomized").GetBoolean());
        var orderedElements = orderedTee.GetProperty("design").GetProperty("surfaces")[0].GetProperty("elements");
        Assert.Equal(uploadId, orderedElements[0].GetProperty("uploadId").GetGuid());
        Assert.Equal("Made at SublimaLab", orderedElements[1].GetProperty("text").GetString());

        // Pay with the test gateway
        var payment = await customer.PostAsync<JsonElement>("/api/payments", new { orderId }, HttpStatusCode.Created);
        Assert.Equal(47.09m, payment.GetProperty("amount").GetDecimal());
        var outcome = await customer.PostAsync<JsonElement>($"/api/payments/{payment.GetProperty("id").GetGuid()}/confirm", new { paymentToken = "tok_test_success" });
        Assert.Equal("Paid", outcome.GetProperty("orderStatus").GetString());

        // The customer receives an order confirmation with a tracking link
        var confirmation = factory.Emails.Last("ada@example.com");
        Assert.Equal($"Order {order.GetProperty("orderNumber").GetString()} confirmed", confirmation.Subject);
        Assert.Contains($"/account/orders/{orderId}", confirmation.HtmlBody, StringComparison.Ordinal);

        // Order history
        var history = await customer.GetAsync<JsonElement>("/api/orders");
        Assert.Equal(orderId, history.GetProperty("items")[0].GetProperty("id").GetGuid());

        // Saved address from checkout
        var addresses = await customer.GetAsync<JsonElement>("/api/account/addresses");
        Assert.Single(addresses.EnumerateArray());

        // Fulfilment by an administrator
        var admin = await Scenario.SignedInAdminAsync(factory);
        var adminList = await admin.GetAsync<JsonElement>("/api/admin/orders?status=Paid");
        Assert.Contains(adminList.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetGuid() == orderId && o.GetProperty("hasCustomization").GetBoolean());

        await admin.PutAsync<JsonElement>($"/api/admin/orders/{orderId}/status", new { status = "InProduction", note = "Printing now" });
        await admin.PutAsync<JsonElement>($"/api/admin/orders/{orderId}/status", new { status = "ReadyToShip" });
        await admin.PostAsync<JsonElement>($"/api/admin/orders/{orderId}/ship", new { carrier = "UPS", trackingNumber = "1z999aa10123456784" });

        // Admin can view the customer's production artwork
        var artwork = await admin.Http.GetAsync($"/api/uploads/{uploadId}/content");
        Assert.Equal(HttpStatusCode.OK, artwork.StatusCode);
        Assert.Equal("image/png", artwork.Content.Headers.ContentType?.MediaType);

        // Tracking as the customer
        var tracked = await customer.GetAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("Shipped", tracked.GetProperty("status").GetString());
        Assert.Equal("1Z999AA10123456784", tracked.GetProperty("shipment").GetProperty("trackingNumber").GetString());
        Assert.Equal(
            ["Pending", "PaymentPending", "Paid", "InProduction", "ReadyToShip", "Shipped"],
            tracked.GetProperty("timeline").EnumerateArray().Select(t => t.GetProperty("status").GetString()));

        // Retained production artwork can no longer be deleted
        var delete = await customer.SendAsync(HttpMethod.Delete, $"/api/uploads/{uploadId}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }
}
