using Microsoft.Extensions.Options;
using ShopPay.Api.Configuration;
using ShopPay.Api.Domain;
using ShopPay.Api.Payments;
using Stripe;
using Stripe.Checkout;

// NOTE: this file lives in the same namespace as OrderService on purpose. Stripe.net also has
// types with generic names, and types from the current namespace win over "using" imports.
namespace ShopPay.Api.Services;

public enum WebhookOutcome
{
    /// <summary>The event was understood and acted on.</summary>
    Processed,

    /// <summary>The event was genuine but needs no action (other event type, unknown order, ...).</summary>
    Ignored,

    /// <summary>The signature check failed: this request did not come from Stripe.</summary>
    InvalidSignature,
}

/// <summary>
/// Verifies and interprets webhook calls from Stripe.
/// Rule #1 of webhooks: never trust the request until the signature has been verified.
/// </summary>
public sealed class StripeWebhookHandler(
    IOptions<StripeSettings> options,
    OrderService orders,
    ILogger<StripeWebhookHandler> logger)
{
    public const string SessionCompletedEvent = "checkout.session.completed";
    public const string SessionExpiredEvent = "checkout.session.expired";

    /// <param name="payload">The RAW request body, exactly as received. Re-serialised JSON would break the signature.</param>
    /// <param name="signatureHeader">Value of the "Stripe-Signature" header.</param>
    public async Task<WebhookOutcome> HandleAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.HasWebhookSecret)
        {
            throw new PaymentConfigurationException(
                "Stripe webhook secret is not configured. Set 'Stripe:WebhookSecret' with `dotnet user-secrets` (see README).");
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            logger.LogWarning("Rejected webhook call without a Stripe-Signature header");
            return WebhookOutcome.InvalidSignature;
        }

        Event stripeEvent;
        try
        {
            // Checks the HMAC signature AND that the timestamp is recent (guards against replayed requests).
            // throwOnApiVersionMismatch: false -> the Stripe CLI / dashboard may use a newer API version
            // than the one this Stripe.net release was built for; we only read a few stable fields.
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                signatureHeader,
                settings.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Rejected webhook call with an invalid signature: {Reason}", ex.Message);
            return WebhookOutcome.InvalidSignature;
        }

        if (stripeEvent.Type == SessionCompletedEvent && stripeEvent.Data.Object is Session completed)
        {
            return await OnSessionCompletedAsync(completed, cancellationToken);
        }

        if (stripeEvent.Type == SessionExpiredEvent && stripeEvent.Data.Object is Session expired)
        {
            return await OnSessionExpiredAsync(expired, cancellationToken);
        }

        logger.LogInformation("Ignoring Stripe event {EventId} of type {EventType}", stripeEvent.Id, stripeEvent.Type);
        return WebhookOutcome.Ignored;
    }

    private async Task<WebhookOutcome> OnSessionCompletedAsync(Session session, CancellationToken cancellationToken)
    {
        if (!TryGetOrderId(session, out var orderId))
        {
            logger.LogWarning("Checkout session {SessionId} has no valid order id, ignoring", session.Id);
            return WebhookOutcome.Ignored;
        }

        // "completed" can also fire for delayed payment methods that have not been paid yet.
        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Session {SessionId} completed but payment_status is {PaymentStatus}, order {OrderId} stays Pending",
                session.Id, session.PaymentStatus, orderId);
            return WebhookOutcome.Ignored;
        }

        var payment = new PaymentConfirmation(
            session.Id,
            session.AmountTotal ?? -1,
            session.Currency ?? string.Empty,
            session.PaymentIntentId);

        try
        {
            var result = await orders.MarkPaidAsync(orderId, payment, cancellationToken);
            switch (result)
            {
                case PaymentResult.MarkedPaid:
                    logger.LogInformation("Order {OrderId} marked as Paid", orderId);
                    return WebhookOutcome.Processed;

                case PaymentResult.AlreadyPaid:
                    logger.LogInformation("Order {OrderId} was already Paid (duplicate webhook delivery)", orderId);
                    return WebhookOutcome.Processed;

                default:
                    logger.LogWarning("Webhook referred to unknown order {OrderId}", orderId);
                    return WebhookOutcome.Ignored;
            }
        }
        catch (OrderStateException ex)
        {
            // Answer 200 anyway: Stripe retrying the same event would not fix this. A human needs to look.
            logger.LogError(ex, "Payment for order {OrderId} could not be applied and needs manual review", orderId);
            return WebhookOutcome.Ignored;
        }
    }

    private async Task<WebhookOutcome> OnSessionExpiredAsync(Session session, CancellationToken cancellationToken)
    {
        if (!TryGetOrderId(session, out var orderId))
        {
            return WebhookOutcome.Ignored;
        }

        var cancelled = await orders.CancelPendingAsync(orderId, cancellationToken);
        if (cancelled)
        {
            logger.LogInformation("Order {OrderId} cancelled because its checkout session expired", orderId);
        }

        return cancelled ? WebhookOutcome.Processed : WebhookOutcome.Ignored;
    }

    private static bool TryGetOrderId(Session session, out Guid orderId)
    {
        if (Guid.TryParse(session.ClientReferenceId, out orderId))
        {
            return true;
        }

        if (session.Metadata is not null &&
            session.Metadata.TryGetValue("order_id", out var fromMetadata) &&
            Guid.TryParse(fromMetadata, out orderId))
        {
            return true;
        }

        orderId = Guid.Empty;
        return false;
    }
}
