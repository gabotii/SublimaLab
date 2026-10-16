using System.Net;
using System.Text.Json;
using SublimaLab.IntegrationTests.Support;

namespace SublimaLab.IntegrationTests;

public sealed class CatalogTests(SublimaLabFactory factory) : IClassFixture<SublimaLabFactory>
{
    [Fact]
    public async Task Seed_data_makes_the_store_usable_immediately()
    {
        var client = await factory.CreateApiClientAsync();

        var categories = await client.GetAsync<JsonElement>("/api/categories");
        var products = await client.GetAsync<JsonElement>("/api/products?pageSize=48");
        var shipping = await client.GetAsync<JsonElement>("/api/shipping/methods");

        Assert.Equal(["t-shirts", "mugs", "keychains"], categories.EnumerateArray().Select(c => c.GetProperty("slug").GetString()));
        Assert.Equal(10, products.GetProperty("totalCount").GetInt32());
        Assert.Equal(["standard", "express"], shipping.EnumerateArray().Select(m => m.GetProperty("code").GetString()));
    }

    [Theory]
    [InlineData("category=mugs", 2)]
    [InlineData("category=keychains", 3)]
    [InlineData("search=KEYCHAIN", 3)]
    [InlineData("search=premium", 2)]
    [InlineData("minPrice=10&maxPrice=20", 4)]
    [InlineData("featured=true", 6)]
    public async Task Products_can_be_searched_and_filtered(string query, int expected)
    {
        var client = await factory.CreateApiClientAsync();

        var result = await client.GetAsync<JsonElement>($"/api/products?{query}");

        Assert.Equal(expected, result.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Products_can_be_sorted_and_paginated()
    {
        var client = await factory.CreateApiClientAsync();

        var page = await client.GetAsync<JsonElement>("/api/products?sort=PriceDesc&pageSize=4&page=1");
        var last = await client.GetAsync<JsonElement>("/api/products?sort=PriceDesc&pageSize=4&page=3");

        var prices = page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("price").GetDecimal()).ToList();
        Assert.Equal(prices.OrderByDescending(p => p), prices);
        Assert.Equal(29.99m, prices[0]);
        Assert.Equal(3, page.GetProperty("totalPages").GetInt32());
        Assert.Equal(2, last.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Product_details_include_variants_and_customization_rules()
    {
        var client = await factory.CreateApiClientAsync();

        var product = await client.GetAsync<JsonElement>("/api/products/colors-collection-t-shirt");

        Assert.Equal(36, product.GetProperty("variants").GetArrayLength()); // 6 colours x 6 sizes
        var customization = product.GetProperty("customization");
        Assert.Equal("Apparel", customization.GetProperty("kind").GetString());
        Assert.Equal(["front", "back"], customization.GetProperty("printAreas").EnumerateArray().Select(a => a.GetProperty("key").GetString()));
        Assert.Contains("Inter", customization.GetProperty("fonts").EnumerateArray().Select(f => f.GetString()));
        Assert.NotEmpty(product.GetProperty("specifications").EnumerateArray());
    }

    [Fact]
    public async Task Round_keychain_exposes_an_elliptical_print_area()
    {
        var client = await factory.CreateApiClientAsync();

        var product = await client.GetAsync<JsonElement>("/api/products/round-keychain");

        Assert.Equal("Ellipse", product.GetProperty("customization").GetProperty("printAreas")[0].GetProperty("shape").GetString());
    }

    [Fact]
    public async Task Unknown_products_return_a_structured_404()
    {
        var client = await factory.CreateApiClientAsync();

        var response = await client.Http.GetAsync("/api/products/does-not-exist");
        var error = await ApiClient.ReadAsync<JsonElement>(response, HttpStatusCode.NotFound);

        Assert.Equal("NOT_FOUND", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Invalid_queries_are_rejected_with_field_errors()
    {
        var client = await factory.CreateApiClientAsync();

        var response = await client.Http.GetAsync("/api/products?pageSize=500&minPrice=-1");
        var error = await ApiClient.ReadAsync<JsonElement>(response, HttpStatusCode.BadRequest);

        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("errors").TryGetProperty("pageSize", out _));
        Assert.True(error.GetProperty("errors").TryGetProperty("minPrice", out _));
    }
}
