using ShopPay.Api.Domain;

namespace ShopPay.Api.Contracts;

/// <summary>A product in the catalogue.</summary>
/// <param name="PriceCents">Price in cents (2999 = 29.99).</param>
public sealed record ProductResponse(int Id, string Name, string Description, long PriceCents, string Currency)
{
    public static ProductResponse From(Product product, string currency) =>
        new(product.Id, product.Name, product.Description, product.PriceCents, currency);
}

/// <summary>Returned after a successful checkout.</summary>
/// <param name="OrderId">Use this with <c>GET /orders/{id}</c> to follow the order.</param>
/// <param name="PaymentUrl">Send the customer here to pay on Stripe's hosted page.</param>
public sealed record CheckoutResponse(
    Guid OrderId,
    string Status,
    long TotalCents,
    string Currency,
    string PaymentUrl);

/// <summary>One line of an order.</summary>
public sealed record OrderItemResponse(
    int ProductId,
    string ProductName,
    long UnitPriceCents,
    int Quantity,
    long LineTotalCents);

/// <summary>An order and its current status.</summary>
/// <param name="Status">Pending, Paid or Cancelled.</param>
public sealed record OrderResponse(
    Guid Id,
    string Status,
    string Currency,
    long TotalCents,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.Currency,
        order.TotalCents,
        order.CreatedAt,
        order.PaidAt,
        order.Items
            .Select(i => new OrderItemResponse(i.ProductId, i.ProductName, i.UnitPriceCents, i.Quantity, i.LineTotalCents))
            .ToList());
}

/// <summary>Acknowledgement sent back to Stripe.</summary>
public sealed record WebhookAckResponse(bool Received);
