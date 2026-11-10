using Basecamp.Contracts;
using Inventory.Api.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Consumers;

/// <summary>
/// Applies a batch of till sales (from the ePOS ETL) to store stock.
/// Positive quantity = sale, negative quantity = refund/return.
/// </summary>
public class SalesRecordedConsumer(InventoryDbContext db, ILogger<SalesRecordedConsumer> logger)
    : IConsumer<SalesRecorded>
{
    public async Task Consume(ConsumeContext<SalesRecorded> context)
    {
        var batch = context.Message;

        // Business idempotency: if the ETL re-sends a batch (e.g. it crashed after publishing
        // but before archiving the file) the BatchId is the same, so we skip it.
        var key = $"epos-batch:{batch.BatchId}";
        if (await db.ProcessedKeys.AnyAsync(k => k.Key == key, context.CancellationToken))
        {
            logger.LogInformation("Batch {BatchId} already applied - skipping", batch.BatchId);
            return;
        }

        var skus = batch.Lines.Select(l => l.Sku).Distinct().ToList();
        var stock = await db.StockItems
            .Where(s => s.StoreId == batch.StoreId && skus.Contains(s.Sku))
            .ToDictionaryAsync(s => s.Sku, context.CancellationToken);

        foreach (var line in batch.Lines)
        {
            if (!stock.TryGetValue(line.Sku, out var item))
            {
                // Don't fail the whole batch for one unknown sku - log it for the data team.
                logger.LogWarning("Sku {Sku} sold in {Store} but not ranged there", line.Sku, batch.StoreId);
                continue;
            }

            if (line.Quantity > 0)
                item.RecordSale(line.Quantity);
            else
                item.Adjust(-line.Quantity, "Till refund");

            db.Movements.Add(new StockMovement
            {
                StoreId = batch.StoreId, Sku = line.Sku,
                Type = line.Quantity > 0 ? MovementType.Sale : MovementType.Refund,
                Quantity = -line.Quantity, Source = $"epos:{line.TransactionId}", OccurredAt = line.SoldAt
            });
        }

        foreach (var item in stock.Values.Where(i => i.IsLow))
            await context.Publish(new StockLevelLow(item.StoreId, item.Sku, item.Available, item.ReorderThreshold));

        db.ProcessedKeys.Add(new ProcessedKey { Key = key });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
