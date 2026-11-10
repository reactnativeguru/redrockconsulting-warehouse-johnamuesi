namespace Inventory.Api.Sync;

// ---------------------------------------------------------------------------
// Offline-first sync contract with the handheld app.
//
// PUSH: device sends queued mutations (things the user did while offline).
//       Each has a client-generated MutationId so retries are idempotent.
// PULL: device asks "what changed since checkpoint X?" and gets current rows.
//
// The checkpoint is a SQL Server rowversion, sent as a STRING: it's a 64-bit
// unsigned number and JavaScript numbers lose precision above 2^53.
// ---------------------------------------------------------------------------

public static class MutationTypes
{
    public const string StockCount = "StockCount";
    public const string StockAdjustment = "StockAdjustment";
}

public record SyncMutation(
    Guid MutationId,
    string Type,
    string Sku,
    int Quantity,
    string? Reason,
    DateTimeOffset OccurredAt);

public record PushRequest(string StoreId, string DeviceId, IReadOnlyList<SyncMutation> Mutations);

public record RejectedMutation(Guid MutationId, string Reason);

public record PushResponse(IReadOnlyList<Guid> Accepted, IReadOnlyList<RejectedMutation> Rejected);

public record StockDto(
    string Sku,
    int OnHand,
    int Reserved,
    int Available,
    int ReorderThreshold,
    bool IsLow,
    DateTimeOffset? LastCountedAt,
    string Version);

public record PullResponse(IReadOnlyList<StockDto> Items, string Checkpoint, bool HasMore);
