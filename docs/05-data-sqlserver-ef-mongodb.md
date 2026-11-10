# 05 — Data: SQL Server + EF Core, and MongoDB

This repo uses both SQL Server and MongoDB, deliberately, for different reasons.

## Choosing: relational vs document

| Ask yourself | Leans relational (SQL Server) | Leans document (MongoDB) |
|---|---|---|
| Concurrency-critical counters / money movements? | ✅ ACID transactions, row locking, rowversion | single-document atomicity only (multi-doc needs a replica set) |
| Shape stable and relational (lines ↔ orders ↔ customers)? | ✅ joins, FKs, constraints | |
| Shape varies per record (tent has berths, boot has sizes)? | | ✅ flexible schema, embedded arrays |
| Read whole aggregate in one go, read-heavy? | | ✅ one document = one read, no joins |
| Ad-hoc reporting / BI? | ✅ SQL, mature tooling | aggregation pipeline (powerful, less familiar to analysts) |

**Here:** Catalog → Mongo (varied product attributes, read-heavy). Inventory and Orders → SQL Server (counters, concurrency, state transitions, reporting).

---

## Entity Framework Core (SQL Server)

EF Core is an ORM: C# classes ↔ tables, LINQ ↔ SQL. It's to .NET what Prisma/TypeORM are to Node.

### DbContext = unit of work + change tracker
```csharp
var item = await db.StockItems.SingleAsync(s => s.StoreId == id && s.Sku == sku); // SELECT, tracked
item.Adjust(-1, "Damaged");                                                        // change in memory
await db.SaveChangesAsync();                                                       // UPDATE ... (one transaction)
```
EF snapshots tracked entities, so `SaveChanges` diffs and emits only the changed columns. Everything added or changed before `SaveChanges` commits **atomically**. The outbox relies on this.

### Mapping (Fluent API in `OnModelCreating`)
```csharp
e.HasIndex(x => new { x.StoreId, x.Sku }).IsUnique();       // unique index
e.Property(x => x.Status).HasConversion<string>();           // enum stored as text
e.OwnsMany(o => o.Lines, l => l.ToTable("OrderLines"));       // owned collection (part of aggregate)
e.Property(x => x.UnitPrice).HasPrecision(18, 2);            // decimal(18,2)
e.Ignore(x => x.Available);                                  // computed in C#, not a column
```
Entities keep **private setters** and a **private parameterless constructor**. EF can still hydrate them, but your code must go through methods like `Reserve()`. That's a rich domain model, not an anemic one.

### Optimistic concurrency with `rowversion` ⭐
```csharp
public ulong RowVersion { get; private set; }
e.Property(x => x.RowVersion).IsRowVersion().HasConversion<byte[]>();
```
- SQL Server auto-increments a `rowversion` (8 bytes) on **every** write to the row.
- EF adds it to the `WHERE` of every UPDATE: `UPDATE ... WHERE Id=@id AND RowVersion=@original`.
- 0 rows affected → `DbUpdateConcurrencyException` → retry with fresh data.
- **Bonus use:** rowversion is database-wide and monotonic, so it's a perfect **sync checkpoint**: "give me rows with RowVersion > X". The handheld sync is built on this. It's sent to JS **as a string** because 64-bit integers exceed `Number.MAX_SAFE_INTEGER` (2⁵³).

### Performance habits to mention
- `AsNoTracking()` for read-only queries (skips change-tracker overhead). Used in every GET.
- Project to DTOs (`Select(x => new Dto(...))`) to fetch only needed columns.
- Avoid **N+1**: use `Include()` or a projection rather than lazy-loading in a loop.
- Index for your `WHERE`/`ORDER BY`: `(StoreId, Sku)` unique, `(StoreId, RowVersion)` for sync.
- Paginate (`Take`). Pull pages are 500 rows plus a `HasMore` flag.
- Check the generated SQL: `optionsBuilder.LogTo(Console.WriteLine)` or the EF log category.

### Migrations (production)
```bash
dotnet tool install -g dotnet-ef
dotnet ef migrations add Initial -p src/Services/Inventory/Inventory.Api
dotnet ef migrations script --idempotent -o inventory.sql   # CI artifact, reviewed + applied in release
```
The demo uses `EnsureCreated()` for zero-setup startup. Know why you'd **not** do that in prod: there's no evolution path, and you'd lose data on schema changes.

### Legacy note
Real estates are often a mixture of greenfield and legacy. Legacy .NET Framework apps often use **EF6** (not Core) and **ASP.NET MVC 5** with Razor views. Same concepts (DbContext, LINQ, migrations), different packages and hosting (IIS, `Web.config`).

---

## MongoDB (.NET driver)

### Document model (`Catalog.Api/Models/Product.cs`)
```json
{
  "_id": "BC-TENT-001",
  "Name": "Basecamp 2-Person Tent",
  "Category": "Camping",
  "Price": NumberDecimal("149.99"),
  "Barcode": "5060000000059",
  "Colours": ["Green"],
  "Sizes": [],
  "Attributes": { "berths": "2", "weightKg": "2.4" }
}
```
- `[BsonId]` on `Sku`: the natural key is the `_id` (no separate surrogate key).
- `[BsonRepresentation(BsonType.Decimal128)]`: **never store money as double**.
- **Embedding vs referencing**: embed what you always read together and what's bounded (colours, sizes, attributes). Reference what's unbounded or shared (stock levels across 300 stores → a separate service entirely).

### Driver usage
```csharp
var filter = Builders<Product>.Filter.Eq(p => p.Category, "Jackets")
           & Builders<Product>.Filter.Regex(p => p.Name, new BsonRegularExpression(Regex.Escape(term), "i"));
await collection.Find(filter).SortBy(p => p.Name).Skip(0).Limit(20).ToListAsync();

// Atomic single-document update, returning the BEFORE image (for the price-change event):
await collection.FindOneAndUpdateAsync(filter, Builders<Product>.Update.Set(p => p.Price, 7.99m),
    new FindOneAndUpdateOptions<Product> { ReturnDocument = ReturnDocument.Before });
```
- `Regex.Escape` prevents regex injection from user input.
- **Indexes are code**, created idempotently at startup (`EnsureIndexesAsync`): a unique index on `Barcode`, a compound index on `Category, Name`.
- `MongoClient` is a **singleton** (it holds the connection pool).
- Single-document writes are atomic. Multi-document transactions need a **replica set** (Atlas and Cosmos provide one; local docker needs `--replSet`).

### Cosmos DB for MongoDB (Azure)
The Azure deployment uses Cosmos DB's MongoDB API. The same driver and code, but be aware of RU-based throughput, some unsupported operators, and different index behaviour. Alternatively, MongoDB Atlas runs on Azure too.
