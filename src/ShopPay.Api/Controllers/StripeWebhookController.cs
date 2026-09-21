using Microsoft.AspNetCore.Mvc;
using ShopPay.Api.Contracts;
using ShopPay.Api.Services;

namespace ShopPay.Api.Controllers;

[ApiController]
[Route("webhooks/stripe")]
[Produces("application/json")]
public class StripeWebhookController(StripeWebhookHandler handler) : ControllerBase
{
    /// <summary>
    /// Receives events from Stripe. Called by Stripe (or the Stripe CLI), not by browsers.
    /// </summary>
    /// <remarks>
    /// The <c>Stripe-Signature</c> header is verified against the webhook secret before anything else happens.
    /// On <c>checkout.session.completed</c> the matching order is marked <c>Paid</c>.
    /// This endpoint cannot be tried from Swagger because a valid signature can only be produced by Stripe.
    /// </remarks>
    /// <response code="200">Event accepted (including events we deliberately ignore).</response>
    /// <response code="400">Missing or invalid signature.</response>
    /// <response code="503">The webhook secret is not configured on the server.</response>
    [HttpPost]
    [ProducesResponseType<WebhookAckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<WebhookAckResponse>> Receive(CancellationToken cancellationToken)
    {
        // The signature is computed over the exact bytes Stripe sent, so read the raw body ourselves
        // instead of letting ASP.NET parse it into an object first.
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        var signature = Request.Headers["Stripe-Signature"].ToString();

        var outcome = await handler.HandleAsync(payload, signature, cancellationToken);

        if (outcome == WebhookOutcome.InvalidSignature)
        {
            return Problem(
                title: "Invalid webhook signature",
                detail: "The Stripe-Signature header is missing or does not match.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(new WebhookAckResponse(true));
    }
}
