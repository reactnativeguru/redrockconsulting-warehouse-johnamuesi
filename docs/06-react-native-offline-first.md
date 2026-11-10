# 06 — React Native: offline-first handheld app

This guide focuses on the **offline-first sync design** (a local on-device database that syncs with the server, as an alternative to products such as Ditto, Couchbase Mobile or Firebase) and on how the app plugs into the .NET backend.

## App structure (`mobile/`)

```
App.tsx                      Shell: header, SyncStatusBar, simple tab state
src/services.ts              Composition root: SQLite store, API client, NetInfo, UUIDs
src/api/http.ts              fetch wrapper: NetworkError vs ApiError(status)
src/api/retailApi.ts         Typed API (product lookup, sync push/pull, pick list, mark picked)
src/data/LocalStore.ts       Persistence interface (outbox + local read model + meta)
src/data/SqliteLocalStore.ts expo-sqlite implementation (WAL, transactions)
src/data/InMemoryLocalStore.ts  Same contract, used in tests
src/sync/SyncEngine.ts       Push/pull algorithm (pure TS, no React)
src/sync/SyncProvider.tsx    React context: state, record(), connectivity + interval triggers
src/screens/*                Lookup, StockCount, PickList
__tests__/*                  Jest + RNTL v14
```

Design choices worth defending:
- **Dependency injection through context** (`AppServices`): screens never import SQLite or fetch directly, so tests swap in `InMemoryLocalStore`, a fake API and fake connectivity. Same idea as .NET DI.
- **Sync engine is framework-free TS**, unit-testable without rendering anything.
- **No navigation library**: three task-focused screens don't need one yet (pragmatism over premature structure).
- **Hardware scanners** (Zebra DataWedge) act as a keyboard: scan → text + Enter. A focused `TextInput` with `onSubmitEditing` handles them with zero native code. `expo-camera` is the phone fallback.

## The offline-first model

> **The local database is the source of truth for the UI. The network is a background concern.**

Two tables do the work:

| Table | Role |
|---|---|
| `outbox` | Things the user *did* that the server hasn't acknowledged yet (mutations) |
| `stock` (+ `products`) | Local **read model**: a cache of server state, updated by pull and optimistic writes |
| `meta` | `inventory.checkpoint`, `lastSyncAt` |

### Writing (always local first)
`record({ kind: 'StockCount', sku, quantity })`:
1. Generate a **client UUID** (`expo-crypto randomUUID`). This is the idempotency key.
2. `INSERT` into `outbox` with `occurredAt = now`.
3. **Optimistically** update the local `stock` row so the UI reflects it instantly.
4. Kick a sync if online (fire-and-forget).

The colleague **never waits on the network**, and nothing is lost if the app is killed: it's in SQLite.

### Syncing: push, then pull
```
PUSH  pending outbox (batches of 100) → POST /api/inventory/sync/push
      ← { accepted: [ids], rejected: [{id, reason}] }
      delete accepted · park rejected (status='rejected', shown in UI) · leave the rest pending
PULL  GET /api/inventory/sync/pull?storeId&since=<checkpoint>
      ← { items, checkpoint, hasMore }
      upsert rows (except SKUs with pending local changes) · save checkpoint · loop while hasMore
```

Why this order and these rules:
- **Push before pull** so the pull includes the server's view *after* our changes.
- **Rows with pending mutations are not overwritten** ("local wins until acknowledged"). Otherwise a pull could briefly revert what the user just did.
- **Checkpoint saved after the rows**: a crash means re-downloading a page, never skipping one.
- **Single-flight**: concurrent `sync()` calls share one promise, so the same batch is never double-sent.
- **Network error** → stop, leave everything queued, retry on the next trigger.
- **Server 4xx on a mutation** → park it as rejected (a *poison message*). It must not block the queue forever. Same idea as the backend's `_error` queue.

Triggers: app start, NetInfo "came online", a 30s interval, tapping the status bar, and right after `record()`.

### Server side (`Inventory.Api/Sync/SyncService.cs`)
- **Idempotent push**: each `mutationId` is recorded in `ProcessedKeys`. If the device re-sends (the response was lost in a tunnel), the server returns "accepted" without re-applying.
- **Conflict rule for counts: last *counted* wins.** A count is an absolute value at a moment in time. A count taken at 10:00 that syncs at 15:00 must not overwrite a 12:00 count. So compare `occurredAt` with `LastCountedAt`, not arrival time.
- **Deltas vs absolutes**: adjustments (+3 delivery, −1 damaged) are **commutative deltas**: order doesn't matter, so they just apply. Absolute values need a timestamp rule. Designing mutations as deltas where possible removes most conflicts.
- **Concurrency conflict** (`DbUpdateConcurrencyException`) → the mutation is neither accepted nor rejected, so the device retries it next sync.
- **Pull** uses SQL Server `rowversion` as a monotonic change sequence → `WHERE StoreId=@s AND RowVersion > @since ORDER BY RowVersion`. The checkpoint is a **string** (uint64 > JS safe integer).

## Conflict-resolution strategies

| Strategy | How | Good for | Risk |
|---|---|---|---|
| Last-writer-wins (by client timestamp) | Compare `occurredAt` | Absolute values (counts) | Device clock skew. Mitigate with server-side sanity checks or NTP on MDM-managed devices |
| Server-authoritative | Server decides, client rebases | Prices, order status | Client must handle rejections |
| Commutative operations | Send deltas, not totals | Stock adjustments, sales | Not every op can be a delta |
| CRDTs | Mergeable data types (counters, sets, maps) | Multi-writer collaborative data | Complexity. This is what **Ditto** gives you out of the box |
| Manual merge | Show the user both versions | Documents/notes | UX cost |

## Ditto / Couchbase Mobile / Firebase vs hand-rolled

| | Hand-rolled (this repo) | Couchbase Lite + Sync Gateway | Ditto | Firebase (Firestore) |
|---|---|---|---|---|
| Local DB | SQLite | Couchbase Lite (JSON docs) | Ditto store | Firestore cache |
| Conflict handling | Your rules | Revision trees + custom resolvers | CRDTs (automatic) | Last write wins |
| Peer-to-peer (device ↔ device, no internet) | ❌ | Limited | ✅ BLE/Wi-Fi mesh: big for stores with no signal | ❌ |
| Backend | Your API (.NET) | Sync Gateway + Couchbase Server | Ditto Cloud / Big Peer | Google Cloud |
| Fits existing SQL/.NET domain | ✅ natural | Needs mapping | Needs connector | Needs mapping |

> Design note: "Outbox + checkpoint sync is hand-rolled here because the domain is small and it maps cleanly onto our SQL model with clear conflict rules. For multi-writer data, or stores with no connectivity where devices need to sync with *each other*, I'd evaluate Ditto, because CRDTs and mesh sync are hard to build well."

## Testing (see `__tests__/`)
- `SyncEngine.test.ts`: 8 tests covering batching, rejection parking, network failure, push-then-pull ordering, checkpoint paging, pending-row protection, 409 vs 503 handling, single-flight.
- `StockCountScreen.test.tsx`: offline save → "waiting" banner → flip connectivity → auto-sync drains the queue.
- `LookupScreen.test.tsx`: scanner submit flow, offline product-cache fallback.
- **RNTL v14** made `render`, `fireEvent` and `act` **async** (React 19). Always `await` them.

## Production hardening
- Auth: device + user login (Entra ID via MSAL / `expo-auth-session`). Store tokens in `expo-secure-store`.
- Fleet: MDM (SOTI/Intune) pushes config per store, and **EAS Update** ships JS OTA updates without app-store releases.
- Background sync: `expo-background-task` for syncing while the app is backgrounded.
- Telemetry: Sentry / App Insights with sync metrics (queue depth, last success, rejection rate).
- Data retention: purge synced rows, and cap the outbox size with a visible warning.
