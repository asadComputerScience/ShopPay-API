namespace ShopPay.Api.Domain;

/// <summary>An item in the shop's catalogue.</summary>
public class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Price in the smallest currency unit (cents). Money is stored as whole integers
    /// on purpose: floating point / decimal rounding bugs and money do not mix.
    /// </summary>
    public long PriceCents { get; set; }
}
