using ShopPay.Api.Domain;

namespace ShopPay.Api.Payments;

/// <summary>What the payment provider gives back after creating a hosted checkout page.</summary>
/// <param name="SessionId">Provider's id for the checkout session.</param>
/// <param name="Url">Where the customer goes to pay.</param>
public sealed record CheckoutSessionResult(string SessionId, string Url);

/// <summary>
/// The only thing the order logic needs from a payment provider.
/// Hiding Stripe behind this interface lets unit tests use a fake instead of calling the internet.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Throws <see cref="PaymentConfigurationException"/> if the provider is not usable yet.</summary>
    void EnsureConfigured();

    /// <exception cref="PaymentGatewayException">The provider failed or refused.</exception>
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(Order order, CancellationToken cancellationToken = default);
}
