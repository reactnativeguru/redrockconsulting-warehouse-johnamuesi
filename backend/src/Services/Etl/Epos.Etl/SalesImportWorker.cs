using Basecamp.Contracts;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Epos.Etl;

/// <summary>
/// ePOS -> platform. Polls a folder for sales exports and publishes SalesRecorded events.
///
/// Extract:   read CSV files dropped by the ePOS (in real life: SFTP, blob storage, or an API)
/// Transform: SalesCsvParser -> SaleLine records, grouped by store
/// Load:      publish SalesRecorded to RabbitMQ; Inventory applies them to stock
///
/// Idempotency: BatchId = "{fileName}:{storeId}" is deterministic. If we crash after
/// publishing but before moving the file, the file is re-processed on restart and
/// Inventory ignores the duplicate batch (ProcessedKeys table).
/// </summary>
public class SalesImportWorker(IBus bus, IOptions<EtlOptions> options, ILogger<SalesImportWorker> logger)
    : BackgroundService
{
    private readonly EtlOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var dir in new[] { _options.InboundFolder, _options.ProcessedFolder, _options.ErrorFolder })
            Directory.CreateDirectory(dir);

        logger.LogInformation("Watching {Folder} for ePOS sales files", Path.GetFullPath(_options.InboundFolder));

        // PeriodicTimer: the modern, allocation-free way to run something on an interval.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollSeconds));
        do
        {
            foreach (var file in Directory.EnumerateFiles(_options.InboundFolder, "*.csv").Order())
            {
                try
                {
                    await ProcessFileAsync(file, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Leave the file in place; it will be retried next tick.
                    logger.LogError(ex, "Failed processing {File}; will retry", file);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ProcessFileAsync(string path, CancellationToken ct)
    {
        var fileName = Path.GetFileName(path);
        SalesCsvParser.ParseResult result;
        using (var reader = new StreamReader(path))
            result = SalesCsvParser.Parse(reader);

        if (result.Sales.Count == 0)
        {
            logger.LogWarning("{File} contained no valid sales ({Errors} errors) - moving to error folder",
                fileName, result.Errors.Count);
            await WriteErrorReportAsync(fileName, result.Errors, ct);
            File.Move(path, Path.Combine(_options.ErrorFolder, fileName), overwrite: true);
            return;
        }

        foreach (var storeGroup in result.Sales.GroupBy(s => s.StoreId))
        {
            await bus.Publish(new SalesRecorded(
                StoreId: storeGroup.Key,
                BatchId: $"{fileName}:{storeGroup.Key}",
                Lines: storeGroup.Select(s => s.Line).ToList(),
                RecordedAt: DateTimeOffset.UtcNow), ct);
        }

        if (result.Errors.Count > 0)
            await WriteErrorReportAsync(fileName, result.Errors, ct);

        File.Move(path, Path.Combine(_options.ProcessedFolder, fileName), overwrite: true);
        logger.LogInformation("Imported {Count} sales lines from {File} ({Errors} rejected rows)",
            result.Sales.Count, fileName, result.Errors.Count);
    }

    private Task WriteErrorReportAsync(string fileName, IReadOnlyList<SalesCsvParser.ParseError> errors, CancellationToken ct)
        => File.WriteAllLinesAsync(
            Path.Combine(_options.ErrorFolder, fileName + ".errors.txt"),
            errors.Select(e => $"line {e.LineNumber}: {e.Message}"), ct);
}
