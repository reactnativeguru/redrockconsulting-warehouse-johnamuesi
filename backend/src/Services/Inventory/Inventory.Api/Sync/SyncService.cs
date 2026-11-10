using Basecamp.Contracts;
using Inventory.Api.Data;
using Inventory.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Sync;

public class SyncService(InventoryDbContext db, IPublishEndpoint publisher, ILogger<SyncService> logger)
{
    public const int PullPageSize = 500;

    /// <summary>
    /// Apply each mutation independently. One bad mutation (unknown sku) must not block
    /// the rest of the device's queue - it's rejected and the device stops retrying it.
    /// </summary>
    public async Task<PushResponse> PushAsync(PushRequest request, CancellationToken ct)
    {
        var accepted = new List<Guid>();
        var rejected = new List<RejectedMutation>();

        foreach (var mutation in request.Mutations.OrderBy(m => m.OccurredAt))
        {
            var key = $"device-mutation:{mutation.MutationId}";

            // Already applied on a previous attempt (device lost the response) -> ack again.
            if (await db.ProcessedKeys.AnyAsync(k => k.Key == key, ct))
            {
                accepted.Add(mutation.MutationId);
                continue;
            }

            var item = await db.StockItems
                .SingleOrDefaultAsync(s => s.StoreId == request.StoreId && s.Sku == mutation.Sku, ct);
            if (item is null)
            {
                rejected.Add(new(mutation.MutationId, $"{mutation.Sku} is not ranged in {request.StoreId}"));
                continue;
            }

            try
            {
                Apply(item, mutation, request.DeviceId);
                if (item.IsLow)
                    // With the bus outbox enabled this is written to the OutboxMessage table
                    // and only sent if SaveChanges below succeeds.
                    await publisher.Publish(
                        new StockLevelLow(item.StoreId, item.Sku, item.Available, item.ReorderThreshold), ct);

                db.ProcessedKeys.Add(new ProcessedKey { Key = key });
                await db.SaveChangesAsync(ct);
                accepted.Add(mutation.MutationId);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone else changed this row in between (e.g. an ePOS batch).
                // Don't reject - leave it un-acked so the device retries on next sync.
                logger.LogInformation("Concurrency conflict on {Sku}; device will retry", mutation.Sku);
                db.ChangeTracker.Clear();
            }
            catch (ArgumentException ex)
            {
                rejected.Add(new(mutation.MutationId, ex.Message));
                db.ChangeTracker.Clear();
            }
        }

        return new PushResponse(accepted, rejected);
    }

    private void Apply(StockItem item, SyncMutation mutation, string deviceId)
    {
        switch (mutation.Type)
        {
            case MutationTypes.StockCount:
                var result = item.ApplyCount(mutation.Quantity, mutation.OccurredAt);
                if (result.WasApplied)
                    db.Movements.Add(new StockMovement
                    {
                        StoreId = item.StoreId, Sku = item.Sku, Type = MovementType.Count,
                        Quantity = result.Variance, Reason = $"Counted {mutation.Quantity}",
                        Source = $"device:{deviceId}", OccurredAt = mutation.OccurredAt
                    });
                break;

            case MutationTypes.StockAdjustment:
                item.Adjust(mutation.Quantity, mutation.Reason ?? "");
                db.Movements.Add(new StockMovement
                {
                    StoreId = item.StoreId, Sku = item.Sku, Type = MovementType.Adjustment,
                    Quantity = mutation.Quantity, Reason = mutation.Reason,
                    Source = $"device:{deviceId}", OccurredAt = mutation.OccurredAt
                });
                break;

            default:
                throw new ArgumentException($"Unknown mutation type '{mutation.Type}'");
        }
    }

    public async Task<PullResponse> PullAsync(string storeId, string? since, CancellationToken ct)
    {
        var checkpoint = ulong.TryParse(since, out var v) ? v : 0UL;

        var rows = await db.StockItems
            .AsNoTracking()
            .Where(s => s.StoreId == storeId && s.RowVersion > checkpoint)
            .OrderBy(s => s.RowVersion)
            .Take(PullPageSize + 1) // fetch one extra to know if there's another page
            .ToListAsync(ct);

        var hasMore = rows.Count > PullPageSize;
        var page = rows.Take(PullPageSize).ToList();
        var newCheckpoint = page.Count > 0 ? page[^1].RowVersion : checkpoint;

        return new PullResponse(page.Select(ToDto).ToList(), newCheckpoint.ToString(), hasMore);
    }

    public static StockDto ToDto(StockItem s) => new(
        s.Sku, s.OnHand, s.Reserved, s.Available, s.ReorderThreshold, s.IsLow, s.LastCountedAt,
        s.RowVersion.ToString());
}
