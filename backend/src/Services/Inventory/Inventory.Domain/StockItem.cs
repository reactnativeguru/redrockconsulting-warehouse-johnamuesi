namespace Inventory.Domain;

/// <summary>
/// Aggregate root: the stock position of ONE sku in ONE store.
///
/// All changes to stock go through methods on this class, so the invariants
/// ("you can't reserve more than is available", "a stale count can't overwrite
/// a newer one") live in exactly one place. Properties have private setters -
/// nothing outside can put the object into an invalid state.
/// </summary>
public sealed class StockItem
{
    // EF Core needs a parameterless constructor; private keeps it out of our code.
    private StockItem() { }

    public StockItem(string storeId, string sku, int onHand, int reorderThreshold)
    {
        if (string.IsNullOrWhiteSpace(storeId)) throw new ArgumentException("Store is required", nameof(storeId));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("Sku is required", nameof(sku));
        if (reorderThreshold < 0) throw new ArgumentOutOfRangeException(nameof(reorderThreshold));

        Id = Guid.NewGuid();
        StoreId = storeId;
        Sku = sku;
        OnHand = onHand;
        ReorderThreshold = reorderThreshold;
    }

    public Guid Id { get; private set; }
    public string StoreId { get; private set; } = default!;
    public string Sku { get; private set; } = default!;

    /// <summary>Physical units in the store (what the shelf + stockroom hold).</summary>
    public int OnHand { get; private set; }

    /// <summary>Units promised to click &amp; collect orders but not yet picked.</summary>
    public int Reserved { get; private set; }

    /// <summary>What can still be sold or promised.</summary>
    public int Available => OnHand - Reserved;

    public int ReorderThreshold { get; private set; }

    public DateTimeOffset? LastCountedAt { get; private set; }

    /// <summary>
    /// SQL Server rowversion. Two jobs:
    ///  1. Optimistic concurrency - EF adds "WHERE RowVersion = @old" to UPDATEs.
    ///  2. Change tracking for device sync - "give me rows changed since version X".
    /// </summary>
    public ulong RowVersion { get; private set; }

    public bool IsLow => Available <= ReorderThreshold;

    /// <summary>Reserve units for a click &amp; collect order.</summary>
    public void Reserve(int quantity)
    {
        GuardPositive(quantity);
        if (quantity > Available)
            throw new InsufficientStockException(StoreId, Sku, quantity, Available);
        Reserved += quantity;
    }

    /// <summary>Order cancelled - give the units back.</summary>
    public void ReleaseReservation(int quantity)
    {
        GuardPositive(quantity);
        Reserved = Math.Max(0, Reserved - quantity);
    }

    /// <summary>Order picked from the shelf: the units physically leave stock.</summary>
    public void FulfilReservation(int quantity)
    {
        GuardPositive(quantity);
        Reserved = Math.Max(0, Reserved - quantity);
        OnHand -= quantity;
    }

    /// <summary>
    /// A till sale imported from the ePOS.
    /// Deliberately NOT validated against stock: the sale already happened in the
    /// real world, the till is the source of truth. If this takes OnHand negative,
    /// that's a signal our stock record was wrong ("phantom stock") and the store
    /// needs a count - not a reason to reject the sale.
    /// </summary>
    public void RecordSale(int quantity)
    {
        GuardPositive(quantity);
        OnHand -= quantity;
    }

    /// <summary>Returns, damages, deliveries... relative change with a reason.</summary>
    public void Adjust(int delta, string reason)
    {
        if (delta == 0) throw new ArgumentException("Adjustment must be non-zero", nameof(delta));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required", nameof(reason));
        OnHand += delta;
    }

    /// <summary>
    /// Apply a physical stock count from a handheld (may arrive late - device was offline).
    ///
    /// Conflict rule: a count is an ABSOLUTE value at a point in time, so we use
    /// "last counted wins" by the time the count was TAKEN (not when it arrived).
    /// A count taken at 10:00 that syncs at 15:00 must not overwrite a 12:00 count.
    /// </summary>
    public CountResult ApplyCount(int countedQuantity, DateTimeOffset countedAt)
    {
        if (countedQuantity < 0) throw new ArgumentOutOfRangeException(nameof(countedQuantity));

        if (LastCountedAt is { } last && countedAt <= last)
            return CountResult.Stale(OnHand);

        var variance = countedQuantity - OnHand;
        OnHand = countedQuantity;
        LastCountedAt = countedAt;
        return CountResult.Applied(variance, OnHand);
    }

    private static void GuardPositive(int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive");
    }
}

/// <summary>Outcome of applying a stock count.</summary>
public readonly record struct CountResult(bool WasApplied, int Variance, int OnHand)
{
    public static CountResult Applied(int variance, int onHand) => new(true, variance, onHand);
    public static CountResult Stale(int onHand) => new(false, 0, onHand);
}

public sealed class InsufficientStockException(string storeId, string sku, int requested, int available)
    : Exception($"Store {storeId} has {available} of {sku} available, {requested} requested")
{
    public string StoreId { get; } = storeId;
    public string Sku { get; } = sku;
    public int Requested { get; } = requested;
    public int Available { get; } = available;
}
