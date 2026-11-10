# 00 — Guided tour: see every concept happen

Run `docker compose up --build`, then work through these. Each step names the code responsible, so you can open it alongside.

Tip: use the `.http` files in each service (`backend/src/Services/*/*.http`) from VS Code (REST Client extension) or Rider. The `curl` commands below are equivalents.

---

## 1. The gateway and REST APIs

```bash
curl http://localhost:5050/api/products?search=jacket
curl http://localhost:5050/api/products/barcode/5060000000011
curl http://localhost:5050/api/inventory/stores/STORE-001/stock
```

**What's happening:** one origin (`:5050`, the YARP gateway) routes `/api/products/**` to Catalog, `/api/inventory/**` to Inventory and `/api/orders/**` to Orders. See `backend/src/Gateway/Gateway/appsettings.json`.

**Concepts:** API gateway, RESTful resource design (`/stores/{id}/stock/{sku}`), MongoDB vs SQL Server behind the same API style.

## 2. A click & collect order: async messaging across services

```bash
curl -i -X POST http://localhost:5050/api/orders -H 'Content-Type: application/json' -d '{
  "storeId":"STORE-001","customerName":"Alex","customerEmail":"alex@example.com",
  "lines":[{"sku":"BC-JKT-001","quantity":1}]}'
```

1. You get **202 Accepted** with status `Submitted`. The order exists, but stock isn't reserved yet.
2. Repeat `GET /api/orders/{id}`. Within about a second it becomes `ReadyToPick`.
3. Open the RabbitMQ UI (http://localhost:15672) → **Exchanges**. You'll see `Basecamp.Contracts:OrderSubmitted`, `Basecamp.Contracts:StockReserved` and so on. Under **Queues** you'll see `order-submitted`, `stock-reserved`, etc. (one per consumer).

**What happened:**

```
Orders API ──(writes order + OrderSubmitted to OUTBOX in one SQL transaction)──▶ SQL
Outbox delivery service ──publish──▶ RabbitMQ exchange "OrderSubmitted"
   ──▶ queue "order-submitted" ──▶ Inventory OrderSubmittedConsumer
         reserves stock + writes StockReserved to ITS outbox (one transaction)
   ──▶ RabbitMQ "StockReserved" ──▶ Orders StockReservedConsumer → status ReadyToPick
```

Code: `OrdersController.Place`, `Inventory.Api/Consumers/OrderSubmittedConsumer.cs`, `Orders.Api/Consumers/StockReservationConsumers.cs`.

**Now try a failure:** order 999 tents. The order becomes `Rejected` with a reason. Inventory published `StockReservationFailed`.

**Concepts:** choreography, eventual consistency, transactional outbox, idempotent consumers (inbox), 202 Accepted.

## 3. Optimistic concurrency: two orders race for the last unit

Find a SKU with `available: 1` in `/api/inventory/stores/STORE-001/stock`, then fire two orders for it at the same time (two terminals). Exactly one becomes `ReadyToPick`. The other is `Rejected`.

**Why:** both consumers read stock = 1. The first `SaveChanges` succeeds and SQL Server bumps the row's `rowversion`. The second `SaveChanges` sends `UPDATE ... WHERE RowVersion = @old`, which matches 0 rows, so EF throws `DbUpdateConcurrencyException`. MassTransit's retry policy re-runs the consumer with a fresh `DbContext`. It now sees 0 available and publishes `StockReservationFailed`.

Code: `InventoryDbContext` (`IsRowVersion()`), `MessagingExtensions.ConfigureCommon` (retry).

## 4. Pick the order (as the handheld would)

```bash
curl -X POST http://localhost:5050/api/orders/{id}/picked   # run it twice: idempotent
```

Orders publishes `OrderPicked`. Inventory's `OrderPickedConsumer` converts the reservation into a real stock decrement and writes an audit row:

```bash
curl http://localhost:5050/api/inventory/stores/STORE-001/movements
```

## 5. ETL from the ePOS

```bash
cp backend/sample-data/epos-sales-20261109.csv data/epos/inbound/
docker compose logs -f epos-etl
```

Within 10 seconds:
- The file moves to `data/epos/processed/`.
- `data/epos/error/epos-sales-20261109.csv.errors.txt` lists the one bad row (`abc` quantity). The good rows still went through.
- One `SalesRecorded` event is published **per store** in the file.
- Stock drops in STORE-001/STORE-002, and a refund (negative quantity) adds stock back.

**Re-drop the same file.** Nothing changes: the BatchId `fileName:storeId` is already in Inventory's `ProcessedKeys` table.

Code: `SalesCsvParser` (transform), `SalesImportWorker` (extract/load), `SalesRecordedConsumer`.

## 6. ETL to the ePOS (price change)

```bash
curl -X PUT http://localhost:5050/api/products/BC-HAT-001/price -H 'Content-Type: application/json' -d '{"price":7.99}'
cat data/epos/outbound/prices_*.csv
```

Catalog publishes `ProductPriceChanged`, and the ETL's `PriceExportConsumer` appends it to the tills' price file. Or do it in the admin portal: http://localhost:8080 → Catalogue → Change price. The shell shows a toast that the remote raised via a browser event.

## 7. Micro-frontend isolation

With `cd web && npm start` running, **stop only the inventory remote** (Ctrl-C its process, or change its port). Reload http://localhost:3000:
- **Catalogue** still works.
- **Stock** shows "Stock is unavailable right now". The shell's `RemoteBoundary` caught the failed load.

Each remote also runs standalone: http://localhost:3001 and http://localhost:3002.

## 8. Offline-first handheld

1. Start the app (`cd mobile && npx expo start`). The banner says **Up to date**.
2. **Stock count** → SKU `BC-BOOT-001`, quantity `12` → Save. It syncs immediately.
3. Turn on **airplane mode** (or `docker compose stop gateway`). The banner turns amber.
4. Record two more counts. They show as **Waiting to sync**, and **Lookup** shows the new on-hand straight away (optimistic update).
5. Restore the network. The NetInfo listener fires, the sync engine **pushes then pulls**, and the queue drains.
6. Record a count for a SKU that doesn't exist (`NOPE-1`). The server rejects it, and it is parked as **"Rejected: … not ranged"** instead of blocking the queue.

Code: `mobile/src/sync/SyncEngine.ts`, `mobile/src/sync/SyncProvider.tsx`, `Inventory.Api/Sync/SyncService.cs`.

## 9. Swap RabbitMQ for Azure Service Bus without code changes

Set `Messaging__Transport=AzureServiceBus` and `ConnectionStrings__ServiceBus=...`. Nothing else changes, because services only use MassTransit abstractions (`IPublishEndpoint`, `IConsumer<T>`). That's exactly what the Azure deployment does. See [10-azure-deployment.md](10-azure-deployment.md).
