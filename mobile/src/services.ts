import NetInfo from '@react-native-community/netinfo';
import * as Crypto from 'expo-crypto';
import { createHttpClient } from './api/http';
import { createRetailApi } from './api/retailApi';
import { config } from './config';
import { SqliteLocalStore } from './data/SqliteLocalStore';
import type { AppServices, Connectivity } from './sync/SyncProvider';

/** NetInfo-backed connectivity. isInternetReachable can be null while unknown. */
const netInfoConnectivity: Connectivity = {
  subscribe: (listener) =>
    NetInfo.addEventListener((s) => listener(!!s.isConnected && s.isInternetReachable !== false)),
};

/**
 * Composition root for the app: the ONE place real implementations are chosen.
 * Tests build their own AppServices with fakes instead.
 */
export async function createAppServices(): Promise<AppServices> {
  const store = new SqliteLocalStore();
  await store.init();
  return {
    store,
    api: createRetailApi(createHttpClient(config.apiUrl)),
    storeId: config.storeId,
    deviceId: config.deviceId,
    newId: () => Crypto.randomUUID(),
    connectivity: netInfoConnectivity,
    syncIntervalMs: config.syncIntervalMs,
  };
}
