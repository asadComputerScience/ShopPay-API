using ShopPay.Api.Domain;

namespace ShopPay.Api.Data;

/// <summary>Sample catalogue inserted into a brand-new database.</summary>
public static class SeedData
{
    public static readonly IReadOnlyList<Product> Products =
    [
        new Product
        {
            Id = 1,
            Name = "Mechanical Keyboard",
            Description = "Hot-swappable 75% keyboard with tactile switches.",
            PriceCents = 8999,
        },
        new Product
        {
            Id = 2,
            Name = "Wireless Mouse",
            Description = "Ergonomic 2.4 GHz / Bluetooth mouse, 70-day battery.",
            PriceCents = 3450,
        },
        new Product
        {
            Id = 3,
            Name = "USB-C Hub",
            Description = "7-in-1 hub: HDMI, 2x USB-A, SD, microSD, PD charging.",
            PriceCents = 4999,
        },
        new Product
        {
            Id = 4,
            Name = "Laptop Stand",
            Description = "Foldable aluminium stand, fits 11-17 inch laptops.",
            PriceCents = 2900,
        },
    ];
}
