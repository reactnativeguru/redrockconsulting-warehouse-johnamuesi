using Basecamp.Contracts;
using Inventory.Api.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Consumers;

/// <summary>Order picked on the shop floor: the reserved units physically leave stock.</summary>
public class OrderPickedConsumer(InventoryDbContext db) : IConsumer<OrderPicked>
{
    public async Task Consume(ConsumeContext<OrderPicked> context)
    {
        var msg = context.Message;
        var skus = msg.Lines.Select(l => l.Sku).Distinct().ToList();
        var stock = await db.StockItems
            .Where(s => s.StoreId == msg.StoreId && skus.Contains(s.Sku))
            .ToDictionaryAsync(s => s.Sku, context.CancellationToken);

        foreach (var line in msg.Lines)
        {
            if (!stock.TryGetValue(line.Sku, out var item)) continue;
            item.FulfilReservation(line.Quantity);
            db.Movements.Add(new StockMovement
            {
                StoreId = msg.StoreId, Sku = line.Sku, Type = MovementType.Fulfilment,
                Quantity = -line.Quantity, Source = $"order:{msg.OrderId}"
            });
            if (item.IsLow)
                await context.Publish(new StockLevelLow(item.StoreId, item.Sku, item.Available, item.ReorderThreshold));
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
