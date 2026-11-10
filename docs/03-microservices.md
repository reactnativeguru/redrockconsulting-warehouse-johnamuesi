# 03 — Microservices: principles, and where they show up here

## What a microservice *is* (one-liner)

> An independently deployable unit that owns one business capability and its data, and collaborates with others through well-defined APIs and events.

"Independently deployable" is the test. If you can't release Inventory without releasing Orders, you have a **distributed monolith**: all the costs of distribution with none of the benefits.

## Core principles → this codebase

| Principle | Meaning | Here |
|---|---|---|
| **Single responsibility / bounded context** | Split by business capability, not by technical layer | Catalog, Inventory, Orders, ETL. Not "a database service" |
| **Database per service** | Nobody else reads or writes your tables | Inventory and Orders have separate SQL databases. Catalog has Mongo |
| **Smart endpoints, dumb pipes** | Logic lives in services, the broker just moves messages | RabbitMQ only routes. Rules live in `StockItem` / `ReservationPolicy` |
| **Contracts as public API** | Events and REST shapes are versioned like a library | `Basecamp.Contracts`: add fields, never rename or move |
| **Design for failure** | Networks fail, services restart, messages repeat | Outbox, inbox, retries, idempotency keys, circuit breaker |
| **Decentralised data, eventual consistency** | No distributed transactions across services | Order is `Submitted` → `ReadyToPick` a moment later |
| **Automate everything** | CI/CD, IaC, health checks | Jenkinsfile, k8s, Bicep, `/health` |
| **Observability** | Logs, metrics, traces across hops | Structured logging now. OpenTelemetry is next (see below) |
| **Polyglot where it pays** | Pick tech per service | Mongo for catalogue, SQL for stock |

## Hard problems and the answers used

### 1. Consistency without distributed transactions
There's no `BEGIN TRAN` across Orders and Inventory. Instead, each service does a **local transaction** and publishes an **event**. Others react. The system becomes consistent **eventually** (milliseconds here).

- **Choreography** (used here): services react to each other's events, with no central coordinator. Simple for 2–3 steps, but harder to see the whole flow as it grows.
- **Orchestration / Saga**: a coordinator (e.g. a MassTransit `MassTransitStateMachine<OrderState>`) tells each service what to do and handles **compensations** (e.g. release stock if payment fails). Choose it when a flow has many steps, timeouts or compensations.

> Design note: this repo starts with choreography because the flow is two hops. Once payment and cancellation are added, the order lifecycle should move into a saga state machine, because compensations get hard to reason about when they're spread across consumers.

### 2. The dual-write problem → transactional outbox
`db.SaveChanges(); bus.Publish(...)`: if the process dies between the two, data and events disagree. **Outbox:** write the event into an `OutboxMessage` table *in the same DB transaction*, and a background process delivers it. Details in [04](04-messaging-rabbitmq-masstransit.md).

### 3. At-least-once delivery → idempotency
Brokers guarantee *at least once*, so duplicates **will** happen. Every handler must be safe to run twice:
- **Inbox** (MassTransit): remembers consumed `MessageId`s.
- **Business keys**: `ProcessedKeys` stores device mutation ids and ePOS batch ids.
- **Naturally idempotent operations**: `POST /orders/{id}/picked` on an already-picked order is a no-op 200. Stock *counts* are absolute values (setting 12 twice = 12).

### 4. Concurrency → optimistic locking
Two consumers reserving the last unit: `rowversion` makes the second `UPDATE` fail, and retry re-reads. No locks are held across network calls.

### 5. Synchronous calls → resilience
Orders → Catalog uses `AddStandardResilienceHandler()` (Polly): retries with jittered backoff, attempt timeout, total timeout and a **circuit breaker**. That stops a struggling Catalog from being hammered and stops Orders' threads piling up waiting.

Rule: **avoid long synchronous call chains** (A→B→C→D). Availability multiplies down: 99.9%⁴ ≈ 99.6%.

### 6. One front door → API gateway
YARP routes `/api/products/**` and so on. Cross-cutting concerns go there: CORS, auth, rate limiting, request logging. Clients don't know the topology. Some teams add a **BFF** (Backend-for-Frontend) per client type, e.g. a handheld BFF that aggregates product + stock in one call.

### 7. Observability (what I'd add next)
- **Correlation**: MassTransit propagates `CorrelationId`/`ConversationId` and supports OpenTelemetry out of the box.
- `builder.Services.AddOpenTelemetry().WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource("MassTransit"))` exported to Azure Monitor / Application Insights gives one trace across HTTP → RabbitMQ → consumer.

## When NOT to use microservices
- Small team, unclear domain boundaries, early product → **modular monolith** first (clear modules, one deployable). Split later along proven seams.
- The payoff that justifies the cost is **tech flexibility per service** and **independent delivery per product team**.

## Microservice design checklist
1. What business capability does it own? What data?
2. What does it *publish*? What does it *consume*? What does it *expose* over HTTP?
3. What happens when each dependency is down?
4. Is every handler idempotent? What's the idempotency key?
5. How do we deploy it independently? How do we version its contracts?
6. How do we see it in production (health, logs, traces, metrics, dead letters)?
