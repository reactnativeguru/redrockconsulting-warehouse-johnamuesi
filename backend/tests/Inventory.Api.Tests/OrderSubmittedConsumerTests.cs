using Basecamp.Contracts;
using Inventory.Api.Consumers;
using Inventory.Api.Data;
using Inventory.Domain;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests;

/// <summary>
/// Consumer tests using MassTransit's in-memory test harness: a real bus, real
/// consumer pipeline, no RabbitMQ. EF Core InMemory stands in for SQL Server.
/// (For full fidelity, use Testcontainers to spin up real SQL Server + RabbitMQ.)
/// </summary>
public class OrderSubmittedConsumerTests : IAsyncLifetime
{
    private ServiceProvider _provider = default!;
    private ITestHarness _harness = default!;

    public async Task InitializeAsync()
    {
        // Name the database ONCE: the AddDbContext options callback runs per DI scope,
        // so generating the name inside it would give the seed and the consumer
        // separate (empty) databases.
        var databaseName = Guid.NewGuid().ToString();

        _provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<InventoryDbContext>(o => o.UseInMemoryDatabase(databaseName))
            .AddMassTransitTestHarness(x => x.AddConsumer<OrderSubmittedConsumer>())
            .BuildServiceProvider(validateScopes: true);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            db.StockItems.Add(new StockItem("STORE-001", "BC-JKT-001", onHand: 5, reorderThreshold: 1));
            db.StockItems.Add(new StockItem("STORE-001", "BC-HAT-001", onHand: 0, reorderThreshold: 1));
            await db.SaveChangesAsync();
        }

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Publishes_StockReserved_and_reserves_when_stock_available()
    {
        var orderId = Guid.NewGuid();

        await _harness.Bus.Publish(new OrderSubmitted(orderId, "STORE-001",
            [new OrderLine("BC-JKT-001", 2)], DateTimeOffset.UtcNow));

        Assert.True(await _harness.Consumed.Any<OrderSubmitted>());
        Assert.True(await _harness.Published.Any<StockReserved>(m => m.Context.Message.OrderId == orderId));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var item = await db.StockItems.SingleAsync(s => s.Sku == "BC-JKT-001");
        Assert.Equal(2, item.Reserved);
    }

    [Fact]
    public async Task Publishes_StockReservationFailed_when_any_line_is_short()
    {
        var orderId = Guid.NewGuid();

        await _harness.Bus.Publish(new OrderSubmitted(orderId, "STORE-001",
            [new OrderLine("BC-JKT-001", 1), new OrderLine("BC-HAT-001", 1)], DateTimeOffset.UtcNow));

        Assert.True(await _harness.Published.Any<StockReservationFailed>(m => m.Context.Message.OrderId == orderId));
        Assert.False(await _harness.Published.Any<StockReserved>());
    }
}
