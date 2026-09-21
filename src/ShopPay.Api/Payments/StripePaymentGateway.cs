using Microsoft.Extensions.Options;
using ShopPay.Api.Configuration;
using Stripe;
using Stripe.Checkout;
using DomainOrder = ShopPay.Api.Domain.Order; // Stripe.net has types with common names, so be explicit.

namespace ShopPay.Api.Payments;

/// <summary>Creates Stripe Checkout Sessions (Stripe's hosted payment page). TEST MODE ONLY.</summary>
public sealed class StripePaymentGateway(
    IOptions<StripeSettings> options,
    ILogger<StripePaymentGateway> logger) : IPaymentGateway
{
    private StripeClient? _client;

    public void EnsureConfigured()
    {
        var settings = options.Value;

        if (!settings.HasSecretKey)
        {
            throw new PaymentConfigurationException(
                "Stripe is not configured. Set 'Stripe:SecretKey' with `dotnet user-secrets` (see README).");
        }

        // Safety net: this portfolio project must never touch real money.
        if (!settings.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal) &&
            !settings.SecretKey.StartsWith("rk_test_", StringComparison.Ordinal))
        {
            throw new PaymentConfigurationException(
                "Only Stripe TEST-mode keys (sk_test_... or rk_test_...) are accepted by this project.");
        }
    }

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        DomainOrder order,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var settings = options.Value;
        var baseUrl = settings.PublicBaseUrl.TrimEnd('/');

        var sessionOptions = new SessionCreateOptions
        {
            Mode = "payment",

            // {CHECKOUT_SESSION_ID} is a literal placeholder that Stripe replaces itself.
            SuccessUrl = $"{baseUrl}/orders/{order.Id}?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{baseUrl}/orders/{order.Id}",

            // Two ways for the webhook to find our order again.
            ClientReferenceId = order.Id.ToString(),
            Metadata = new Dictionary<string, string> { ["order_id"] = order.Id.ToString() },

            LineItems = order.Items
                .Select(item => new SessionLineItemOptions
                {
                    Quantity = item.Quantity,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = order.Currency,
                        UnitAmount = item.UnitPriceCents,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = item.ProductName,
                        },
                    },
                })
                .ToList(),
        };

        try
        {
            _client ??= new StripeClient(settings.SecretKey);
            var service = new SessionService(_client);

            // The idempotency key means a retry of the same order can never create two sessions.
            var session = await service.CreateAsync(
                sessionOptions,
                new RequestOptions { IdempotencyKey = $"checkout-{order.Id}" },
                cancellationToken);

            if (string.IsNullOrEmpty(session.Url))
            {
                throw new PaymentGatewayException("Stripe did not return a payment URL.");
            }

            return new CheckoutSessionResult(session.Id, session.Url);
        }
        catch (StripeException ex)
        {
            // Log the detail for developers, but keep the message we return to clients generic.
            logger.LogError(ex, "Stripe rejected checkout session for order {OrderId}: {StripeMessage}",
                order.Id, ex.StripeError?.Message ?? ex.Message);
            throw new PaymentGatewayException("The payment provider could not create a checkout session.", ex);
        }
    }
}
