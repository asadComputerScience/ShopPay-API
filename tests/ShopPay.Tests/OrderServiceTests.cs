using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShopPay.Api.Configuration;
using ShopPay.Api.Contracts;
using ShopPay.Api.Data;
using ShopPay.Api.Domain;
using ShopPay.Api.Payments;
using ShopPay.Api.Services;

namespace ShopPay.Tests;

/// <summary>
/// Tests the checkout / payment flow against a real (in-memory) SQLite database
/// with a fake payment gateway, so nothing here ever talks to Stripe.
/// </summary>
public sealed class OrderServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    // Seeded automatically by ShopPayDbContext (HasData).
    private static readonly Product Keyboard = SeedData.Products[0]; // 8999 cents
    private static readonly Product Mouse = SeedData.Products[1];    // 3450 cents

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ShopPayDbContext> _dbOptions;
    private readonly FakePaymentGateway _gateway = new();

    public OrderServiceTests()
    {
        // An in-memory SQLite database lives only as long as its connection stays open.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<ShopPayDbContext>().UseSqlite(_connection).Options;

        using var db = new ShopPayDbContext(_dbOptions);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ShopPayDbContext NewDbContext() => new(_dbOptions);

    private OrderService NewService(ShopPayDbContext db) => new(
        db,
        _gateway,
        new FixedTimeProvider(Now),
        Options.Create(new StripeSettings { Currency = "usd" }),
        NullLogger<OrderService>.Instance);

    private static CheckoutRequest Request(params (int ProductId, int Quantity)[] lines) => new()
    {
        Items = lines.Select(l => new CheckoutItemRequest { ProductId = l.ProductId, Quantity = l.Quantity }).ToList(),
    };

    [Fact]
    public async Task Checkout_CreatesPendingOrder_AttachesSession_AndReturnsPaymentUrl()
    {
        using var db = NewDbContext();
        var service = NewService(db);

        var result = await service.CheckoutAsync(Request((Keyboard.Id, 2), (Mouse.Id, 1)));

        Assert.Equal(OrderStatus.Pending, result.Order.Status);
        Assert.Equal(2 * Keyboard.PriceCents + Mouse.PriceCents, result.Order.TotalCents);
        Assert.StartsWith("https://", result.PaymentUrl);

        // Check what really reached the database, using a brand-new context.
        using var verify = NewDbContext();
        var saved = await verify.Orders.Include(o => o.Items).SingleAsync();
        Assert.Equal(result.Order.Id, saved.Id);
        Assert.Equal(OrderStatus.Pending, saved.Status);
        Assert.Equal(_gateway.LastSessionId, saved.StripeSessionId);
        Assert.Equal(2, saved.Items.Count);
    }

    [Fact]
    public async Task Checkout_WithUnknownProduct_IsRejected_AndSavesNothing()
    {
        using var db = NewDbContext();
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<OrderValidationException>(
            () => service.CheckoutAsync(Request((Keyboard.Id, 1), (999, 1))));

        Assert.Contains("999", error.Errors["items"][0]);
        using var verify = NewDbContext();
        Assert.Equal(0, await verify.Orders.CountAsync());
        Assert.Equal(0, _gateway.SessionsCreated);
    }

    [Fact]
    public async Task Checkout_SameProductTwice_IsMergedIntoOneLine()
    {
        using var db = NewDbContext();
        var service = NewService(db);

        var result = await service.CheckoutAsync(Request((Keyboard.Id, 1), (Keyboard.Id, 2)));

        var line = Assert.Single(result.Order.Items);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(3 * Keyboard.PriceCents, result.Order.TotalCents);
    }

    [Fact]
    public async Task Checkout_WhenMergedQuantityIsTooLarge_IsRejected()
    {
        using var db = NewDbContext();
        var service = NewService(db);

        await Assert.ThrowsAsync<OrderValidationException>(
            () => service.CheckoutAsync(Request((Keyboard.Id, 60), (Keyboard.Id, 60))));

        using var verify = NewDbContext();
        Assert.Equal(0, await verify.Orders.CountAsync());
    }

    [Fact]
    public async Task Checkout_WhenStripeFails_CancelsTheOrder_SoNoOrphanStaysPending()
    {
        _gateway.ShouldFail = true;
        using var db = NewDbContext();
        var service = NewService(db);

        await Assert.ThrowsAsync<PaymentGatewayException>(
            () => service.CheckoutAsync(Request((Mouse.Id, 1))));

        using var verify = NewDbContext();
        var saved = await verify.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, saved.Status);
    }

    [Fact]
    public async Task MarkPaid_UpdatesTheOrderInTheDatabase_AndIsSafeToRepeat()
    {
        using var db = NewDbContext();
        var service = NewService(db);
        var checkout = await service.CheckoutAsync(Request((Keyboard.Id, 1)));
        var payment = new PaymentConfirmation(
            _gateway.LastSessionId!, checkout.Order.TotalCents, "usd", "pi_test_1");

        var first = await service.MarkPaidAsync(checkout.Order.Id, payment);
        var duplicate = await service.MarkPaidAsync(checkout.Order.Id, payment);

        Assert.Equal(PaymentResult.MarkedPaid, first);
        Assert.Equal(PaymentResult.AlreadyPaid, duplicate);

        using var verify = NewDbContext();
        var saved = await verify.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Paid, saved.Status);
        Assert.Equal(Now, saved.PaidAt);
        Assert.Equal("pi_test_1", saved.StripePaymentIntentId);
    }

    [Fact]
    public async Task MarkPaid_ForUnknownOrder_ReportsNotFound()
    {
        using var db = NewDbContext();
        var service = NewService(db);

        var result = await service.MarkPaidAsync(
            Guid.NewGuid(), new PaymentConfirmation("cs_test_x", 100, "usd"));

        Assert.Equal(PaymentResult.OrderNotFound, result);
    }

    [Fact]
    public async Task CancelPending_NeverCancelsAnOrderThatIsAlreadyPaid()
    {
        using var db = NewDbContext();
        var service = NewService(db);
        var checkout = await service.CheckoutAsync(Request((Mouse.Id, 1)));
        await service.MarkPaidAsync(
            checkout.Order.Id,
            new PaymentConfirmation(_gateway.LastSessionId!, checkout.Order.TotalCents, "usd"));

        var cancelled = await service.CancelPendingAsync(checkout.Order.Id);

        Assert.False(cancelled);
        using var verify = NewDbContext();
        Assert.Equal(OrderStatus.Paid, (await verify.Orders.SingleAsync()).Status);
    }

    // ---- test doubles ------------------------------------------------------------------------

    private sealed class FakePaymentGateway : IPaymentGateway
    {
        public bool ShouldFail { get; set; }

        public string? LastSessionId { get; private set; }

        public int SessionsCreated { get; private set; }

        public void EnsureConfigured()
        {
        }

        public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
            Order order,
            CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new PaymentGatewayException("Simulated Stripe outage.");
            }

            SessionsCreated++;
            LastSessionId = $"cs_test_{order.Id:N}";
            return Task.FromResult(new CheckoutSessionResult(LastSessionId, $"https://checkout.stripe.test/pay/{LastSessionId}"));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
