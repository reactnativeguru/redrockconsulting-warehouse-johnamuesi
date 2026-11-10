using Basecamp.Contracts;
using MassTransit;
using Orders.Api.Data;
using Orders.Api.Domain;

namespace Orders.Api.Consumers;

/// <summary>Inventory reserved the stock: the order can now be picked in store.</summary>
public class StockReservedConsumer(OrdersDbContext db, ILogger<StockReservedConsumer> logger)
    : IConsumer<StockReserved>
{
    public async Task Consume(ConsumeContext<StockReserved> context)
    {
        var order = await db.Orders.FindAsync([context.Message.OrderId], context.CancellationToken);
        if (order is null)
        {
            logger.LogWarning("StockReserved for unknown order {OrderId}", context.Message.OrderId);
            return;
        }
        if (order.Status != OrderStatus.Submitted) return; // already handled (idempotent)

        order.MarkReadyToPick();
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>Inventory couldn't reserve: reject (a real system would email the customer).</summary>
public class StockReservationFailedConsumer(OrdersDbContext db) : IConsumer<StockReservationFailed>
{
    public async Task Consume(ConsumeContext<StockReservationFailed> context)
    {
        var order = await db.Orders.FindAsync([context.Message.OrderId], context.CancellationToken);
        if (order is null || order.Status != OrderStatus.Submitted) return;

        order.Reject(context.Message.Reason);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
