namespace ShopPay.Api.Domain;

/// <summary>
/// An order and its life cycle. All the business rules about how an order may change state live here
/// (rather than in controllers), which keeps them easy to unit test.
/// <code>
///   Pending --MarkPaid--> Paid
///   Pending --Cancel----> Cancelled
/// </code>
/// </summary>
public sealed class Order
{
    public const int MaxQuantityPerItem = 99;

    private readonly List<OrderItem> _items = [];

    private Order()
    {
        // Required by EF Core. Use Order.Create(...) in code.
    }

    public Guid Id { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>Lower-case ISO currency code, e.g. "usd".</summary>
    public string Currency { get; private set; } = string.Empty;

    public long TotalCents { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public string? StripeSessionId { get; private set; }

    public string? StripePaymentIntentId { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items;

    /// <summary>Builds a new <see cref="OrderStatus.Pending"/> order and calculates its total.</summary>
    public static Order Create(IEnumerable<(Product Product, int Quantity)> lines, string currency, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var materialised = lines.ToList();
        if (materialised.Count == 0)
        {
            throw new OrderValidationException("items", "An order needs at least one item.");
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            Currency = currency.Trim().ToLowerInvariant(),
            CreatedAt = now,
        };

        foreach (var (product, quantity) in materialised)
        {
            if (quantity is < 1 or > MaxQuantityPerItem)
            {
                throw new OrderValidationException(
                    "items",
                    $"Quantity for product {product.Id} must be between 1 and {MaxQuantityPerItem}.");
            }

            order._items.Add(new OrderItem(product.Id, product.Name, product.PriceCents, quantity));
        }

        order.TotalCents = order._items.Sum(i => i.LineTotalCents);
        return order;
    }

    /// <summary>Remembers which Stripe Checkout Session belongs to this order.</summary>
    public void AttachCheckoutSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (Status != OrderStatus.Pending)
        {
            throw new OrderStateException($"Order {Id} is {Status} and cannot get a new checkout session.");
        }

        StripeSessionId = sessionId;
    }

    /// <summary>
    /// Marks the order as paid after checking that Stripe's numbers match ours.
    /// Safe to call twice: Stripe may deliver the same webhook more than once.
    /// </summary>
    /// <returns><c>true</c> if the order changed; <c>false</c> if it was already paid.</returns>
    /// <exception cref="OrderStateException">
    /// The order is cancelled, or the session / amount / currency does not match this order.
    /// </exception>
    public bool MarkPaid(PaymentConfirmation payment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (Status == OrderStatus.Paid)
        {
            return false;
        }

        if (Status == OrderStatus.Cancelled)
        {
            throw new OrderStateException($"Order {Id} is cancelled and cannot be marked as paid.");
        }

        if (StripeSessionId is not null && !string.Equals(StripeSessionId, payment.SessionId, StringComparison.Ordinal))
        {
            throw new OrderStateException($"Payment for order {Id} came from a different checkout session.");
        }

        if (payment.AmountCents != TotalCents ||
            !string.Equals(payment.Currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderStateException(
                $"Payment for order {Id} does not match the order total " +
                $"(expected {TotalCents} {Currency}, got {payment.AmountCents} {payment.Currency}).");
        }

        Status = OrderStatus.Paid;
        PaidAt = now;
        StripeSessionId ??= payment.SessionId;
        StripePaymentIntentId = payment.PaymentIntentId;
        return true;
    }

    /// <summary>Cancels an unpaid order. A paid order can never be cancelled here.</summary>
    /// <returns><c>true</c> if the order changed; <c>false</c> if it was already cancelled.</returns>
    public bool Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }

        if (Status == OrderStatus.Paid)
        {
            throw new OrderStateException($"Order {Id} is already paid and cannot be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        return true;
    }
}
