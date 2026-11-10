using Basecamp.Contracts;
using Inventory.Api.Data;
using Inventory.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Consumers;

/// <summary>
/// Reacts to a new click &amp; collect order by reserving stock in the chosen store.
///
/// Flow (choreography - no central coordinator):
///   Orders --OrderSubmitted--> Inventory --StockReserved / StockReservationFailed--> Orders
///
/// Reliability: this endpoint is configured with the EF outbox (see Program.cs), so
///   - context.Publish(...) is NOT sent immediately; it's stored in the OutboxMessage table
///   - SaveChangesAsync commits stock changes + outgoing messages in ONE transaction
///   - the InboxState table means a redelivered OrderSubmitted is skipped
/// Result: no "reserved stock but never told Orders" (or vice versa) inconsistencies.
/// </summary>
public class OrderSubmittedConsumer(InventoryDbContext db, ILogger<OrderSubmittedConsumer> logger)
    : IConsumer<OrderSubmitted>
{
    public async Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        var order = context.Message;
        var skus = order.Lines.Select(l => l.Sku).Distinct().ToList();

        var stock = await db.StockItems
            .Where(s => s.StoreId == order.StoreId && skus.Contains(s.Sku))
            .ToDictionaryAsync(s => s.Sku, context.CancellationToken);

        var outcome = ReservationPolicy.TryReserve(
            stock, order.Lines.Select(l => new ReservationPolicy.Line(l.Sku, l.Quantity)));

        if (outcome.Success)
        {
            foreach (var line in order.Lines)
            {
                db.Movements.Add(new StockMovement
                {
                    StoreId = order.StoreId, Sku = line.Sku, Type = MovementType.Reservation,
                    Quantity = 0, Reason = $"Reserved {line.Quantity}", Source = $"order:{order.OrderId}"
                });
            }

            await context.Publish(new StockReserved(order.OrderId, order.StoreId));
            logger.LogInformation("Reserved stock for order {OrderId}", order.OrderId);
        }
        else
        {
            await context.Publish(new StockReservationFailed(order.OrderId, order.StoreId, outcome.FailureReason!));
            logger.LogWarning("Could not reserve order {OrderId}: {Reason}", order.OrderId, outcome.FailureReason);
        }

        // If two orders race for the last unit, one SaveChanges throws
        // DbUpdateConcurrencyException (rowversion changed). MassTransit's retry policy
        // re-runs this consumer with a fresh DbContext, which re-reads current stock.
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
