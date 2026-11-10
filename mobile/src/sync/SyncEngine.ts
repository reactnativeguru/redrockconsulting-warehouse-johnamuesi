import { ApiError, NetworkError } from '../api/http';
import type { RetailApi } from '../api/retailApi';
import type { SyncMutationDto } from '../api/types';
import type { LocalStore, PendingMutation } from '../data/LocalStore';

export const CHECKPOINT_KEY = 'inventory.checkpoint';
export const LAST_SYNC_KEY = 'lastSyncAt';

export interface SyncResult {
  pushed: number;
  rejected: number;
  pulled: number;
  /** Set when the sync stopped early (e.g. went offline). Queue is untouched - safe to retry. */
  error?: string;
}

export interface SyncEngineOptions {
  store: LocalStore;
  api: RetailApi;
  storeId: string;
  deviceId: string;
  batchSize?: number;
}

/**
 * Offline-first sync: PUSH local changes, then PULL server changes.
 *
 * Why push first? So the pull reflects our own changes (server has applied them),
 * and we never overwrite an optimistic local value with stale server data.
 *
 * Guarantees:
 *  - At-least-once delivery: a mutation leaves the outbox only when the server ACKs it.
 *  - Idempotent: the server dedupes by mutation id, so re-sending after a lost
 *    response is harmless.
 *  - Single-flight: concurrent sync() calls share one run (no double-sends).
 *  - Poison messages don't block the queue: server-rejected mutations are parked
 *    as 'rejected' and shown to the user.
 */
export class SyncEngine {
  private inFlight: Promise<SyncResult> | null = null;
  private readonly batchSize: number;

  constructor(private readonly opts: SyncEngineOptions) {
    this.batchSize = opts.batchSize ?? 100;
  }

  sync(): Promise<SyncResult> {
    if (!this.inFlight) {
      this.inFlight = this.run().finally(() => {
        this.inFlight = null;
      });
    }
    return this.inFlight;
  }

  private async run(): Promise<SyncResult> {
    const result: SyncResult = { pushed: 0, rejected: 0, pulled: 0 };
    try {
      await this.push(result);
      await this.pull(result);
      await this.opts.store.setMeta(LAST_SYNC_KEY, new Date().toISOString());
    } catch (e) {
      // Network down or server 5xx: stop here, keep everything queued, try again later.
      result.error = e instanceof Error ? e.message : String(e);
    }
    return result;
  }

  private async push(result: SyncResult): Promise<void> {
    const { store, api, storeId, deviceId } = this.opts;

    // Drain the queue in batches.
    for (;;) {
      const pending = await store.getPending(this.batchSize);
      if (pending.length === 0) return;

      const inventory = pending.filter((m) => m.kind === 'StockCount' || m.kind === 'StockAdjustment');
      const picks = pending.filter((m) => m.kind === 'OrderPicked');

      if (inventory.length > 0) {
        const response = await api.pushInventory({
          storeId,
          deviceId,
          mutations: inventory.map(toDto),
        });
        await store.remove(response.accepted);
        for (const r of response.rejected) await store.markRejected(r.mutationId, r.reason);
        result.pushed += response.accepted.length;
        result.rejected += response.rejected.length;

        // Anything neither accepted nor rejected (server concurrency conflict) stays
        // pending. Stop this round to avoid a hot loop; next sync retries it.
        const handled = response.accepted.length + response.rejected.length;
        if (handled < inventory.length) return;
      }

      for (const pick of picks) {
        try {
          await api.markOrderPicked(pick.orderId!, pick.id);
          await store.remove([pick.id]);
          result.pushed++;
        } catch (e) {
          if (e instanceof ApiError && e.isClientError) {
            // e.g. 409 order was cancelled - retrying won't help.
            await store.markRejected(pick.id, e.message || `HTTP ${e.status}`);
            result.rejected++;
          } else {
            throw e; // NetworkError / 5xx -> abort, retry later
          }
        }
      }

      if (pending.length < this.batchSize) return;
    }
  }

  private async pull(result: SyncResult): Promise<void> {
    const { store, api, storeId } = this.opts;

    // Rows with un-synced local changes keep their optimistic local value
    // ("local wins until the server acknowledges").
    const pendingSkus = new Set(
      (await store.getPending(10_000)).filter((m) => m.sku).map((m) => m.sku as string),
    );

    let hasMore = true;
    while (hasMore) {
      const since = (await store.getMeta(CHECKPOINT_KEY)) ?? '0';
      const page = await api.pullInventory(storeId, since);
      const rows = page.items.filter((r) => !pendingSkus.has(r.sku));
      await store.upsertStock(rows);
      // Save the checkpoint only after the rows are stored - a crash in between
      // just means we re-download the same page next time.
      await store.setMeta(CHECKPOINT_KEY, page.checkpoint);
      result.pulled += page.items.length;
      hasMore = page.hasMore;
    }
  }
}

function toDto(m: PendingMutation): SyncMutationDto {
  return {
    mutationId: m.id,
    type: m.kind as SyncMutationDto['type'],
    sku: m.sku!,
    quantity: m.quantity!,
    reason: m.reason,
    occurredAt: m.occurredAt,
  };
}

export { NetworkError };
