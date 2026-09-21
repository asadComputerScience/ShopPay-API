using ShopPay.Api.Domain;

namespace ShopPay.Tests;

/// <summary>Tests for the rules built into the <see cref="Order"/> itself. No database, no Stripe.</summary>
public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly Product Keyboard = new() { Id = 1, Name = "Keyboard", PriceCents = 8999 };
    private static readonly Product Mouse = new() { Id = 2, Name = "Mouse", PriceCents = 3450 };

    private static Order NewOrder() =>
        Order.Create([(Keyboard, 2), (Mouse, 1)], "USD", Now);

    private static PaymentConfirmation PaymentFor(Order order, string sessionId = "cs_test_123") =>
        new(sessionId, order.TotalCents, "usd", "pi_test_123");

    [Fact]
    public void Create_StartsPending_AndCalculatesTotalFromLines()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(2 * 8999 + 3450, order.TotalCents); // 21448 cents
        Assert.Equal(2, order.Items.Count);
        Assert.Equal("usd", order.Currency); // normalised to lower case
        Assert.Equal(Now, order.CreatedAt);
        Assert.Null(order.PaidAt);
    }

    [Fact]
    public void Create_CopiesNameAndPrice_SoLaterCatalogueChangesDoNotAffectTheOrder()
    {
        var product = new Product { Id = 7, Name = "Old name", PriceCents = 1000 };
        var order = Order.Create([(product, 1)], "usd", Now);

        product.Name = "New name";
        product.PriceCents = 9999;

        var line = Assert.Single(order.Items);
        Assert.Equal("Old name", line.ProductName);
        Assert.Equal(1000, line.UnitPriceCents);
        Assert.Equal(1000, order.TotalCents);
    }

    [Fact]
    public void Create_WithoutItems_IsRejected()
    {
        Assert.Throws<OrderValidationException>(() => Order.Create([], "usd", Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(Order.MaxQuantityPerItem + 1)]
    public void Create_WithQuantityOutOfRange_IsRejected(int quantity)
    {
        Assert.Throws<OrderValidationException>(() => Order.Create([(Keyboard, quantity)], "usd", Now));
    }

    [Fact]
    public void MarkPaid_MovesPendingOrderToPaid_AndRecordsWhenAndHow()
    {
        var order = NewOrder();
        var paidAt = Now.AddMinutes(3);

        var changed = order.MarkPaid(PaymentFor(order), paidAt);

        Assert.True(changed);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(paidAt, order.PaidAt);
        Assert.Equal("cs_test_123", order.StripeSessionId);
        Assert.Equal("pi_test_123", order.StripePaymentIntentId);
    }

    [Fact]
    public void MarkPaid_CalledTwice_IsHarmless()
    {
        // Stripe can deliver the same webhook more than once.
        var order = NewOrder();
        var firstPaidAt = Now.AddMinutes(1);
        order.MarkPaid(PaymentFor(order), firstPaidAt);

        var changedAgain = order.MarkPaid(PaymentFor(order), Now.AddMinutes(30));

        Assert.False(changedAgain);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(firstPaidAt, order.PaidAt); // the original timestamp is kept
    }

    [Fact]
    public void MarkPaid_WithWrongAmount_IsRejected_AndOrderStaysPending()
    {
        var order = NewOrder();
        var tooLittle = new PaymentConfirmation("cs_test_123", order.TotalCents - 1, "usd");

        Assert.Throws<OrderStateException>(() => order.MarkPaid(tooLittle, Now));

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.PaidAt);
    }

    [Fact]
    public void MarkPaid_WithWrongCurrency_IsRejected()
    {
        var order = NewOrder();
        var wrongCurrency = new PaymentConfirmation("cs_test_123", order.TotalCents, "eur");

        Assert.Throws<OrderStateException>(() => order.MarkPaid(wrongCurrency, Now));
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void MarkPaid_FromDifferentCheckoutSession_IsRejected()
    {
        var order = NewOrder();
        order.AttachCheckoutSession("cs_test_original");

        Assert.Throws<OrderStateException>(() => order.MarkPaid(PaymentFor(order, "cs_test_other"), Now));
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void MarkPaid_OnCancelledOrder_IsRejected()
    {
        var order = NewOrder();
        order.Cancel();

        Assert.Throws<OrderStateException>(() => order.MarkPaid(PaymentFor(order), Now));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_OnPaidOrder_IsRejected()
    {
        var order = NewOrder();
        order.MarkPaid(PaymentFor(order), Now);

        Assert.Throws<OrderStateException>(() => order.Cancel());
        Assert.Equal(OrderStatus.Paid, order.Status);
    }
}
