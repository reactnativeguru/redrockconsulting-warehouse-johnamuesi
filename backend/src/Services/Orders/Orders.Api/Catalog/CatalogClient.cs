using System.Net.Http.Json;

namespace Orders.Api.Catalog;

public record CatalogProduct(string Sku, string Name, decimal Price);

/// <summary>
/// Typed HttpClient for the Catalog service.
///
/// Why a synchronous call here (rather than events)? When a customer places an order
/// we need the CURRENT authoritative price, right now. That's a query, and a query
/// is a good fit for HTTP. The resilience pipeline (retry, timeout, circuit breaker)
/// is configured in Program.cs via AddStandardResilienceHandler().
///
/// Alternative worth discussing: Orders keeps its own local price table, updated
/// from ProductPriceChanged events. No runtime dependency on Catalog, but prices can
/// be briefly stale (eventual consistency).
/// </summary>
public class CatalogClient(HttpClient http)
{
    public async Task<IReadOnlyDictionary<string, CatalogProduct>> GetProductsAsync(
        IEnumerable<string> skus, CancellationToken ct)
    {
        var query = string.Join(",", skus.Distinct());
        var products = await http.GetFromJsonAsync<List<CatalogProduct>>(
            $"api/products/lookup?skus={Uri.EscapeDataString(query)}", ct) ?? [];
        return products.ToDictionary(p => p.Sku);
    }
}
