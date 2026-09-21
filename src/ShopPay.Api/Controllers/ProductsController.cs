using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShopPay.Api.Configuration;
using ShopPay.Api.Contracts;
using ShopPay.Api.Data;

namespace ShopPay.Api.Controllers;

[ApiController]
[Route("products")]
[Produces("application/json")]
public class ProductsController(ShopPayDbContext db, IOptions<StripeSettings> settings) : ControllerBase
{
    /// <summary>Lists every product in the catalogue.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProductResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> GetProducts(CancellationToken cancellationToken)
    {
        var currency = settings.Value.Currency;

        var products = await db.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return Ok(products.Select(p => ProductResponse.From(p, currency)).ToList());
    }
}
