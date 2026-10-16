using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Customization;
using SublimaLab.Domain.Discounts;
using SublimaLab.Domain.Shipping;

namespace SublimaLab.Infrastructure.Persistence.Seed;

/// <summary>Realistic starter catalog so the store is usable right after installation.</summary>
internal static class CatalogSeed
{
    private static readonly string[] ApparelSizes = ["XS", "S", "M", "L", "XL", "XXL"];

    public static IReadOnlyList<ProductCategory> Build()
    {
        var tshirts = new ProductCategory
        {
            Name = "T-Shirts",
            Slug = "t-shirts",
            Description = "Soft, durable tees printed with vivid, wash-resistant colour. Front, back or both.",
            ImageUrl = "/images/categories/t-shirts.svg",
            SortOrder = 1,
        };
        var mugs = new ProductCategory
        {
            Name = "Mugs",
            Slug = "mugs",
            Description = "Ceramic mugs with full-wrap, dishwasher-safe prints. Perfect gifts for every desk.",
            ImageUrl = "/images/categories/mugs.svg",
            SortOrder = 2,
        };
        var keychains = new ProductCategory
        {
            Name = "Keychains",
            Slug = "keychains",
            Description = "Small, sturdy keychains with your photos, logos or lettering. Printed on both sides.",
            ImageUrl = "/images/categories/keychains.svg",
            SortOrder = 3,
        };

        AddTShirt(tshirts, "Classic White T-Shirt", "classic-white-t-shirt", 19.99m, featured: true,
            "Our everyday crew-neck tee in bright white: the perfect canvas for colourful sublimation prints.",
            "A soft, breathable 100% polyester-feel crew neck that makes colours pop. Sublimation fuses the ink into the fabric, so your print will not crack, peel or fade, even after many washes.",
            "Classic", [("White", "#FFFFFF")]);

        AddTShirt(tshirts, "Classic Black T-Shirt", "classic-black-t-shirt", 19.99m, featured: false,
            "Timeless black tee printed with our white-underbase process for bold designs on dark fabric.",
            "Dark garments cannot take sublimation ink directly, so we print your design on a white underbase transfer that bonds to the fabric. The result: crisp, vibrant artwork on a classic black tee.",
            "Classic", [("Black", "#1C1C1E")]);

        AddTShirt(tshirts, "Premium White T-Shirt", "premium-white-t-shirt", 29.99m, featured: true,
            "Heavyweight, ultra-soft premium tee with a tailored fit and reinforced seams.",
            "Our premium tee uses a denser 190 g/m² performance knit with a silky hand feel, taped shoulders and a modern tailored fit. Ideal for brands, merch drops and gifts that need to impress.",
            "Premium", [("White", "#FFFFFF")]);

        AddTShirt(tshirts, "Premium Black T-Shirt", "premium-black-t-shirt", 29.99m, featured: false,
            "The premium heavyweight tee in deep black, printed with our white-underbase process.",
            "The same premium 190 g/m² knit and tailored fit, in a deep black that makes neon and pastel artwork stand out. Printed with our white-underbase transfer for full-colour designs on dark fabric.",
            "Premium", [("Black", "#1C1C1E")]);

        AddTShirt(tshirts, "Colors Collection T-Shirt", "colors-collection-t-shirt", 22.99m, featured: true,
            "Light, playful colours made for sublimation. Pick your favourite shade.",
            "Sublimation looks best on light fabric, so we curated six soft shades that keep your artwork bright and true to colour. Same comfy classic fit, more personality.",
            "Colors", [("White", "#FFFFFF"), ("Heather Gray", "#D5D6D8"), ("Sky Blue", "#A9D4F5"), ("Soft Pink", "#F7C8D6"), ("Mint", "#BFEBD7"), ("Lemon", "#FFF3A8")]);

        AddMug(mugs, "White Ceramic Mug", "white-ceramic-mug", 12.99m, featured: true,
            "Glossy white ceramic mug with an edge-to-edge wrap print. Available in 11 oz and 15 oz.",
            "A classic glossy ceramic mug with a comfortable C-handle. Your design wraps around the whole mug and is fused into the coating, so it is dishwasher and microwave safe.",
            [("White", "#FFFFFF", "11 oz", 0m), ("White", "#FFFFFF", "15 oz", 3m)]);

        AddMug(mugs, "Magic Color-Changing Mug", "magic-color-changing-mug", 17.99m, featured: true,
            "Looks solid black until you pour a hot drink, then your design magically appears.",
            "A heat-reactive coating hides your design until the mug is filled with a hot drink. A guaranteed smile every morning. Hand wash recommended to preserve the magic effect.",
            [("Black (heat reveal)", "#1C1C1E", "11 oz", 0m)]);

        AddKeychain(keychains, "Acrylic Keychain", "acrylic-keychain", 6.99m, featured: false,
            "Crystal-clear rectangular acrylic keychain with a vivid two-sided print.",
            "A 3 mm crystal-clear acrylic blank with polished edges and a sturdy split ring. Print a photo, logo or illustration on the front and something different on the back.",
            "Clear", "#EEF3F8", "50 × 70 mm", 50m, 70m, PrintAreaShape.Rectangle);

        AddKeychain(keychains, "Square Keychain", "square-keychain", 5.99m, featured: false,
            "Compact square keychain in glossy white, printed on both sides.",
            "A lightweight hardboard square with a glossy white sublimation coating and a metal ring. Perfect for logos, initials and tiny portraits.",
            "White", "#FFFFFF", "50 × 50 mm", 50m, 50m, PrintAreaShape.Rectangle);

        AddKeychain(keychains, "Round Keychain", "round-keychain", 5.99m, featured: true,
            "Round glossy keychain, great for photos, pets and badges.",
            "A 50 mm round hardboard keychain with a glossy white coating and a metal ring. Designs are printed edge-to-edge and trimmed to the circle.",
            "White", "#FFFFFF", "50 mm Ø", 50m, 50m, PrintAreaShape.Ellipse);

        return [tshirts, mugs, keychains];
    }

    public static IReadOnlyList<ShippingMethod> ShippingMethods() =>
    [
        new ShippingMethod
        {
            Code = "standard",
            Name = "Standard Shipping",
            Description = "Tracked delivery. Free on orders over $50.",
            Cost = 4.99m,
            FreeShippingThreshold = 50m,
            MinDeliveryDays = 3,
            MaxDeliveryDays = 6,
            SortOrder = 1,
        },
        new ShippingMethod
        {
            Code = "express",
            Name = "Express Shipping",
            Description = "Priority handling and delivery in 1-2 business days after production.",
            Cost = 12.99m,
            MinDeliveryDays = 1,
            MaxDeliveryDays = 2,
            SortOrder = 2,
        },
    ];

    public static IReadOnlyList<DiscountCode> DiscountCodes() =>
    [
        new DiscountCode
        {
            Code = "WELCOME10",
            Description = "10% off your first creation",
            Type = DiscountType.Percentage,
            Value = 10m,
        },
    ];

    private static void AddTShirt(ProductCategory category, string name, string slug, decimal price, bool featured, string shortDescription, string description, string line, (string Name, string Hex)[] colors)
    {
        var product = NewProduct(category, name, slug, price, featured, shortDescription, description, productionDays: 2);
        product.Specifications =
        [
            new("Fit", line == "Premium" ? "Tailored, true to size" : "Classic, true to size"),
            new("Fabric", line == "Premium" ? "190 g/m² performance knit" : "160 g/m² polyester-feel jersey"),
            new("Print method", "Dye sublimation"),
            new("Print area", "Front and back, up to 30 × 40 cm each"),
            new("Care", "Machine wash cold inside out, tumble dry low"),
        ];

        var sort = 0;
        foreach (var (colorName, hex) in colors)
        {
            foreach (var size in ApparelSizes)
            {
                product.Variants.Add(new ProductVariant
                {
                    Sku = Sku("TSH", slug, colorName, size),
                    ColorName = colorName,
                    ColorHex = hex,
                    Size = size,
                    PriceAdjustment = size == "XXL" ? 2m : 0m,
                    SortOrder = sort++,
                });
            }
        }

        product.Customization = new CustomizationConfiguration
        {
            Kind = CustomizerKind.Apparel,
            ModelUrl = "/models/tshirt.glb",
            BaseFee = 6m,
            AdditionalPrintAreaFee = 4m,
            MaxElementsPerArea = 10,
            MinRecommendedDpi = 150,
        };
        // UV rectangles are calibrated against the 3D model's texture layout (Phase 7).
        product.Customization.PrintAreas.Add(new PrintArea { Key = "front", Name = "Front", WidthMm = 300, HeightMm = 400, UvX = 0.0m, UvY = 0.0m, UvWidth = 0.5m, UvHeight = 1m, SortOrder = 1 });
        product.Customization.PrintAreas.Add(new PrintArea { Key = "back", Name = "Back", WidthMm = 300, HeightMm = 400, UvX = 0.5m, UvY = 0.0m, UvWidth = 0.5m, UvHeight = 1m, SortOrder = 2 });
    }

    private static void AddMug(ProductCategory category, string name, string slug, decimal price, bool featured, string shortDescription, string description, (string Color, string Hex, string Size, decimal Adjustment)[] variants)
    {
        var product = NewProduct(category, name, slug, price, featured, shortDescription, description, productionDays: 1);
        product.Specifications =
        [
            new("Material", "Ceramic with sublimation coating"),
            new("Capacity", string.Join(" / ", variants.Select(v => v.Size))),
            new("Print area", "Full wrap, 20 × 8.5 cm"),
            new("Care", slug.Contains("magic", StringComparison.Ordinal) ? "Hand wash recommended" : "Dishwasher and microwave safe"),
        ];

        var sort = 0;
        foreach (var (color, hex, size, adjustment) in variants)
        {
            product.Variants.Add(new ProductVariant
            {
                Sku = Sku("MUG", slug, color, size),
                ColorName = color,
                ColorHex = hex,
                Size = size,
                PriceAdjustment = adjustment,
                SortOrder = sort++,
            });
        }

        product.Customization = new CustomizationConfiguration
        {
            Kind = CustomizerKind.Drinkware,
            ModelUrl = "/models/mug.glb",
            BaseFee = 3m,
            AdditionalPrintAreaFee = 0m,
            MaxElementsPerArea = 8,
            MinRecommendedDpi = 200,
        };
        product.Customization.PrintAreas.Add(new PrintArea { Key = "wrap", Name = "Wrap-around", WidthMm = 200, HeightMm = 85, SortOrder = 1 });
    }

    private static void AddKeychain(ProductCategory category, string name, string slug, decimal price, bool featured, string shortDescription, string description, string color, string hex, string size, decimal widthMm, decimal heightMm, PrintAreaShape shape)
    {
        var product = NewProduct(category, name, slug, price, featured, shortDescription, description, productionDays: 1);
        product.Specifications =
        [
            new("Size", size),
            new("Material", slug.StartsWith("acrylic", StringComparison.Ordinal) ? "3 mm cast acrylic" : "Hardboard with glossy coating"),
            new("Print", "Double-sided dye sublimation"),
            new("Hardware", "Nickel-plated split ring"),
        ];

        product.Variants.Add(new ProductVariant
        {
            Sku = Sku("KEY", slug, color, null),
            ColorName = color,
            ColorHex = hex,
            Size = size,
        });

        product.Customization = new CustomizationConfiguration
        {
            Kind = CustomizerKind.Accessory,
            ModelUrl = $"/models/{slug}.glb",
            BaseFee = 2m,
            AdditionalPrintAreaFee = 1.5m,
            MaxElementsPerArea = 5,
            MinRecommendedDpi = 300,
        };
        product.Customization.PrintAreas.Add(new PrintArea { Key = "front", Name = "Front", WidthMm = widthMm, HeightMm = heightMm, Shape = shape, SortOrder = 1 });
        product.Customization.PrintAreas.Add(new PrintArea { Key = "back", Name = "Back", WidthMm = widthMm, HeightMm = heightMm, Shape = shape, SortOrder = 2 });
    }

    private static Product NewProduct(ProductCategory category, string name, string slug, decimal price, bool featured, string shortDescription, string description, int productionDays)
    {
        var product = new Product
        {
            Category = category,
            Name = name,
            Slug = slug,
            BasePrice = price,
            IsFeatured = featured,
            ShortDescription = shortDescription,
            Description = description,
            ProductionTimeDays = productionDays,
        };

        product.Images.Add(new ProductImage { Url = $"/images/products/{slug}.svg", AltText = name, SortOrder = 0 });
        category.Products.Add(product);
        return product;
    }

    private static string Sku(string prefix, string slug, string color, string? size)
    {
        var model = string.Concat(slug.Split('-').Take(2).Select(part => char.ToUpperInvariant(part[0])));
        var colorCode = new string(color.Where(char.IsLetter).Take(3).ToArray()).ToUpperInvariant();
        var sizeCode = size is null ? string.Empty : "-" + new string(size.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return $"{prefix}-{model}-{colorCode}{sizeCode}";
    }
}
