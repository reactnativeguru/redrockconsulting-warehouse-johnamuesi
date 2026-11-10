using Inventory.Domain;

namespace Inventory.Domain.Tests;

public class ReservationPolicyTests
{
    private static Dictionary<string, StockItem> Stock(params (string Sku, int OnHand)[] items) =>
        items.ToDictionary(i => i.Sku, i => new StockItem("STORE-001", i.Sku, i.OnHand, 0));

    [Fact]
    public void TryReserve_AllLinesAvailable_ReservesEverything()
    {
        var stock = Stock(("A", 5), ("B", 5));

        var outcome = ReservationPolicy.TryReserve(stock, [new("A", 2), new("B", 1)]);

        Assert.True(outcome.Success);
        Assert.Equal(2, stock["A"].Reserved);
        Assert.Equal(1, stock["B"].Reserved);
    }

    [Fact]
    public void TryReserve_OneLineShort_ReservesNothing()
    {
        var stock = Stock(("A", 5), ("B", 0));

        var outcome = ReservationPolicy.TryReserve(stock, [new("A", 2), new("B", 1)]);

        Assert.False(outcome.Success);
        Assert.Contains("B", outcome.FailureReason);
        Assert.Equal(0, stock["A"].Reserved); // all-or-nothing
    }

    [Fact]
    public void TryReserve_DuplicateSkusAreSummed()
    {
        var stock = Stock(("A", 3));

        var outcome = ReservationPolicy.TryReserve(stock, [new("A", 2), new("A", 2)]);

        Assert.False(outcome.Success); // 4 requested in total, only 3 available
    }

    [Fact]
    public void TryReserve_UnknownSku_Fails()
    {
        var outcome = ReservationPolicy.TryReserve(Stock(("A", 3)), [new("Z", 1)]);

        Assert.False(outcome.Success);
        Assert.Contains("not ranged", outcome.FailureReason);
    }
}
