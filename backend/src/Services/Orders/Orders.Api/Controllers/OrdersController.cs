using Basecamp.Contracts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Catalog;
using Orders.Api.Data;
using Orders.Api.Domain;
using ContractLine = Basecamp.Contracts.OrderLine;

namespace Orders.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(OrdersDbContext db, IPublishEndpoint publisher, CatalogClient catalog) : ControllerBase
{
    public record PlaceOrderLine(string Sku, int Quantity);
    public record PlaceOrderRequest(string StoreId, string CustomerName, string CustomerEmail, List<PlaceOrderLine> Lines);

    public record OrderLineDto(string Sku, string ProductName, int Quantity, decimal UnitPrice);
    public record OrderDto(Guid Id, string StoreId, string CustomerName, string Status, string? StatusReason,
        decimal Total, DateTimeOffset CreatedAt, IReadOnlyList<OrderLineDto> Lines);

    /// <summary>
    /// Place a click &amp; collect order. Returns 202 Accepted: the order exists, but
    /// stock reservation happens asynchronously. Clients poll GET /api/orders/{id}
    /// (or in a fuller system, get a push notification).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        if (request.Lines is not { Count: > 0 })
            return ValidationProblem("At least one line is required");
        if (request.Lines.Any(l => l.Quantity <= 0))
            return ValidationProblem("Quantities must be positive");

        var products = await catalog.GetProductsAsync(request.Lines.Select(l => l.Sku), ct);
        var unknown = request.Lines.Where(l => !products.ContainsKey(l.Sku)).Select(l => l.Sku).ToList();
        if (unknown.Count > 0)
            return ValidationProblem($"Unknown products: {string.Join(", ", unknown)}");

        var order = new Order(request.StoreId, request.CustomerName, request.CustomerEmail,
            request.Lines.Select(l => new Domain.OrderLine(l.Sku, products[l.Sku].Name, l.Quantity, products[l.Sku].Price)));

        db.Orders.Add(order);

        // Goes to the outbox table; committed atomically with the order row below.
        await publisher.Publish(new OrderSubmitted(
            order.Id, order.StoreId,
            order.Lines.Select(l => new ContractLine(l.Sku, l.Quantity)).ToList(),
            order.CreatedAt), ct);

        await db.SaveChangesAsync(ct);

        return AcceptedAtAction(nameof(Get), new { id = order.Id }, ToDto(order));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? NotFound() : ToDto(order);
    }

    /// <summary>Pick list for a store, e.g. ?storeId=STORE-001&amp;status=ReadyToPick</summary>
    [HttpGet]
    public async Task<IEnumerable<OrderDto>> List([FromQuery] string storeId, [FromQuery] OrderStatus? status, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.StoreId == storeId);
        if (status is not null) query = query.Where(o => o.Status == status);
        var orders = await query.OrderBy(o => o.CreatedAt).Take(200).ToListAsync(ct);
        return orders.Select(ToDto);
    }

    /// <summary>
    /// Called from the handheld (possibly replayed from its offline queue), so it is
    /// idempotent: picking an already-picked order returns 200 without side effects.
    /// </summary>
    [HttpPost("{id:guid}/picked")]
    public async Task<ActionResult<OrderDto>> Picked(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();

        try
        {
            if (order.MarkPicked())
            {
                await publisher.Publish(new OrderPicked(order.Id, order.StoreId,
                    order.Lines.Select(l => new ContractLine(l.Sku, l.Quantity)).ToList()), ct);
                await db.SaveChangesAsync(ct);
            }
        }
        catch (InvalidOrderStateException ex)
        {
            return Conflict(new ProblemDetails { Title = "Invalid order state", Detail = ex.Message });
        }

        return ToDto(order);
    }

    [HttpPost("{id:guid}/collected")]
    public async Task<ActionResult<OrderDto>> Collected(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();
        try
        {
            order.MarkCollected();
            await db.SaveChangesAsync(ct);
        }
        catch (InvalidOrderStateException ex)
        {
            return Conflict(new ProblemDetails { Title = "Invalid order state", Detail = ex.Message });
        }
        return ToDto(order);
    }

    private static OrderDto ToDto(Order o) => new(
        o.Id, o.StoreId, o.CustomerName, o.Status.ToString(), o.StatusReason, o.Total, o.CreatedAt,
        o.Lines.Select(l => new OrderLineDto(l.Sku, l.ProductName, l.Quantity, l.UnitPrice)).ToList());
}
