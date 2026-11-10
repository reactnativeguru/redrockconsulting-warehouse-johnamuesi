using System.Text.Json.Serialization;
using Basecamp.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Catalog;
using Orders.Api.Consumers;
using Orders.Api.Data;

// ---------------------------------------------------------------------------
// Orders service: click & collect orders (SQL Server via EF Core).
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("OrdersDb")));

builder.Services.AddBasecampMessaging(builder.Configuration, x =>
{
    x.AddConsumer<StockReservedConsumer>();
    x.AddConsumer<StockReservationFailedConsumer>();

    x.AddEntityFrameworkOutbox<OrdersDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });
    x.AddConfigureEndpointsCallback((context, _, cfg) =>
        cfg.UseEntityFrameworkOutbox<OrdersDbContext>(context));
});

// Typed HttpClient + Polly-based resilience: retries with backoff, per-attempt timeout,
// circuit breaker. One line, sensible defaults.
builder.Services.AddHttpClient<CatalogClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:CatalogUrl"] ?? "http://localhost:5101/"))
    .AddStandardResilienceHandler();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<OrdersDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapControllers();
app.MapHealthChecks("/health");

// Dev convenience (see Inventory's DatabaseInitializer for the migrations note).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try { await db.Database.EnsureCreatedAsync(); break; }
        catch when (attempt < 15) { await Task.Delay(TimeSpan.FromSeconds(3)); }
    }
}

app.Run();

public partial class Program;
