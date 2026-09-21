namespace ShopPay.Api.Configuration;

/// <summary>
/// Settings bound from the "Stripe" configuration section.
/// The real keys are NEVER stored in the repository: put them in user-secrets
/// (<c>dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."</c>) or environment variables.
/// </summary>
public sealed class StripeSettings
{
    public const string SectionName = "Stripe";

    /// <summary>Stripe TEST secret key (sk_test_...). Comes from user-secrets.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Webhook signing secret (whsec_...). Printed by <c>stripe listen</c>.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Currency used for every order (ISO code).</summary>
    public string Currency { get; set; } = "usd";

    /// <summary>Where Stripe sends the customer back to after paying or cancelling.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5062";

    /// <summary>False while the value is empty or still the "&lt;placeholder&gt;" from appsettings.json.</summary>
    public bool HasSecretKey => IsFilledIn(SecretKey);

    public bool HasWebhookSecret => IsFilledIn(WebhookSecret);

    private static bool IsFilledIn(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith('<');
}
