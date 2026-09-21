namespace ShopPay.Api.Domain;

/// <summary>
/// One line of an order. Name and price are copied ("snapshotted") from the product at purchase time,
/// so changing the catalogue later never rewrites history.
/// </summary>
public sealed class OrderItem
{
    private OrderItem()
    {
        // Required by EF Core.
    }

    internal OrderItem(int productId, string productName, long unitPriceCents, int quantity)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPriceCents = unitPriceCents;
        Quantity = quantity;
    }

    public int Id { get; private set; }

    public Guid OrderId { get; private set; }

    public int ProductId { get; private set; }

    public string ProductName { get; private set; } = string.Empty;

    public long UnitPriceCents { get; private set; }

    public int Quantity { get; private set; }

    public long LineTotalCents => UnitPriceCents * Quantity;
}
