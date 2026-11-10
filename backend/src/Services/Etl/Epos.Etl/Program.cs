using Basecamp.Messaging;
using Epos.Etl;

// ---------------------------------------------------------------------------
// ePOS ETL worker: no HTTP API, just a long-running background process.
// "Generic Host" gives us DI, config, logging and graceful shutdown for free.
// ---------------------------------------------------------------------------
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<EtlOptions>(builder.Configuration.GetSection(EtlOptions.Section));

builder.Services.AddBasecampMessaging(builder.Configuration, x =>
{
    x.AddConsumer<PriceExportConsumer>();
});

builder.Services.AddHostedService<SalesImportWorker>();

builder.Build().Run();
