using Microsoft.AspNetCore.Mvc;
using ShopPay.Api.Contracts;
using ShopPay.Api.Services;

namespace ShopPay.Api.Controllers;

[ApiController]
[Route("checkout")]
[Produces("application/json")]
public class CheckoutController(OrderService orders) : ControllerBase
{
    /// <summary>
    /// Creates a Pending order and a Stripe Checkout Session, and returns the payment URL.
    /// </summary>
    /// <remarks>
    /// Open <c>paymentUrl</c> in a browser and pay with the Stripe test card <c>4242 4242 4242 4242</c>
    /// (any future expiry, any CVC). Once Stripe calls the webhook, <c>GET /orders/{id}</c> shows <c>Paid</c>.
    /// </remarks>
    /// <response code="201">Order created. Send the customer to <c>paymentUrl</c>.</response>
    /// <response code="400">Invalid input (empty basket, bad quantity, unknown product).</response>
    /// <response code="502">Stripe could not create the checkout session.</response>
    /// <response code="503">Stripe keys are not configured on the server.</response>
    [HttpPost]
    [ProducesResponseType<CheckoutResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CheckoutResponse>> Checkout(
        CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var result = await orders.CheckoutAsync(request, cancellationToken);

        var response = new CheckoutResponse(
            result.Order.Id,
            result.Order.Status.ToString(),
            result.Order.TotalCents,
            result.Order.Currency,
            result.PaymentUrl);

        return Created($"/orders/{result.Order.Id}", response);
    }
}
