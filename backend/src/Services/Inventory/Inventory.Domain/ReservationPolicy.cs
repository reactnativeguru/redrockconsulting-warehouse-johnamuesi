namespace Inventory.Domain;

/// <summary>
/// Domain service: reserves a whole order across several StockItems, all-or-nothing.
/// Lives in the domain (not the consumer) so it can be unit-tested without a bus or DB.
/// </summary>
public static class ReservationPolicy
{
    public sealed record Line(string Sku, int Quantity);

    public sealed record Outcome(bool Success, string? FailureReason)
    {
        public static Outcome Ok() => new(true, null);
        public static Outcome Fail(string reason) => new(false, reason);
    }

    public static Outcome TryReserve(IReadOnlyDictionary<string, StockItem> stockBySku, IEnumerable<Line> lines)
    {
        // Merge duplicate skus (same product on two lines) before checking.
        var demand = lines
            .GroupBy(l => l.Sku)
            .Select(g => new Line(g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        // Validate EVERYTHING first; only mutate when we know all lines succeed.
        foreach (var line in demand)
        {
            if (!stockBySku.TryGetValue(line.Sku, out var item))
                return Outcome.Fail($"{line.Sku} is not ranged in this store");
            if (item.Available < line.Quantity)
                return Outcome.Fail($"{line.Sku}: {item.Available} available, {line.Quantity} requested");
        }

        foreach (var line in demand)
            stockBySku[line.Sku].Reserve(line.Quantity);

        return Outcome.Ok();
    }
}
