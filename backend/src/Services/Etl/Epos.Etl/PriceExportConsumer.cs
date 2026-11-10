using System.Globalization;
using Basecamp.Contracts;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Epos.Etl;

/// <summary>
/// Platform -> ePOS. When head office changes a price, append it to today's price
/// file, which the tills import (the ePOS is a third-party product we can't change,
/// so files are the integration contract).
/// </summary>
public class PriceExportConsumer(IOptions<EtlOptions> options, ILogger<PriceExportConsumer> logger)
    : IConsumer<ProductPriceChanged>
{
    // Consumers can run concurrently; serialise writes to the shared file.
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public async Task Consume(ConsumeContext<ProductPriceChanged> context)
    {
        var msg = context.Message;
        var folder = options.Value.OutboundFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"prices_{msg.ChangedAt:yyyyMMdd}.csv");

        await FileLock.WaitAsync(context.CancellationToken);
        try
        {
            if (!File.Exists(path))
                await File.WriteAllTextAsync(path, "sku,price,effective_at" + Environment.NewLine, context.CancellationToken);

            var line = string.Create(CultureInfo.InvariantCulture, $"{msg.Sku},{msg.NewPrice:0.00},{msg.ChangedAt:O}");
            await File.AppendAllTextAsync(path, line + Environment.NewLine, context.CancellationToken);
        }
        finally
        {
            FileLock.Release();
        }

        logger.LogInformation("Exported price change {Sku} {Old} -> {New}", msg.Sku, msg.OldPrice, msg.NewPrice);
    }
}
