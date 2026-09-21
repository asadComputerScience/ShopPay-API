namespace ShopPay.Api.Domain;

/// <summary>
/// The facts Stripe tells us about a finished payment. The order checks these against its own
/// records before it will flip to <see cref="OrderStatus.Paid"/>.
/// </summary>
/// <param name="SessionId">The Stripe Checkout Session id (cs_test_...).</param>
/// <param name="AmountCents">Total Stripe actually charged, in cents.</param>
/// <param name="Currency">Three-letter currency code Stripe charged in.</param>
/// <param name="PaymentIntentId">The Stripe PaymentIntent id (pi_...), if known.</param>
public sealed record PaymentConfirmation(
    string SessionId,
    long AmountCents,
    string Currency,
    string? PaymentIntentId = null);
