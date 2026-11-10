using Basecamp.Messaging;
using Inventory.Api.Consumers;
using Inventory.Api.Data;
using Inventory.Api.Sync;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

// ---------------------------------------------------------------------------
// Inventory service: store stock positions (SQL Server via EF Core).
// - REST API for the admin portal + handheld sync
// - Consumes order + ePOS events from RabbitMQ
// - Publishes via the transactional outbox
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// Dependency injection registrations ("composition root").
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("InventoryDb")));

builder.Services.AddBasecampMessaging(builder.Configuration, x =>
{
    x.AddConsumer<OrderSubmittedConsumer>();
    x.AddConsumer<OrderPickedConsumer>();
    x.AddConsumer<SalesRecordedConsumer>();

    // Transactional outbox backed by our own SQL database.
    x.AddEntityFrameworkOutbox<InventoryDbContext>(o =>
    {
        o.UseSqlServer();
        // UseBusOutbox: publishes from controllers/services (not just consumers)
        // also go via the outbox table.
        o.UseBusOutbox();
        o.QueryDelay = TimeSpan.FromSeconds(1);
    });

    // Every consumer endpoint gets the inbox/outbox (idempotent consume + atomic publish).
    x.AddConfigureEndpointsCallback((context, _, cfg) =>
        cfg.UseEntityFrameworkOutbox<InventoryDbContext>(context));
});

builder.Services.AddScoped<SyncService>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();               // /openapi/v1.json
app.MapControllers();
app.MapHealthChecks("/health"); // used by docker-compose + k8s probes

if (app.Configuration.GetValue("Database:InitialiseOnStartup", true))
    await DatabaseInitializer.InitialiseAsync(app.Services);

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
