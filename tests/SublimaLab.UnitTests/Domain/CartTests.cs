using SublimaLab.Domain.Carts;
using SublimaLab.Domain.Common;
using SublimaLab.Tests.Support;

namespace SublimaLab.UnitTests.Domain;

public class CartTests
{
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid VariantId = Guid.NewGuid();

    [Fact]
    public void Plain_items_of_the_same_variant_are_merged()
    {
        var cart = Cart.ForSession("session");

        cart.AddItem(ProductId, VariantId, 2, null, null, null);
        cart.AddItem(ProductId, VariantId, 3, null, null, null);

        var item = Assert.Single(cart.Items);
        Assert.Equal(5, item.Quantity);
    }

    [Fact]
    public void Customized_items_are_always_separate_lines()
    {
        var cart = Cart.ForSession("session");
        var design = TestData.Design(("front", [TestData.Text()]));

        cart.AddItem(ProductId, VariantId, 1, design, null, null);
        cart.AddItem(ProductId, VariantId, 1, design, null, null);

        Assert.Equal(2, cart.Items.Count);
    }

    [Fact]
    public void Merged_quantity_is_capped_at_the_line_maximum()
    {
        var cart = Cart.ForSession("session");

        cart.AddItem(ProductId, VariantId, 80, null, null, null);
        cart.AddItem(ProductId, VariantId, 80, null, null, null);

        Assert.Equal(Cart.MaxQuantityPerLine, Assert.Single(cart.Items).Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Invalid_quantities_are_rejected(int quantity)
    {
        var cart = Cart.ForSession("session");

        var error = Assert.Throws<DomainException>(() => cart.AddItem(ProductId, VariantId, quantity, null, null, null));
        Assert.Equal("INVALID_QUANTITY", error.Code);
    }

    [Fact]
    public void Cart_rejects_more_than_the_maximum_number_of_lines()
    {
        var cart = Cart.ForSession("session");
        for (var i = 0; i < Cart.MaxLines; i++)
        {
            cart.AddItem(Guid.NewGuid(), Guid.NewGuid(), 1, null, null, null);
        }

        var error = Assert.Throws<DomainException>(() => cart.AddItem(Guid.NewGuid(), Guid.NewGuid(), 1, null, null, null));
        Assert.Equal("CART_FULL", error.Code);
    }

    [Fact]
    public void Editing_the_design_of_a_plain_item_is_rejected()
    {
        var cart = Cart.ForSession("session");
        var item = cart.AddItem(ProductId, VariantId, 1, null, null, null);

        var error = Assert.Throws<DomainException>(() =>
            cart.UpdateCustomization(item.Id, VariantId, TestData.Design(("front", [TestData.Text()])), null));
        Assert.Equal("CART_ITEM_NOT_CUSTOMIZED", error.Code);
    }

    [Fact]
    public void Merging_a_guest_cart_moves_lines_and_discount_then_empties_the_guest_cart()
    {
        var userCart = Cart.ForUser(Guid.NewGuid());
        userCart.AddItem(ProductId, VariantId, 1, null, null, null);

        var guestCart = Cart.ForSession("guest");
        guestCart.AddItem(ProductId, VariantId, 2, null, null, null);
        guestCart.AddItem(ProductId, VariantId, 1, TestData.Design(("front", [TestData.Text()])), null, null);
        guestCart.ApplyDiscountCode(" welcome10 ");

        userCart.MergeFrom(guestCart);

        Assert.Equal(2, userCart.Items.Count);
        Assert.Equal(3, userCart.Items.Single(i => i.Design is null).Quantity);
        Assert.Equal("WELCOME10", userCart.DiscountCode);
        Assert.Empty(guestCart.Items);
    }
}
