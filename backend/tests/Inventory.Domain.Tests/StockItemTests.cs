using Inventory.Domain;

namespace Inventory.Domain.Tests;

// Naming convention: Method_Scenario_ExpectedResult. Arrange / Act / Assert.
public class StockItemTests
{
    private static StockItem NewItem(int onHand = 10, int threshold = 2) =>
        new("STORE-001", "BC-JKT-001", onHand, threshold);

    [Fact]
    public void Reserve_WhenEnoughAvailable_ReducesAvailableButNotOnHand()
    {
        var item = NewItem(onHand: 10);

        item.Reserve(3);

        Assert.Equal(10, item.OnHand);
        Assert.Equal(3, item.Reserved);
        Assert.Equal(7, item.Available);
    }

    [Fact]
    public void Reserve_MoreThanAvailable_Throws()
    {
        var item = NewItem(onHand: 2);

        var ex = Assert.Throws<InsufficientStockException>(() => item.Reserve(3));

        Assert.Equal(2, ex.Available);
        Assert.Equal(0, item.Reserved); // unchanged
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_NonPositiveQuantity_Throws(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewItem().Reserve(quantity));
    }

    [Fact]
    public void FulfilReservation_RemovesFromOnHandAndReserved()
    {
        var item = NewItem(onHand: 10);
        item.Reserve(4);

        item.FulfilReservation(4);

        Assert.Equal(6, item.OnHand);
        Assert.Equal(0, item.Reserved);
    }

    [Fact]
    public void RecordSale_CanGoNegative_BecauseTheTillIsTheSourceOfTruth()
    {
        var item = NewItem(onHand: 1);

        item.RecordSale(3);

        Assert.Equal(-2, item.OnHand);
    }

    [Fact]
    public void ApplyCount_SetsAbsoluteValueAndReportsVariance()
    {
        var item = NewItem(onHand: 10);

        var result = item.ApplyCount(7, DateTimeOffset.Parse("2026-11-09T10:00:00Z"));

        Assert.True(result.WasApplied);
        Assert.Equal(-3, result.Variance);
        Assert.Equal(7, item.OnHand);
    }

    [Fact]
    public void ApplyCount_OlderThanLastCount_IsIgnored()
    {
        // Device A counted at 12:00 and synced first.
        // Device B counted at 10:00 but was offline and syncs later. B must not win.
        var item = NewItem(onHand: 10);
        item.ApplyCount(5, DateTimeOffset.Parse("2026-11-09T12:00:00Z"));

        var late = item.ApplyCount(9, DateTimeOffset.Parse("2026-11-09T10:00:00Z"));

        Assert.False(late.WasApplied);
        Assert.Equal(5, item.OnHand);
    }

    [Fact]
    public void IsLow_WhenAvailableAtOrBelowThreshold()
    {
        var item = NewItem(onHand: 5, threshold: 2);
        Assert.False(item.IsLow);

        item.Reserve(3);

        Assert.True(item.IsLow);
    }

    [Fact]
    public void Adjust_RequiresAReason()
    {
        Assert.Throws<ArgumentException>(() => NewItem().Adjust(-1, " "));
    }
}
