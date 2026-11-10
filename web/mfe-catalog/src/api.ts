export interface ProductSummary {
  sku: string;
  name: string;
  category: string;
  price: number;
  barcode: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export function createCatalogApi(baseUrl: string) {
  return {
    async search(search: string, page = 1): Promise<PagedResult<ProductSummary>> {
      const qs = new URLSearchParams({ search, page: String(page), pageSize: '20' });
      const res = await fetch(`${baseUrl}/api/products?${qs}`);
      if (!res.ok) throw new Error(`Search failed (${res.status})`);
      return res.json();
    },

    async changePrice(sku: string, price: number): Promise<void> {
      const res = await fetch(`${baseUrl}/api/products/${encodeURIComponent(sku)}/price`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ price }),
      });
      if (!res.ok) throw new Error(`Price change failed (${res.status})`);
    },
  };
}
