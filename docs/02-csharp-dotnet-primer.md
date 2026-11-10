# 02 — C# & .NET for TypeScript developers

You already think in TypeScript, async/await, DI-ish React context and Node servers. Most of C# maps directly. This guide covers **every C# feature used in this repo**, with a TS equivalent.

## The ecosystem in one table

| .NET | JS/TS equivalent |
|---|---|
| .NET 10 SDK (`dotnet` CLI) | Node + npm + tsc in one |
| `*.csproj` | `package.json` (+ tsconfig) |
| `*.slnx` (solution) | npm workspaces root |
| NuGet package | npm package |
| `Directory.Packages.props` (Central Package Management) | one pinned version list for all workspaces |
| `Directory.Build.props` | shared tsconfig `extends` |
| ASP.NET Core | Express/Fastify/NestJS |
| Entity Framework Core | Prisma / TypeORM |
| xUnit | Jest |
| `dotnet run` / `dotnet test` / `dotnet publish` | `npm start` / `npm test` / build |

.NET 10 is an **LTS** release (supported for 3 years). Odd-numbered versions are STS (18 months). Teams usually run on LTS.

## Syntax map

```csharp
// --- variables & types ---
var count = 3;                      // let count = 3   (type inferred, still static)
const int Max = 10;                 // const MAX = 10  (compile-time constant)
string? maybe = null;               // let maybe: string | null
decimal price = 89.99m;             // money! never double/float for currency ('m' suffix)
DateTimeOffset at = DateTimeOffset.UtcNow;   // Date with explicit UTC offset

// --- string interpolation ---
$"Store {storeId} has {qty}"        // `Store ${storeId} has ${qty}`

// --- collections ---
List<string> skus = ["A", "B"];      // const skus: string[] = ['A','B']  (C# 12 collection expressions)
Dictionary<string, int> stock = [];  // Map<string, number> / Record<string, number>
IReadOnlyList<T>                     // ReadonlyArray<T>

// --- null handling ---
order?.Lines                         // order?.lines
name ?? "unknown"                    // name ?? 'unknown'
item!.Sku                            // item!.sku  (null-forgiving)
```

### Classes, records, and immutability

```csharp
// Record = immutable data type with value equality. Like a TS `type` + Object.freeze.
public record OrderSubmitted(Guid OrderId, string StoreId, IReadOnlyList<OrderLine> Lines, DateTimeOffset SubmittedAt);
// Two records with the same values are == (unlike classes, which compare by reference).
// `with` creates a modified copy:  var later = evt with { StoreId = "STORE-002" };  // like {...evt, storeId: ...}
```
Used for: **message contracts** (`Basecamp.Contracts/Events.cs`), **DTOs** (`SyncModels.cs`).

```csharp
// Class with encapsulated state = a domain entity / aggregate
public sealed class StockItem            // sealed = can't be inherited (like a `final` class)
{
    public int OnHand { get; private set; }   // readable everywhere, settable only inside
    public int Available => OnHand - Reserved; // computed getter: get available() { return ... }

    public void Reserve(int quantity) { ... }  // behaviour lives WITH the data
}
```
`{ get; private set; }` is the C# way to enforce invariants: no outside code can do `item.OnHand = -5`. Compare `StockItem.cs`.

**Primary constructors** (C# 12) are like TS constructor parameter properties:
```csharp
public class SyncService(InventoryDbContext db, ILogger<SyncService> logger) { ... }
// TS:  class SyncService { constructor(private db: InventoryDbContext, private logger: Logger) {} }
```

**`required` / `init`**:
```csharp
public class StockMovement {
    public required string Sku { get; init; }  // must be set when created, then immutable
}
new StockMovement { Sku = "BC-JKT-001", ... }  // object initializer (like an object literal)
```

### Enums and pattern matching

```csharp
public enum OrderStatus { Submitted, ReadyToPick, Rejected }   // stored as string via HasConversion<string>()

if (LastCountedAt is { } last && countedAt <= last) ...       // "if not null, bind to `last`"
var x = obj is Product p ? p.Name : "?";                       // type narrowing, like `instanceof`
switch (mutation.Type) { case MutationTypes.StockCount: ...; } // classic switch
catch (Exception ex) when (attempt < 15) { ... }               // exception filters
```

### Async/await: nearly identical to JS

| C# | TS |
|---|---|
| `Task<T>` | `Promise<T>` |
| `Task` | `Promise<void>` |
| `async Task<Order> GetAsync()` | `async getAsync(): Promise<Order>` |
| `await Task.WhenAll(a, b)` | `await Promise.all([a, b])` |
| `CancellationToken ct` | `AbortSignal` |

Differences worth knowing:
- **Pass `CancellationToken` through everything.** ASP.NET Core gives you one per request (cancelled if the client disconnects). MassTransit gives `context.CancellationToken`. You'll see `ct` threaded through every method here.
- C# is **multi-threaded**. Two requests really can run your code at the same time on different threads. That's why `PriceExportConsumer` uses a `SemaphoreSlim` to serialise writes to one file. Node's single thread hides this.
- Convention: async methods end in `Async`.
- Never `.Result` or `.Wait()` on a Task (deadlock risk). Always `await`.

### LINQ = array methods (and SQL)

```csharp
order.Lines.Where(l => l.Quantity > 0)      // .filter
           .Select(l => l.Sku)              // .map
           .Distinct()                      // [...new Set()]
           .OrderBy(s => s)                 // .sort
           .ToList();                       // materialise
lines.Sum(l => l.UnitPrice * l.Quantity)    // .reduce
lines.Any(l => l.Quantity <= 0)             // .some
lines.GroupBy(s => s.StoreId)               // groupBy
```

The superpower: **the same LINQ against an EF `DbSet` is translated to SQL**:
```csharp
await db.StockItems
    .Where(s => s.StoreId == storeId && s.RowVersion > checkpoint)
    .OrderBy(s => s.RowVersion)
    .Take(501)
    .ToListAsync(ct);
// → SELECT TOP(501) ... FROM StockItems WHERE StoreId = @p0 AND RowVersion > @p1 ORDER BY RowVersion
```
Gotcha: a query is only executed when you call `ToListAsync`, `FirstOrDefaultAsync`, `AnyAsync` and so on. Until then it's an `IQueryable` (a query *description*).

### Generics and interfaces

```csharp
public interface IProductRepository { Task<Product?> GetAsync(string sku, CancellationToken ct); }
public class MongoProductRepository : IProductRepository { ... }   // `implements`
IConsumer<OrderSubmitted>                                          // generic interface, like Handler<T>
```

## ASP.NET Core in 5 minutes

### `Program.cs`: the composition root

Every service's `Program.cs` follows the same three steps:

```csharp
var builder = WebApplication.CreateBuilder(args);   // 1. config + logging + DI container

builder.Services.AddDbContext<InventoryDbContext>(...);   // 2. register services (DI)
builder.Services.AddScoped<SyncService>();
builder.Services.AddControllers();

var app = builder.Build();
app.UseExceptionHandler();                               // 3. middleware pipeline (like app.use() in Express)
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
```

### Dependency injection (built in)

Register types and the framework constructs them, injecting their constructor parameters. Lifetimes:

| Lifetime | Meaning | Used for |
|---|---|---|
| `Singleton` | One instance for the app | `MongoClient` (holds the connection pool), repositories over it |
| `Scoped` | One per HTTP request / per consumed message | `DbContext`, `SyncService` |
| `Transient` | New every time it's asked for | Lightweight stateless helpers |

Why is DbContext scoped? It tracks changes for one unit of work and isn't thread-safe, so one per request is right. A **singleton depending on a scoped service** is a bug ("captive dependency"). `BuildServiceProvider(validateScopes: true)` in the tests catches it.

### Controllers (MVC / Web API)

```csharp
[ApiController]                       // auto 400 on invalid model, binds JSON body
[Route("api/orders")]
public class OrdersController(OrdersDbContext db) : ControllerBase
{
    [HttpGet("{id:guid}")]            // GET /api/orders/{id}, route constraint
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct)
        => await db.Orders.FindAsync([id], ct) is { } o ? ToDto(o) : NotFound();

    [HttpPost]                         // body bound from JSON automatically
    public async Task<ActionResult<OrderDto>> Place(PlaceOrderRequest request, CancellationToken ct) { ... }
}
```
Return helpers: `Ok()`, `NotFound()`, `Conflict()`, `AcceptedAtAction()`, `ValidationProblem()`. Errors come back as **RFC 7807 ProblemDetails** JSON (`AddProblemDetails()`).

*Minimal APIs* (`app.MapGet("/x", () => ...)`) are the lighter alternative. This repo uses controllers because they organise larger APIs well.

### Configuration: `appsettings.json` + env vars

Configuration is layered: `appsettings.json` → `appsettings.{Environment}.json` → environment variables → command line. **Double underscore = nesting**:

```
Messaging__RabbitMq__Host=rabbitmq   →   configuration["Messaging:RabbitMq:Host"]
ConnectionStrings__InventoryDb=...   →   GetConnectionString("InventoryDb")
```
That's how the same image runs locally, in docker-compose, in k8s (ConfigMap/Secret) and in Azure.

**Options pattern** for typed config (`Epos.Etl/EtlOptions.cs`):
```csharp
builder.Services.Configure<EtlOptions>(builder.Configuration.GetSection("Etl"));
public class SalesImportWorker(IOptions<EtlOptions> options) { ... options.Value.InboundFolder ... }
```

### Background work: Worker Service

`Epos.Etl` uses `Host.CreateApplicationBuilder` (no web server) plus a `BackgroundService`:
```csharp
public class SalesImportWorker : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken)) { ... }
    }
}
```
`stoppingToken` fires on SIGTERM (e.g. k8s pod shutdown), giving you graceful shutdown for free.

### `using` (two meanings)

```csharp
using MongoDB.Driver;                       // import
using var reader = new StreamReader(path);  // dispose at end of scope (like try/finally close())
```

## Project and solution layout

```
backend/
  Directory.Build.props      ← every project: net10.0, nullable, implicit usings
  Directory.Packages.props   ← every NuGet version, pinned once
  BasecampRetail.slnx        ← new XML solution format (.NET 9+)
  src/.../X.csproj           ← <PackageReference Include="MassTransit" />  (no version!)
```

`<Project Sdk="Microsoft.NET.Sdk.Web">` gives a web app, `Sdk.Worker` a background service, and `Microsoft.NET.Sdk` a class library or tests.

## Handy CLI

```bash
dotnet new webapi -n Foo            # scaffold
dotnet add package MongoDB.Driver    # (with CPM, add version to Directory.Packages.props)
dotnet build / run / test / publish
dotnet watch run                     # hot reload, like nodemon
dotnet ef migrations add Initial     # EF migrations (dotnet tool install -g dotnet-ef)
dotnet test --filter "FullyQualifiedName~StockItem"
```
