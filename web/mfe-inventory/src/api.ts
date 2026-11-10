export interface StockRow {
  sku: string;
  onHand: number;
  reserved: number;
  available: number;
  reorderThreshold: number;
  isLow: boolean;
  lastCountedAt: string | null;
}

export function createInventoryApi(baseUrl: string) {
  const store = (id: string) => `${baseUrl}/api/inventory/stores/${encodeURIComponent(id)}`;

  return {
    async getStock(storeId: string, lowOnly: boolean): Promise<StockRow[]> {
      const res = await fetch(`${store(storeId)}/stock?lowOnly=${lowOnly}`);
      if (!res.ok) throw new Error(`Could not load stock (${res.status})`);
      return res.json();
    },

    async adjust(storeId: string, sku: string, delta: number, reason: string): Promise<StockRow> {
      const res = await fetch(`${store(storeId)}/stock/${encodeURIComponent(sku)}/adjustments`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ delta, reason }),
      });
      if (!res.ok) throw new Error(`Adjustment failed (${res.status})`);
      return res.json();
    },
  };
}
