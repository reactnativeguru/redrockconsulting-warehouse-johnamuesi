using Basecamp.Messaging;
using Catalog.Api.Data;
using MongoDB.Driver;

// ---------------------------------------------------------------------------
// Catalog service: product information (MongoDB).
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// MongoClient is thread-safe and holds the connection pool -> register as singleton.
builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration.GetConnectionString("CatalogDb")));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase(builder.Configuration["Mongo:Database"] ?? "catalog"));
builder.Services.AddSingleton<MongoProductRepository>();
builder.Services.AddSingleton<IProductRepository>(sp => sp.GetRequiredService<MongoProductRepository>());

// Catalog only publishes; it has no consumers.
builder.Services.AddBasecampMessaging(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapControllers();
app.MapHealthChecks("/health");

// Indexes + demo data.
var repo = app.Services.GetRequiredService<MongoProductRepository>();
await repo.EnsureIndexesAsync();
if (app.Configuration.GetValue("Mongo:Seed", true))
    foreach (var product in CatalogSeed.Products)
        if (await repo.GetAsync(product.Sku, default) is null)
            await repo.UpsertAsync(product, default);

app.Run();

public partial class Program;
