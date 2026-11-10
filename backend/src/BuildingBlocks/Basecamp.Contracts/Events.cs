namespace Basecamp.Contracts;

// ---------------------------------------------------------------------------
// Integration events. Past tense = "something happened" (an event, not a command).
// MassTransit uses the full type name (Basecamp.Contracts:OrderSubmitted) as the
// RabbitMQ exchange name, so NEVER rename/move these once in production -
// add new properties instead (consumers ignore properties they don't know).
// ---------------------------------------------------------------------------

/// <summary>A line on an order or sale.</summary>
public record OrderLine(string Sku, int Quantity);

/// <summary>Published by Orders when a customer places a click &amp; collect order.</summary>
public record OrderSubmitted(
    Guid OrderId,
    string StoreId,
    IReadOnlyList<OrderLine> Lines,
    DateTimeOffset SubmittedAt);

/// <summary>Published by Inventory when every line on the order was reserved.</summary>
public record StockReserved(Guid OrderId, string StoreId);

/// <summary>Published by Inventory when the order could not be reserved (nothing is reserved).</summary>
public record StockReservationFailed(Guid OrderId, string StoreId, string Reason);

/// <summary>Published by Orders when a store colleague has picked the order on a handheld.</summary>
public record OrderPicked(Guid OrderId, string StoreId, IReadOnlyList<OrderLine> Lines);

/// <summary>Published by the ePOS ETL worker for each batch of till sales imported.</summary>
public record SalesRecorded(
    string StoreId,
    string BatchId,
    IReadOnlyList<SaleLine> Lines,
    DateTimeOffset RecordedAt);

public record SaleLine(string TransactionId, string Sku, int Quantity, decimal UnitPrice, DateTimeOffset SoldAt);

/// <summary>Published by Catalog when head office changes a price. ETL exports these to the ePOS.</summary>
public record ProductPriceChanged(string Sku, decimal OldPrice, decimal NewPrice, DateTimeOffset ChangedAt);

/// <summary>Published by Inventory when available stock drops to/below the reorder threshold.</summary>
public record StockLevelLow(string StoreId, string Sku, int Available, int Threshold);
