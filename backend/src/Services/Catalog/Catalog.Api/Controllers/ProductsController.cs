using Basecamp.Contracts;
using Catalog.Api.Data;
using Catalog.Api.Models;
using MassTransit;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

/// <summary>
/// RESTful product resource. Conventions used throughout:
///   GET    /api/products              list (filter + paging via query string)
///   GET    /api/products/{sku}        one resource, 404 if missing
///   PUT    /api/products/{sku}        create-or-replace (idempotent)
///   PUT    /api/products/{sku}/price  replace a sub-resource (idempotent)
/// </summary>
[ApiController]
[Route("api/products")]
public class ProductsController(IProductRepository products, IPublishEndpoint publisher) : ControllerBase
{
    public record ProductSummary(string Sku, string Name, string Category, decimal Price, string Barcode);
    public record LookupResult(string Sku, string Name, decimal Price);
    public record PriceChangeRequest(decimal Price);

    [HttpGet]
    public async Task<PagedResult<ProductSummary>> Search(
        [FromQuery] string? search, [FromQuery] string? category,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await products.SearchAsync(search, category, page, pageSize, ct);
        return new PagedResult<ProductSummary>(
            result.Items.Select(p => new ProductSummary(p.Sku, p.Name, p.Category, p.Price, p.Barcode)).ToList(),
            result.Total, result.Page, result.PageSize);
    }

    [HttpGet("categories")]
    public Task<IReadOnlyList<string>> Categories(CancellationToken ct) => products.GetCategoriesAsync(ct);

    [HttpGet("{sku}")]
    public async Task<ActionResult<Product>> Get(string sku, CancellationToken ct)
        => await products.GetAsync(sku, ct) is { } p ? p : NotFound();

    /// <summary>Handheld scans a barcode -> product.</summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<ActionResult<Product>> GetByBarcode(string barcode, CancellationToken ct)
        => await products.GetByBarcodeAsync(barcode, ct) is { } p ? p : NotFound();

    /// <summary>Batch lookup used service-to-service by Orders: ?skus=A,B,C</summary>
    [HttpGet("lookup")]
    public async Task<IEnumerable<LookupResult>> Lookup([FromQuery] string skus, CancellationToken ct)
    {
        var list = skus.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var found = await products.GetManyAsync(list, ct);
        return found.Select(p => new LookupResult(p.Sku, p.Name, p.Price));
    }

    [HttpPut("{sku}")]
    public async Task<IActionResult> Upsert(string sku, Product product, CancellationToken ct)
    {
        if (sku != product.Sku) return BadRequest("Sku in URL and body must match");
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await products.UpsertAsync(product, ct);
        return NoContent();
    }

    /// <summary>
    /// Change a price and broadcast ProductPriceChanged so the ETL can push it to the tills.
    ///
    /// Known trade-off (the "dual write" problem): we write to Mongo, THEN publish.
    /// If the process dies between the two, the tills never get the new price.
    /// Inventory/Orders avoid this with the EF transactional outbox. The equivalent
    /// here is MassTransit's MongoDB outbox (needs a replica set for transactions) -
    /// see docs/05-messaging-rabbitmq-masstransit.md. Kept simple on purpose.
    /// </summary>
    [HttpPut("{sku}/price")]
    public async Task<IActionResult> ChangePrice(string sku, PriceChangeRequest request, CancellationToken ct)
    {
        if (request.Price <= 0 || decimal.Round(request.Price, 2) != request.Price)
            return ValidationProblem("Price must be positive with at most 2 decimal places");

        var before = await products.UpdatePriceAsync(sku, request.Price, ct);
        if (before is null) return NotFound();

        if (before.Price != request.Price)
            await publisher.Publish(new ProductPriceChanged(sku, before.Price, request.Price, DateTimeOffset.UtcNow), ct);

        return NoContent();
    }
}
