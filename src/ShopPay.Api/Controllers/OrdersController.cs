using Microsoft.AspNetCore.Mvc;
using ShopPay.Api.Contracts;
using ShopPay.Api.Services;

namespace ShopPay.Api.Controllers;

[ApiController]
[Route("orders")]
[Produces("application/json")]
public class OrdersController(OrderService orders) : ControllerBase
{
    /// <summary>Gets an order, including its current status (Pending, Paid or Cancelled).</summary>
    /// <param name="id">The order id returned by <c>POST /checkout</c>.</param>
    /// <response code="200">The order.</response>
    /// <response code="404">No order with that id.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await orders.GetOrderAsync(id, cancellationToken);

        if (order is null)
        {
            return Problem(
                title: "Order not found",
                detail: $"There is no order with id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(OrderResponse.From(order));
    }
}
