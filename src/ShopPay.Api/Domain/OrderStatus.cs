namespace ShopPay.Api.Domain;

public enum OrderStatus
{
    /// <summary>Order created, waiting for the customer to pay on Stripe Checkout.</summary>
    Pending = 0,

    /// <summary>Stripe confirmed the payment (via the signed webhook).</summary>
    Paid = 1,

    /// <summary>The checkout session expired, or it could never be created.</summary>
    Cancelled = 2,
}
