using Epos.Etl;

namespace Epos.Etl.Tests;

public class SalesCsvParserTests
{
    private const string Header = "store_id,transaction_id,sku,quantity,unit_price,sold_at";

    private static SalesCsvParser.ParseResult Parse(params string[] lines) =>
        SalesCsvParser.Parse(new StringReader(string.Join('\n', lines)));

    [Fact]
    public void Parses_valid_rows()
    {
        var result = Parse(Header, "STORE-001,T1,BC-JKT-001,2,89.99,2026-11-09T09:15:00Z");

        var sale = Assert.Single(result.Sales);
        Assert.Empty(result.Errors);
        Assert.Equal("STORE-001", sale.StoreId);
        Assert.Equal(2, sale.Line.Quantity);
        Assert.Equal(89.99m, sale.Line.UnitPrice);
    }

    [Fact]
    public void Negative_quantity_is_a_refund_not_an_error()
    {
        var result = Parse(Header, "STORE-001,T1,BC-JKT-001,-1,89.99,2026-11-09T09:15:00Z");

        Assert.Equal(-1, Assert.Single(result.Sales).Line.Quantity);
    }

    [Fact]
    public void Bad_rows_are_reported_with_line_numbers_and_good_rows_still_parse()
    {
        var result = Parse(Header,
            "STORE-001,T1,BC-JKT-001,1,89.99,2026-11-09T09:15:00Z",
            "STORE-001,T2,BC-JKT-001,abc,89.99,2026-11-09T09:15:00Z",
            "STORE-001,T3,BC-JKT-001,1,89.99");

        Assert.Single(result.Sales);
        Assert.Collection(result.Errors,
            e => Assert.Equal(3, e.LineNumber),
            e => Assert.Equal(4, e.LineNumber));
    }

    [Fact]
    public void Wrong_header_rejects_the_file()
    {
        var result = Parse("store,sku,qty", "STORE-001,BC-JKT-001,1");

        Assert.Empty(result.Sales);
        Assert.Equal(1, Assert.Single(result.Errors).LineNumber);
    }

    [Fact]
    public void Prices_are_parsed_with_invariant_culture()
    {
        // "89,99" (comma decimal) shifts the column count -> rejected, never silently misread.
        var result = Parse(Header, "STORE-001,T1,BC-JKT-001,1,89,99,2026-11-09T09:15:00Z");

        Assert.Empty(result.Sales);
        Assert.Single(result.Errors);
    }
}
