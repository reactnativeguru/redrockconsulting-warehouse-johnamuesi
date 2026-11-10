# 08 — Testing

High-quality, tested code is a first-class goal of this repo: plenty of fast unit tests, plus Jest and React Testing Library for the front ends.

## What's tested, where

| Layer | Tool | Files | What it proves |
|---|---|---|---|
| Domain rules (C#) | xUnit | `backend/tests/Inventory.Domain.Tests` | Reservation, fulfilment, sales going negative, stale counts ignored, all-or-nothing reservations |
| Parsing/ETL (C#) | xUnit | `backend/tests/Epos.Etl.Tests` | Valid rows, refunds, bad rows reported with line numbers, culture-safe numbers |
| Messaging (C#) | MassTransit test harness + EF InMemory | `backend/tests/Inventory.Api.Tests` | Consumer publishes `StockReserved` / `StockReservationFailed` and persists reservations |
| Sync algorithm (TS) | Jest | `mobile/__tests__/SyncEngine.test.ts` | Batching, idempotent acks, rejection parking, offline, ordering, checkpoints, single-flight |
| RN screens | Jest + **React Native Testing Library** | `mobile/__tests__/*Screen.test.tsx` | Offline save → auto-sync on reconnect, validation, scanner submit, cache fallback |
| Micro-frontends | Jest + **React Testing Library** + user-event | `web/*/src/*.test.tsx` | Search, price edit + event to shell, store switching, low-stock filter, remote failure isolation |

## The test pyramid applied
```
        ▲  few   E2E (Playwright / Detox / Maestro), next step
       ▲▲▲       Integration: consumer + DB, API + DB (Testcontainers, WebApplicationFactory)
    ▲▲▲▲▲▲▲ many Unit: domain rules, parsers, sync engine, components
```
Push logic **down** into pure, fast-to-test units: `StockItem`, `ReservationPolicy`, `SalesCsvParser`, `SyncEngine`. They have no DB, broker or UI. Then the outer layers need fewer, simpler tests.

## xUnit essentials
```csharp
public class StockItemTests                       // a test class (new instance PER test)
{
    [Fact]                                         // = it(...)
    public void Reserve_MoreThanAvailable_Throws()
    {
        var item = new StockItem("S1", "SKU", onHand: 2, reorderThreshold: 0);   // Arrange
        var ex = Assert.Throws<InsufficientStockException>(() => item.Reserve(3)); // Act + Assert
        Assert.Equal(2, ex.Available);
    }

    [Theory]                                       // = it.each([...])
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_NonPositive_Throws(int qty) => Assert.Throws<ArgumentOutOfRangeException>(() => ...);
}
```
- Setup/teardown: constructor and `IDisposable`, or `IAsyncLifetime` for async (see the consumer test).
- Naming: `Method_Scenario_ExpectedResult`.
- Mocks: **NSubstitute** (`Substitute.For<IProductRepository>()`) or Moq. Prefer fakes for things you own.
- Assertions here use plain `Assert`. FluentAssertions v8+ is commercially licensed (another licensing shift). Shouldly or AwesomeAssertions are free alternatives.

## MassTransit test harness
```csharp
.AddMassTransitTestHarness(x => x.AddConsumer<OrderSubmittedConsumer>())
await harness.Bus.Publish(new OrderSubmitted(...));
Assert.True(await harness.Consumed.Any<OrderSubmitted>());
Assert.True(await harness.Published.Any<StockReserved>(m => m.Context.Message.OrderId == orderId));
```
This is a real in-memory bus and the real consumer pipeline, with no RabbitMQ. It's fast enough for unit-test suites.

## Integration tests (next step to mention)
- **`WebApplicationFactory<Program>`** boots the real ASP.NET Core app in-memory and gives you an `HttpClient`. (That's why each `Program.cs` ends with `public partial class Program;`.)
- **Testcontainers for .NET** spins up real SQL Server, Mongo and RabbitMQ in Docker per test run. It catches what InMemory can't (rowversion, SQL translation, transactions).

## React Testing Library philosophy
> "The more your tests resemble the way your software is used, the more confidence they can give you."

- Query by what users perceive: `getByRole('button', { name: 'Save' })`, `getByLabelText`, `getByText`. Use `testID`/`data-testid` only as a last resort.
- `findBy*` = wait for async UI. `queryBy*` = assert absence.
- **`@testing-library/user-event`** (web) simulates real typing and clicking, which is better than `fireEvent`.
- **RNTL v14** (React 19): `render`, `fireEvent` and `act` return Promises, so always `await` them.
- Mock at the **boundary**: `fetch`, the API object, connectivity. Never mock the component under test.

## Gotchas found while building this (true stories to tell)
- **Timer overflow:** a test set the sync interval to 60,000,000 ms to "disable" it. Node clamps timers above 2³¹−1 ms to **1 ms**, so the interval fired constantly and Jest hung. Fix: a sane large value (or inject a scheduler).
- **RNTL v14 async migration:** tests failed with "render function has not been called" until `render` was awaited.
- **Brittle text matchers:** `findByText('1 lines · ')` failed because the matcher normalises whitespace. Asserting on behaviour (the row disappears) is more robust.

## Code review checklist (they have a strong review culture)
1. Does it have tests at the right level? Do they fail without the change?
2. Are failure paths handled (network down, duplicates, concurrency)?
3. Is every message handler idempotent?
4. Any blocking calls (`.Result`), missing `CancellationToken`, or `async void`?
5. N+1 queries, missing `AsNoTracking`, unbounded queries?
6. Contracts changed additively? Breaking API/event changes versioned?
7. Secrets out of code? Logging free of PII?
8. Is it as simple as it can be? (Pragmatism over cleverness.)
