namespace ShopPay.Api.Payments;

/// <summary>Stripe is not set up (missing key / webhook secret, or a non-test key). Maps to HTTP 503.</summary>
public sealed class PaymentConfigurationException(string message) : Exception(message);

/// <summary>Stripe could not be reached or rejected our request. Maps to HTTP 502.</summary>
public sealed class PaymentGatewayException(string message, Exception? innerException = null)
    : Exception(message, innerException);
