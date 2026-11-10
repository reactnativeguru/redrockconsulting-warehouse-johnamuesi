import { ApiError, NetworkError } from '../src/api/http';
import type { StockDto } from '../src/api/types';
import { InMemoryLocalStore } from '../src/data/InMemoryLocalStore';
import type { PendingMutation } from '../src/data/LocalStore';
import { CHECKPOINT_KEY, SyncEngine } from '../src/sync/SyncEngine';
import { fakeApi } from './testUtils';

const count = (id: string, sku: string, quantity: number, occurredAt = '2026-11-09T10:00:00Z'): PendingMutation => ({
  id, kind: 'StockCount', sku, quantity, occurredAt, status: 'pending',
});

const stock = (sku: string, onHand: number, version = '1'): StockDto => ({
  sku, onHand, reserved: 0, available: onHand, reorderThreshold: 2, isLow: onHand <= 2, lastCountedAt: null, version,
});

function setup(api = fakeApi()) {
  const store = new InMemoryLocalStore();
  const engine = new SyncEngine({ store, api, storeId: 'STORE-001', deviceId: 'D1', batchSize: 2 });
  return { store, api, engine };
}

describe('SyncEngine', () => {
  it('pushes pending mutations and removes the ones the server accepted', async () => {
    const { store, api, engine } = setup();
    await store.enqueue(count('m1', 'A', 5));
    await store.enqueue(count('m2', 'B', 3));

    const result = await engine.sync();

    expect(result).toMatchObject({ pushed: 2, rejected: 0 });
    expect(api.pushInventory).toHaveBeenCalledWith(expect.objectContaining({
      storeId: 'STORE-001',
      deviceId: 'D1',
      mutations: [
        expect.objectContaining({ mutationId: 'm1', type: 'StockCount', sku: 'A', quantity: 5 }),
        expect.objectContaining({ mutationId: 'm2', type: 'StockCount', sku: 'B', quantity: 3 }),
      ],
    }));
    expect(await store.getPending(10)).toHaveLength(0);
  });

  it('drains the queue in batches', async () => {
    const { store, api, engine } = setup();
    for (let i = 1; i <= 5; i++) await store.enqueue(count(`m${i}`, `S${i}`, i, `2026-11-09T10:0${i}:00Z`));

    await engine.sync();

    expect(api.pushInventory).toHaveBeenCalledTimes(3); // 2 + 2 + 1
    expect(await store.getPending(10)).toHaveLength(0);
  });

  it('parks server-rejected mutations instead of retrying them forever', async () => {
    const api = fakeApi({
      pushInventory: jest.fn().mockResolvedValue({
        accepted: ['m1'],
        rejected: [{ mutationId: 'm2', reason: 'ZZZ is not ranged in STORE-001' }],
      }),
    });
    const { store, engine } = setup(api);
    await store.enqueue(count('m1', 'A', 5));
    await store.enqueue(count('m2', 'ZZZ', 1));

    const result = await engine.sync();

    expect(result.rejected).toBe(1);
    const all = await store.getAllMutations();
    expect(all).toEqual([expect.objectContaining({ id: 'm2', status: 'rejected', lastError: expect.stringContaining('not ranged') })]);
  });

  it('keeps everything queued when the network is down', async () => {
    const api = fakeApi({ pushInventory: jest.fn().mockRejectedValue(new NetworkError('offline')) });
    const { store, engine } = setup(api);
    await store.enqueue(count('m1', 'A', 5));

    const result = await engine.sync();

    expect(result.error).toBe('offline');
    expect(await store.getPending(10)).toHaveLength(1);
    expect(api.pullInventory).not.toHaveBeenCalled();
  });

  it('pushes before pulling, and pulls pages until hasMore is false, saving the checkpoint', async () => {
    const calls: string[] = [];
    const api = fakeApi({
      pushInventory: jest.fn(async (req) => {
        calls.push('push');
        return { accepted: req.mutations.map((m) => m.mutationId), rejected: [] };
      }),
      pullInventory: jest.fn()
        .mockImplementationOnce(async () => { calls.push('pull'); return { items: [stock('A', 5, '10')], checkpoint: '10', hasMore: true }; })
        .mockImplementationOnce(async () => { calls.push('pull'); return { items: [stock('B', 1, '11')], checkpoint: '11', hasMore: false }; }),
    });
    const { store, engine } = setup(api);
    await store.enqueue(count('m1', 'A', 5));

    await engine.sync();

    expect(calls).toEqual(['push', 'pull', 'pull']);
    expect(api.pullInventory).toHaveBeenNthCalledWith(1, 'STORE-001', '0');
    expect(api.pullInventory).toHaveBeenNthCalledWith(2, 'STORE-001', '10');
    expect(await store.getMeta(CHECKPOINT_KEY)).toBe('11');
    expect(await store.listStock()).toHaveLength(2);
  });

  it('does not overwrite rows that still have un-synced local changes', async () => {
    const api = fakeApi({
      // Server returns a conflict-ish "neither accepted nor rejected" -> stays pending
      pushInventory: jest.fn().mockResolvedValue({ accepted: [], rejected: [] }),
      pullInventory: jest.fn().mockResolvedValue({ items: [stock('A', 99, '20')], checkpoint: '20', hasMore: false }),
    });
    const { store, engine } = setup(api);
    await store.upsertStock([stock('A', 7)]); // optimistic local value
    await store.enqueue(count('m1', 'A', 7));

    await engine.sync();

    expect((await store.getStock('A'))?.onHand).toBe(7);
  });

  it('rejects an order pick on a 409 but retries later on a 503', async () => {
    const api = fakeApi({
      markOrderPicked: jest.fn()
        .mockRejectedValueOnce(new ApiError(409, 'Order is Collected'))
        .mockRejectedValueOnce(new ApiError(503, 'Unavailable')),
    });
    const { store, engine } = setup(api);
    await store.enqueue({ id: 'p1', kind: 'OrderPicked', orderId: 'o1', occurredAt: '2026-11-09T10:00:00Z', status: 'pending' });
    await store.enqueue({ id: 'p2', kind: 'OrderPicked', orderId: 'o2', occurredAt: '2026-11-09T10:01:00Z', status: 'pending' });

    const result = await engine.sync();

    expect(result.rejected).toBe(1);
    expect(result.error).toBe('Unavailable');
    const all = await store.getAllMutations();
    expect(all.find((m) => m.id === 'p1')?.status).toBe('rejected');
    expect(all.find((m) => m.id === 'p2')?.status).toBe('pending');
  });

  it('is single-flight: concurrent sync() calls share one run', async () => {
    const { store, api, engine } = setup();
    await store.enqueue(count('m1', 'A', 5));

    await Promise.all([engine.sync(), engine.sync(), engine.sync()]);

    expect(api.pushInventory).toHaveBeenCalledTimes(1);
  });
});
