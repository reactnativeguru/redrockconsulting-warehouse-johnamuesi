using Inventory.Api.Data;
using Inventory.Api.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// REST resources for the store admin portal (micro-frontend) and reporting.
/// Resource-oriented URLs: /stores/{storeId}/stock/{sku}
/// </summary>
[ApiController]
[Route("api/inventory/stores")]
public class StockController(InventoryDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<string>> GetStores(CancellationToken ct)
        => await db.StockItems.Select(s => s.StoreId).Distinct().OrderBy(s => s).ToListAsync(ct);

    [HttpGet("{storeId}/stock")]
    public async Task<IEnumerable<StockDto>> GetStock(string storeId, [FromQuery] bool lowOnly, CancellationToken ct)
    {
        var items = await db.StockItems.AsNoTracking()
            .Where(s => s.StoreId == storeId)
            .OrderBy(s => s.Sku)
            .ToListAsync(ct);

        // IsLow is computed in C# (not a column), so filter after materialising.
        // Fine for ~thousands of rows per store; for more, add a computed column.
        return items.Where(i => !lowOnly || i.IsLow).Select(SyncService.ToDto);
    }

    [HttpGet("{storeId}/stock/{sku}")]
    public async Task<ActionResult<StockDto>> GetStockItem(string storeId, string sku, CancellationToken ct)
    {
        var item = await db.StockItems.AsNoTracking()
            .SingleOrDefaultAsync(s => s.StoreId == storeId && s.Sku == sku, ct);
        return item is null ? NotFound() : SyncService.ToDto(item);
    }

    public record AdjustmentRequest(int Delta, string Reason);

    [HttpPost("{storeId}/stock/{sku}/adjustments")]
    public async Task<ActionResult<StockDto>> Adjust(string storeId, string sku, AdjustmentRequest request, CancellationToken ct)
    {
        var item = await db.StockItems.SingleOrDefaultAsync(s => s.StoreId == storeId && s.Sku == sku, ct);
        if (item is null) return NotFound();

        try
        {
            item.Adjust(request.Delta, request.Reason);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(ex.Message);
        }

        db.Movements.Add(new StockMovement
        {
            StoreId = storeId, Sku = sku, Type = MovementType.Adjustment,
            Quantity = request.Delta, Reason = request.Reason, Source = "admin-portal"
        });
        await db.SaveChangesAsync(ct);
        return SyncService.ToDto(item);
    }

    /// <summary>Reporting: recent stock movements for a store (audit trail).</summary>
    [HttpGet("{storeId}/movements")]
    public async Task<IEnumerable<StockMovement>> GetMovements(string storeId, [FromQuery] int take = 50, CancellationToken ct = default)
        => await db.Movements.AsNoTracking()
            .Where(m => m.StoreId == storeId)
            .OrderByDescending(m => m.OccurredAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);
}
