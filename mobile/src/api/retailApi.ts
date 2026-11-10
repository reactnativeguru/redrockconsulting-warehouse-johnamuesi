import type { HttpClient } from './http';
import type { Order, Product, PullResponse, PushRequest, PushResponse } from './types';

/** Everything the app needs from the backend (all calls go via the API gateway). */
export interface RetailApi {
  getProduct(skuOrBarcode: string): Promise<Product>;
  pushInventory(request: PushRequest): Promise<PushResponse>;
  pullInventory(storeId: string, since: string): Promise<PullResponse>;
  getPickList(storeId: string): Promise<Order[]>;
  markOrderPicked(orderId: string, idempotencyKey: string): Promise<Order>;
}

const isBarcode = (value: string) => /^\d{8,14}$/.test(value);

export function createRetailApi(http: HttpClient): RetailApi {
  return {
    getProduct: (value) =>
      isBarcode(value)
        ? http.get<Product>(`/api/products/barcode/${encodeURIComponent(value)}`)
        : http.get<Product>(`/api/products/${encodeURIComponent(value.toUpperCase())}`),

    pushInventory: (request) => http.post<PushResponse>('/api/inventory/sync/push', request),

    pullInventory: (storeId, since) =>
      http.get<PullResponse>(
        `/api/inventory/sync/pull?storeId=${encodeURIComponent(storeId)}&since=${encodeURIComponent(since)}`,
      ),

    getPickList: (storeId) =>
      http.get<Order[]>(`/api/orders?storeId=${encodeURIComponent(storeId)}&status=ReadyToPick`),

    markOrderPicked: (orderId, idempotencyKey) =>
      http.post<Order>(`/api/orders/${orderId}/picked`, undefined, { 'Idempotency-Key': idempotencyKey }),
  };
}
