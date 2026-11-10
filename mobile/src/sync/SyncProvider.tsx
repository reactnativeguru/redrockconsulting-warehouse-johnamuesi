import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import type { RetailApi } from '../api/retailApi';
import type { LocalStore, PendingMutation } from '../data/LocalStore';
import { LAST_SYNC_KEY, SyncEngine, type SyncResult } from './SyncEngine';

/** Abstracts NetInfo so tests can drive connectivity directly. */
export interface Connectivity {
  subscribe(listener: (online: boolean) => void): () => void;
}

export interface AppServices {
  store: LocalStore;
  api: RetailApi;
  storeId: string;
  deviceId: string;
  newId: () => string;
  connectivity: Connectivity;
  syncIntervalMs?: number;
}

export interface SyncState {
  online: boolean;
  syncing: boolean;
  pendingCount: number;
  rejectedCount: number;
  lastSyncAt: string | null;
  lastError: string | null;
}

interface SyncContextValue extends SyncState {
  services: AppServices;
  syncNow(): Promise<SyncResult | undefined>;
  /** Record a user action: save locally, update the local view, sync if we can. */
  record(mutation: Omit<PendingMutation, 'id' | 'occurredAt' | 'status'>): Promise<PendingMutation>;
  mutations: PendingMutation[];
}

const SyncContext = createContext<SyncContextValue | null>(null);

export function SyncProvider({ services, children }: { services: AppServices; children: React.ReactNode }) {
  const engine = useMemo(
    () => new SyncEngine({ store: services.store, api: services.api, storeId: services.storeId, deviceId: services.deviceId }),
    [services],
  );
  const [state, setState] = useState<SyncState>({
    online: false, syncing: false, pendingCount: 0, rejectedCount: 0, lastSyncAt: null, lastError: null,
  });
  const [mutations, setMutations] = useState<PendingMutation[]>([]);
  const onlineRef = useRef(false);

  const refresh = useCallback(async () => {
    const all = await services.store.getAllMutations();
    const lastSyncAt = await services.store.getMeta(LAST_SYNC_KEY);
    setMutations(all);
    setState((s) => ({
      ...s,
      pendingCount: all.filter((m) => m.status === 'pending').length,
      rejectedCount: all.filter((m) => m.status === 'rejected').length,
      lastSyncAt,
    }));
  }, [services]);

  const syncNow = useCallback(async () => {
    if (!onlineRef.current) return undefined;
    setState((s) => ({ ...s, syncing: true }));
    const result = await engine.sync();
    setState((s) => ({ ...s, syncing: false, lastError: result.error ?? null }));
    await refresh();
    return result;
  }, [engine, refresh]);

  const record = useCallback<SyncContextValue['record']>(
    async (input) => {
      const mutation: PendingMutation = {
        ...input,
        id: services.newId(),
        occurredAt: new Date().toISOString(),
        status: 'pending',
      };
      await services.store.enqueue(mutation);

      // Optimistic UI: reflect a count in the local stock view immediately.
      if (mutation.kind === 'StockCount' && mutation.sku && mutation.quantity !== undefined) {
        const current = await services.store.getStock(mutation.sku);
        const reserved = current?.reserved ?? 0;
        await services.store.upsertStock([{
          sku: mutation.sku,
          onHand: mutation.quantity,
          reserved,
          available: mutation.quantity - reserved,
          reorderThreshold: current?.reorderThreshold ?? 0,
          isLow: mutation.quantity - reserved <= (current?.reorderThreshold ?? 0),
          lastCountedAt: mutation.occurredAt,
          version: current?.version ?? '0',
        }]);
      }

      await refresh();
      void syncNow(); // fire-and-forget; no-op when offline
      return mutation;
    },
    [services, refresh, syncNow],
  );

  // Startup + connectivity-triggered sync + periodic background sync.
  useEffect(() => {
    let cancelled = false;
    void refresh();
    const unsubscribe = services.connectivity.subscribe((online) => {
      const cameOnline = online && !onlineRef.current;
      onlineRef.current = online;
      if (!cancelled) setState((s) => ({ ...s, online }));
      if (cameOnline) void syncNow();
    });
    const timer = setInterval(() => void syncNow(), services.syncIntervalMs ?? 30_000);
    return () => {
      cancelled = true;
      unsubscribe();
      clearInterval(timer);
    };
  }, [services, refresh, syncNow]);

  const value = useMemo<SyncContextValue>(
    () => ({ ...state, services, syncNow, record, mutations }),
    [state, services, syncNow, record, mutations],
  );

  return <SyncContext.Provider value={value}>{children}</SyncContext.Provider>;
}

export function useSync(): SyncContextValue {
  const ctx = useContext(SyncContext);
  if (!ctx) throw new Error('useSync must be used inside <SyncProvider>');
  return ctx;
}
