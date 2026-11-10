import React from 'react';
import type { RetailApi } from '../src/api/retailApi';
import type { PushRequest } from '../src/api/types';
import { InMemoryLocalStore } from '../src/data/InMemoryLocalStore';
import { SyncProvider, type AppServices, type Connectivity } from '../src/sync/SyncProvider';

/** Connectivity we can flip from a test: `net.set(true)` simulates coming online. */
export function fakeConnectivity(initial: boolean) {
  let listener: ((online: boolean) => void) | null = null;
  let current = initial;
  const connectivity: Connectivity = {
    subscribe(l) {
      listener = l;
      l(current);
      return () => {
        listener = null;
      };
    },
  };
  return {
    connectivity,
    set(online: boolean) {
      current = online;
      listener?.(online);
    },
  };
}

/** A RetailApi where every method is a jest.fn with a sensible default. */
export function fakeApi(overrides: Partial<RetailApi> = {}): jest.Mocked<RetailApi> {
  return {
    getProduct: jest.fn().mockRejectedValue(new Error('not found')),
    pushInventory: jest.fn(async (req: PushRequest) => ({
      accepted: req.mutations.map((m) => m.mutationId),
      rejected: [],
    })),
    pullInventory: jest.fn().mockResolvedValue({ items: [], checkpoint: '0', hasMore: false }),
    getPickList: jest.fn().mockResolvedValue([]),
    markOrderPicked: jest.fn().mockResolvedValue({}),
    ...overrides,
  } as jest.Mocked<RetailApi>;
}

let idCounter = 0;

export function buildServices(opts: { online?: boolean; api?: jest.Mocked<RetailApi> } = {}) {
  const net = fakeConnectivity(opts.online ?? false);
  const store = new InMemoryLocalStore();
  const api = opts.api ?? fakeApi();
  const services: AppServices = {
    store,
    api,
    storeId: 'STORE-001',
    deviceId: 'TEST-DEVICE',
    newId: () => `id-${++idCounter}`,
    connectivity: net.connectivity,
    syncIntervalMs: 1_000_000, // effectively disabled (note: >2^31 ms makes Node fire every 1ms!)
  };
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <SyncProvider services={services}>{children}</SyncProvider>
  );
  return { services, store, api, net, wrapper };
}
