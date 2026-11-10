using System.Globalization;
using Basecamp.Contracts;

namespace Epos.Etl;

/// <summary>
/// The "T" in ETL: turn the ePOS export format into our domain events.
///
/// Expected format (header required):
///   store_id,transaction_id,sku,quantity,unit_price,sold_at
///   STORE-001,T10001,BC-JKT-001,1,89.99,2026-11-09T09:15:00Z
///
/// Negative quantity = refund. Bad rows are collected as errors rather than
/// failing the whole file - one typo shouldn't block a store's whole day of sales.
///
/// Pure function (TextReader in, result out) so it's trivially unit testable.
/// </summary>
public static class SalesCsvParser
{
    private static readonly string[] ExpectedHeader =
        ["store_id", "transaction_id", "sku", "quantity", "unit_price", "sold_at"];

    public sealed record ParsedSale(string StoreId, SaleLine Line);
    public sealed record ParseError(int LineNumber, string Message);
    public sealed record ParseResult(IReadOnlyList<ParsedSale> Sales, IReadOnlyList<ParseError> Errors);

    public static ParseResult Parse(TextReader reader)
    {
        var sales = new List<ParsedSale>();
        var errors = new List<ParseError>();

        var header = reader.ReadLine();
        if (header is null || !header.Split(',').Select(h => h.Trim().ToLowerInvariant()).SequenceEqual(ExpectedHeader))
        {
            errors.Add(new ParseError(1, $"Header must be: {string.Join(',', ExpectedHeader)}"));
            return new ParseResult(sales, errors);
        }

        var lineNumber = 1;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            // NOTE: simple split - the ePOS export never quotes fields. If it did,
            // swap this for a proper CSV library (e.g. CsvHelper).
            var cols = line.Split(',').Select(c => c.Trim()).ToArray();
            if (cols.Length != ExpectedHeader.Length)
            {
                errors.Add(new ParseError(lineNumber, $"Expected {ExpectedHeader.Length} columns, got {cols.Length}"));
                continue;
            }

            var (storeId, txId, sku) = (cols[0], cols[1], cols[2]);
            if (storeId.Length == 0 || txId.Length == 0 || sku.Length == 0)
            {
                errors.Add(new ParseError(lineNumber, "store_id, transaction_id and sku are required"));
                continue;
            }
            if (!int.TryParse(cols[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var qty) || qty == 0)
            {
                errors.Add(new ParseError(lineNumber, $"Invalid quantity '{cols[3]}'"));
                continue;
            }
            // InvariantCulture: never let the server's locale decide whether "89,99" is a number.
            if (!decimal.TryParse(cols[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
            {
                errors.Add(new ParseError(lineNumber, $"Invalid unit_price '{cols[4]}'"));
                continue;
            }
            if (!DateTimeOffset.TryParse(cols[5], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var soldAt))
            {
                errors.Add(new ParseError(lineNumber, $"Invalid sold_at '{cols[5]}'"));
                continue;
            }

            sales.Add(new ParsedSale(storeId, new SaleLine(txId, sku, qty, price, soldAt)));
        }

        return new ParseResult(sales, errors);
    }
}
