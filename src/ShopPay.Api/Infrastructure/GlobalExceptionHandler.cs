using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ShopPay.Api.Domain;
using ShopPay.Api.Payments;

namespace ShopPay.Api.Infrastructure;

/// <summary>
/// One place that turns exceptions into consistent JSON error responses (RFC 7807 "problem details").
/// Expected problems get a helpful message; anything unexpected is logged and returned as a bland 500
/// so internal details never leak to clients.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The client hung up; nobody is waiting for a response.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var problem = exception switch
        {
            OrderValidationException ex => new ValidationProblemDetails(ex.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            OrderStateException ex => Create(StatusCodes.Status409Conflict, "Conflict with the order's current state", ex.Message),
            PaymentConfigurationException ex => Create(StatusCodes.Status503ServiceUnavailable, "Payments are not available", ex.Message),
            PaymentGatewayException ex => Create(StatusCodes.Status502BadGateway, "Payment provider error", ex.Message),
            _ => Create(StatusCodes.Status500InternalServerError, "An unexpected error occurred", "Something went wrong on our side. Please try again later."),
        };

        problem.Instance = httpContext.Request.Path;

        if (problem.Status >= StatusCodes.Status500InternalServerError && exception is not PaymentConfigurationException)
        {
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning("Request {Method} {Path} failed with {Status}: {Message}",
                httpContext.Request.Method, httpContext.Request.Path, problem.Status, exception.Message);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Create(int status, string title, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };
}
