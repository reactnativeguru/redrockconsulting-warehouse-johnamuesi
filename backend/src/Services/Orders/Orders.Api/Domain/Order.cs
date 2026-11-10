namespace Orders.Api.Domain;

/// <summary>
/// Click &amp; collect order lifecycle:
///
///   Submitted --(StockReserved)--------> ReadyToPick --(picked on handheld)--> ReadyForCollection --> Collected
///       \----(StockReservationFailed)--> Rejected
///
/// State transitions are methods that guard the current state, so an invalid
/// transition (e.g. collecting an order that was never picked) is impossible.
/// </summary>
public class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order() { } // EF

    public Order(string storeId, string customerName, string customerEmail, IEnumerable<OrderLine> lines)
    {
        Id = Guid.NewGuid();
        StoreId = storeId;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        _lines.AddRange(lines);
        if (_lines.Count == 0) throw new ArgumentException("An order needs at least one line");
        Status = OrderStatus.Submitted;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string StoreId { get; private set; } = default!;
    public string CustomerName { get; private set; } = default!;
    public string CustomerEmail { get; private set; } = default!;
    public OrderStatus Status { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;
    public decimal Total => _lines.Sum(l => l.UnitPrice * l.Quantity);

    public void MarkReadyToPick() => Transition(OrderStatus.Submitted, OrderStatus.ReadyToPick);

    public void Reject(string reason)
    {
        Transition(OrderStatus.Submitted, OrderStatus.Rejected);
        StatusReason = reason;
    }

    /// <summary>Returns false if already picked (idempotent retry from a handheld).</summary>
    public bool MarkPicked()
    {
        if (Status == OrderStatus.ReadyForCollection) return false;
        Transition(OrderStatus.ReadyToPick, OrderStatus.ReadyForCollection);
        return true;
    }

    public void MarkCollected() => Transition(OrderStatus.ReadyForCollection, OrderStatus.Collected);

    private void Transition(OrderStatus from, OrderStatus to)
    {
        if (Status != from)
            throw new InvalidOrderStateException(Id, Status, to);
        Status = to;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public class OrderLine
{
    private OrderLine() { } // EF

    public OrderLine(string sku, string productName, int quantity, decimal unitPrice)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        Sku = sku;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public string Sku { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    /// <summary>Price captured at order time - later price changes don't affect this order.</summary>
    public decimal UnitPrice { get; private set; }
}

public enum OrderStatus
{
    Submitted,
    ReadyToPick,
    Rejected,
    ReadyForCollection,
    Collected
}

public class InvalidOrderStateException(Guid orderId, OrderStatus current, OrderStatus attempted)
    : Exception($"Order {orderId} is {current}; cannot move to {attempted}");
