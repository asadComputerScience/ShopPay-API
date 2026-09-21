namespace ShopPay.Api.Domain;

/// <summary>
/// The caller sent something invalid (unknown product, bad quantity, ...). Maps to HTTP 400.
/// </summary>
public sealed class OrderValidationException : Exception
{
    public OrderValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public OrderValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    /// <summary>Field name -> list of problems, same shape as ASP.NET's own validation errors.</summary>
    public IDictionary<string, string[]> Errors { get; }
}

/// <summary>
/// The requested change is not allowed for the order's current state
/// (e.g. paying a cancelled order, or a payment whose amount does not match). Maps to HTTP 409.
/// </summary>
public sealed class OrderStateException(string message) : Exception(message);
