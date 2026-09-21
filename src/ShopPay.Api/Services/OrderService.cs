using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShopPay.Api.Configuration;
using ShopPay.Api.Contracts;
using ShopPay.Api.Data;
using ShopPay.Api.Domain;
using ShopPay.Api.Payments;

namespace ShopPay.Api.Services;

/// <summary>Result of <see cref="OrderService.CheckoutAsync"/>.</summary>
public sealed record CheckoutResult(Order Order, string PaymentUrl);

public enum PaymentResult
{
    /// <summary>The order was Pending and is now Paid.</summary>
    MarkedPaid,

    /// <summary>Duplicate delivery: the order was already Paid, nothing changed.</summary>
    AlreadyPaid,

    OrderNotFound,
}

/// <summary>
/// Coordinates the database, the payment provider and the <see cref="Order"/> rules.
/// Controllers stay thin; this class is where "what happens when someone checks out" lives.
/// </summary>
public sealed class OrderService(
    ShopPayDbContext db,
    IPaymentGateway gateway,
    TimeProvider clock,
    IOptions<StripeSettings> settings,
    ILogger<OrderService> logger)
{
    /// <summary>
    /// Creates a Pending order for the requested products, then a Stripe Checkout Session for it.
    /// If Stripe fails, the order is cancelled so no orphan "Pending" orders are left behind.
    /// </summary>
    public async Task<CheckoutResult> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Fail fast (before touching the database) if Stripe is not set up.
        gateway.EnsureConfigured();

        // The same product listed twice is treated as one line with the summed quantity.
        var quantities = request.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var productIds = quantities.Keys.ToList();
        var products = await db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var missing = productIds.Except(products.Select(p => p.Id)).Order().ToList();
        if (missing.Count > 0)
        {
            throw new OrderValidationException(
                "items",
                $"Unknown product id(s): {string.Join(", ", missing)}.");
        }

        var order = Order.Create(
            products.OrderBy(p => p.Id).Select(p => (p, quantities[p.Id])),
            settings.Value.Currency,
            clock.GetUtcNow());

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        CheckoutSessionResult session;
        try
        {
            session = await gateway.CreateCheckoutSessionAsync(order, cancellationToken);
        }
        catch
        {
            order.Cancel();
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning("Order {OrderId} cancelled because the checkout session could not be created", order.Id);
            throw;
        }

        order.AttachCheckoutSession(session.SessionId);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderId} created, waiting for payment (session {SessionId})", order.Id, session.SessionId);
        return new CheckoutResult(order, session.Url);
    }

    public Task<Order?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    /// <summary>Called by the webhook once Stripe says the payment went through.</summary>
    /// <exception cref="OrderStateException">The order is cancelled or the payment does not match it.</exception>
    public async Task<PaymentResult> MarkPaidAsync(
        Guid orderId,
        PaymentConfirmation payment,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return PaymentResult.OrderNotFound;
        }

        if (!order.MarkPaid(payment, clock.GetUtcNow()))
        {
            return PaymentResult.AlreadyPaid;
        }

        await db.SaveChangesAsync(cancellationToken);
        return PaymentResult.MarkedPaid;
    }

    /// <summary>Cancels an order whose checkout session expired. Paid orders are left alone.</summary>
    /// <returns><c>true</c> if an order was actually cancelled.</returns>
    public async Task<bool> CancelPendingAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null || order.Status != OrderStatus.Pending)
        {
            return false;
        }

        order.Cancel();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
