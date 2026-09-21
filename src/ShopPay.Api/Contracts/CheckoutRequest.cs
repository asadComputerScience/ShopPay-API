using System.ComponentModel.DataAnnotations;
using ShopPay.Api.Domain;

namespace ShopPay.Api.Contracts;

/// <summary>Body of <c>POST /checkout</c>.</summary>
public sealed class CheckoutRequest
{
    /// <summary>What the customer wants to buy (1 to 20 lines).</summary>
    [Required]
    [MinLength(1, ErrorMessage = "Add at least one item.")]
    [MaxLength(20, ErrorMessage = "An order can contain at most 20 lines.")]
    public List<CheckoutItemRequest> Items { get; init; } = [];
}

/// <summary>One product and how many of it to buy.</summary>
public sealed class CheckoutItemRequest
{
    /// <summary>Id of a product returned by <c>GET /products</c>.</summary>
    /// <example>1</example>
    [Range(1, int.MaxValue, ErrorMessage = "ProductId must be a positive number.")]
    public int ProductId { get; init; }

    /// <summary>How many to buy (1 to 99).</summary>
    /// <example>2</example>
    [Range(1, Order.MaxQuantityPerItem, ErrorMessage = "Quantity must be between 1 and 99.")]
    public int Quantity { get; init; }
}
